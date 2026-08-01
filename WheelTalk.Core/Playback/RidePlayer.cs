using System.Reactive.Subjects;
using WheelTalk.Core.Contracts;

namespace WheelTalk.Core.Playback;

/// <summary>
/// Один отсчёт записанной поездки: сколько прошло от её начала, когда это было по часам и что
/// колесо тогда сказало.
/// <para>
/// Настенное время хранится рядом со смещением, а не считается из него: смещение отвечает на
/// вопрос «где я в записи», а часы — на вопрос «когда это было», и разбирают запись именно ради
/// второго (docs/playback-plan.md §1). Строка базы его и так содержит, так что стоит оно ничего.
/// </para>
/// </summary>
public sealed record RideSample(TimeSpan At, DateTimeOffset Stamp, TelemetrySnapshot Snapshot);

/// <summary>
/// Проигрыватель записанной поездки: пуск, пауза, перемотка, скорость. Отдаёт те же
/// <see cref="TelemetrySnapshot"/>, что живое колесо, поэтому панель и всё, что от снэпшотов
/// зависит, работает без единой правки.
/// <para>
/// Это не транспорт. <c>ReplayTransport</c> подаёт в декодер сырые байты и потому годится для
/// отладки протокола; здесь источник — таблица <c>telemetry</c>, где лежат уже разобранные
/// величины, и декодеру в этой цепочке делать нечего. Отсюда же и главное свойство: перемотка
/// стоит поиска по отсортированному списку, а не прокрутки файла с начала.
/// </para>
/// <para>
/// Отсчёты передаются готовым списком, а не читаются отсюда: ядро не знает про базу (это её слой
/// зависит от ядра, не наоборот), и заодно проигрыватель проверяется тестами без файла и без SQLite.
/// </para>
/// </summary>
public sealed class RidePlayer : IDisposable
{
    /// <summary>
    /// Как часто проигрыватель просыпается. Сорок миллисекунд — заметно чаще, чем колесо шлёт
    /// отсчёты (пять раз в секунду), так что на обычной скорости очередь отсчётов не копится, и
    /// заметно реже кадра панели: кадр рисуется по своему vsync и берёт последнее, что пришло.
    /// </summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(40);

    private readonly IReadOnlyList<RideSample> _samples;
    private readonly TimeProvider _time;
    private readonly Subject<TelemetrySnapshot> _telemetry = new();
    private readonly ITimer _timer;
    private readonly Lock _gate = new();

    private long _lastTick;
    private int _next;
    private TimeSpan _position;
    private double _speed = 1;
    private bool _playing;
    private RideSample? _current;

    public RidePlayer(IReadOnlyList<RideSample> samples, TimeProvider time)
    {
        _samples = samples;
        _time = time;
        Duration = samples.Count > 0 ? samples[^1].At : TimeSpan.Zero;
        _timer = time.CreateTimer(_ => OnTick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Поток отсчётов — тот же контракт, что у <c>WheelSession.Telemetry</c>.</summary>
    public IObservable<TelemetrySnapshot> Telemetry => _telemetry;

    /// <summary>Длительность записи: время последнего отсчёта от начала поездки.</summary>
    public TimeSpan Duration { get; }

    public TimeSpan Position
    {
        get { lock (_gate) return _position; }
    }

    public bool IsPlaying
    {
        get { lock (_gate) return _playing; }
    }

    /// <summary>Множитель хода: 1 — как было, 2 и 4 — быстрее. Меняется на ходу.</summary>
    public double Speed
    {
        get { lock (_gate) return _speed; }
        set
        {
            lock (_gate)
            {
                // Отсчёт времени сдвигается на «сейчас»: иначе прошедшее с прошлого тика посчиталось
                // бы по новому множителю, и смена скорости давала бы скачок положения.
                _lastTick = _time.GetTimestamp();
                _speed = value <= 0 ? 1 : value;
            }
        }
    }

    /// <summary>
    /// Отсчёт, который показан сейчас, — с его настенным временем. <c>null</c>, пока не показано
    /// ничего (пустая запись).
    /// </summary>
    public RideSample? Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>Положение и состояние изменились — полосе прокрутки пора обновиться.</summary>
    public event Action? Changed;

    /// <summary>
    /// Положение изменилось скачком — перемоткой или перезапуском с конца. Всё, что копится по
    /// ходу записи (след поездки с его минимумами и максимумами), после скачка врёт: оно набрано
    /// из другого места записи, а иногда из того, которое ещё не наступило. Подписчик обязан
    /// начать копить заново.
    /// </summary>
    public event Action? Jumped;

    public void Play()
    {
        RideSample? restarted = null;
        lock (_gate)
        {
            if (_playing) return;

            // Пуск с самого конца начинает запись заново: иначе кнопка не делала бы ничего, и это
            // читалось бы как поломка. Первый кадр выдаётся сразу же — как при перемотке: иначе
            // до следующего отсчёта на панели оставалась бы концовка прошлого прохода.
            if (_position >= Duration) restarted = SeekLocked(TimeSpan.Zero);

            _playing = true;
            _lastTick = _time.GetTimestamp();
            _timer.Change(Tick, Tick);
        }

        if (restarted is not null)
        {
            Jumped?.Invoke();
            _telemetry.OnNext(restarted.Snapshot);
        }

        Changed?.Invoke();
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (!_playing) return;
            _playing = false;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Перемотка. Отсчёт под новым положением выдаётся сразу же, не дожидаясь хода: на паузе
    /// перемотка обязана показывать то место, куда её привели, — иначе непонятно, куда попал.
    /// </summary>
    public void Seek(TimeSpan position)
    {
        RideSample? at;
        lock (_gate)
        {
            at = SeekLocked(position);
        }

        Jumped?.Invoke();
        if (at is not null) _telemetry.OnNext(at.Snapshot);
        Changed?.Invoke();
    }

    private RideSample? SeekLocked(TimeSpan position)
    {
        _position = position < TimeSpan.Zero ? TimeSpan.Zero
            : position > Duration ? Duration
            : position;
        _lastTick = _time.GetTimestamp();

        _next = IndexAfter(_position);
        _current = _next > 0 ? _samples[_next - 1] : _samples.Count > 0 ? _samples[0] : null;
        return _current;
    }

    private void OnTick()
    {
        var due = new List<TelemetrySnapshot>();
        bool ended = false;

        lock (_gate)
        {
            if (!_playing) return;

            long now = _time.GetTimestamp();
            _position += _time.GetElapsedTime(_lastTick, now) * _speed;
            _lastTick = now;

            if (_position >= Duration)
            {
                _position = Duration;
                _playing = false;
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                ended = true;
            }

            // Выдаются все отсчёты, попавшие в прошедший отрезок, а не только последний: на
            // ускоренном ходу их несколько за тик, и след поездки на ленте строится по каждому —
            // пропуски исказили бы минимумы и максимумы, ради которых на след и смотрят.
            while (_next < _samples.Count && _samples[_next].At <= _position)
            {
                _current = _samples[_next];
                due.Add(_current.Snapshot);
                _next++;
            }
        }

        foreach (var snapshot in due) _telemetry.OnNext(snapshot);
        if (due.Count > 0 || ended) Changed?.Invoke();
    }

    /// <summary>Индекс первого отсчёта строго позже указанного времени — двоичным поиском по возрастающему списку.</summary>
    private int IndexAfter(TimeSpan position)
    {
        int low = 0, high = _samples.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (_samples[mid].At <= position) low = mid + 1;
            else high = mid;
        }

        return low;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _telemetry.Dispose();
    }
}
