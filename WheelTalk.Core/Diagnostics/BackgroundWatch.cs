using Microsoft.Extensions.Logging;
using WheelTalk.Core.Services;

namespace WheelTalk.Core.Diagnostics;

/// <summary>
/// Замечает, что фоновую работу остановили не мы. Пока сессия занята колесом, раз в
/// <see cref="BackgroundBeat.Period"/> кладёт на диск короткую отметку «жив, фаза, время»; на
/// возвращении к людям сравнивает последнюю отметку с «сейчас» и, если пропуск велик
/// (<see cref="BackgroundBeat.Missed"/>), говорит об этом — строкой в журнал всегда и один раз
/// человеку (<see cref="TakeGap"/>).
/// <para>
/// <b>Почему не хватает метки падения.</b> <c>running.marker</c> отвечает на вопрос «прошлый
/// процесс попрощался?» и спрашивается ровно один раз — при создании процесса. Случай владельца
/// 13.08.2026 в неё не попадает вовсе: процесс не умирал. EMUI заморозил его посреди погони
/// (последний кадр BLE — 20:36:37, оборван на середине; кольцо кадров и сам процесс дожили до
/// 23:13), и все два с половиной часа приложение не делало ничего: ни попыток подключения, ни
/// строки в журнале, ни встречи включённого в 23:05 колеса. Нового старта не случилось — значит и
/// спросить метку было некому. Поэтому здесь мерится <b>пропуск во времени</b>, а не факт
/// прощания: он одинаково виден и убитому процессу (отметка осталась на диске), и замороженному
/// (отметка осталась в памяти).
/// </para>
/// <para>
/// <b>Ложных тревог не плодит.</b> Отметка живёт ровно столько, сколько незаконченная работа:
/// сессия ушла в <see cref="ConnectionState.Disconnected"/> — таймер снят, файл удалён. Значит и
/// штатное отключение, и выход из приложения, и снятие руками при неактивной фазе оставляют
/// молчание.
/// </para>
/// </summary>
public sealed partial class BackgroundWatch : IDisposable
{
    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly ILogger<BackgroundWatch> _logger;
    private readonly Lock _gate = new();
    private readonly IDisposable _states;

    private ConnectionState _phase = ConnectionState.Disconnected;

    /// <summary>Наша последняя отметка — она же зеркало файла. <c>null</c> значит «не при делах».</summary>
    private BackgroundBeat? _last;

    private ITimer? _beats;

    /// <summary>Пропуск, о котором человеку ещё не сказали. Забирается один раз — <see cref="TakeGap"/>.</summary>
    private TimeSpan? _unsaid;

    public BackgroundWatch(
        string path, IObservable<ConnectionState> states, TimeProvider time, ILogger<BackgroundWatch> logger)
    {
        _path = path;
        _time = time;
        _logger = logger;

        // След прошлого запуска читается ДО подписки, и это не порядок ради порядка: подписка
        // приходит с текущим состоянием сессии, а оно на старте Disconnected — первое же
        // уведомление стёрло бы файл вместе с уликой.
        Notice(Read(), time.GetUtcNow());

        _states = states.Subscribe(OnState);
    }

    /// <summary>
    /// Пропуск, о котором стоит сказать человеку, — и заодно повод проверить себя: спрашивают это
    /// на возвращении экрана, то есть ровно в тот миг, когда замороженный процесс оттаял и его
    /// собственная отметка оказалась старой. Ждать очередного тика таймера тут нельзя — он придёт
    /// когда угодно, в том числе позже показа.
    /// <para>Возвращает раз: сказанное однажды не повторяется до следующего перерыва.</para>
    /// </summary>
    public TimeSpan? TakeGap()
    {
        lock (_gate)
        {
            NoticeOwnGap(_time.GetUtcNow());

            var gap = _unsaid;
            _unsaid = null;
            return gap;
        }
    }

    public void Dispose()
    {
        _states.Dispose();

        lock (_gate)
        {
            _beats?.Dispose();
            _beats = null;
        }
    }

    /// <summary>
    /// Файл не трогается: снятие наблюдателя — не признак того, что работа кончилась штатно. Гасит
    /// отметку только уход сессии в <see cref="ConnectionState.Disconnected"/>.
    /// </summary>
    private void OnState(ConnectionState state)
    {
        lock (_gate)
        {
            _phase = state;
            if (state == ConnectionState.Disconnected)
            {
                StopBeating();
                return;
            }

            _beats ??= _time.CreateTimer(_ => Beat(), null, BackgroundBeat.Period, BackgroundBeat.Period);

            // Отметка в памяти — да, на диск — нет: сюда приходят потоком той стороны, что меняла
            // состояние (подключение ждут с экрана, значит это бывает и главный поток), а диску там
            // не место. Файл пишет один только таймер, и первая запись случится через период.
            Alive(_time.GetUtcNow(), toDisk: false);
        }
    }

    private void Beat()
    {
        lock (_gate) Alive(_time.GetUtcNow(), toDisk: true);
    }

    /// <summary>
    /// «Живы вот в этот миг». Прежде чем записать новую отметку, считает разрыв со старой: тик,
    /// пришедший много позже срока, — это и есть оттаявший процесс, между отметками не работал никто.
    /// </summary>
    private void Alive(DateTimeOffset now, bool toDisk)
    {
        NoticeOwnGap(now);

        _last = new BackgroundBeat(now, _phase);
        if (toDisk) Write(_last.Value);
    }

    private void StopBeating()
    {
        // Перерыв досчитывается и здесь: то, что фон стоял два часа, остаётся правдой, даже если к
        // моменту разбора райдер уже нажал «Отключить».
        NoticeOwnGap(_time.GetUtcNow());

        _beats?.Dispose();
        _beats = null;
        _last = null;
        Delete();
    }

    /// <summary>Пропуск по своей же прошлой отметке — та же мера, что и на старте, только из памяти.</summary>
    private void NoticeOwnGap(DateTimeOffset now)
    {
        if (_last is not { } last) return;

        // Перерыв засчитывается один раз: дальше счёт идёт от этого мига, иначе один и тот же
        // пропуск попадал бы в журнал и на тике таймера, и на возвращении экрана.
        _last = last with { At = now };
        Notice(last, now);
    }

    private void Notice(BackgroundBeat? beat, DateTimeOffset now)
    {
        if (beat is not { } last || BackgroundBeat.Gap(last, now) is not { } gap) return;

        // Свежий перерыв важнее давнего: человеку показывается последний, в журнале остаются оба.
        _unsaid = gap;
        LogStopped((int)gap.TotalMinutes, last.Phase);
    }

    private BackgroundBeat? Read()
    {
        try
        {
            return File.Exists(_path) ? BackgroundBeat.Parse(File.ReadAllText(_path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Write(BackgroundBeat beat)
    {
        try
        {
            if (Path.GetDirectoryName(_path) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
            File.WriteAllText(_path, beat.Format());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Отметка — удобство разбора. Уронить из-за неё поездку было бы смешно.
        }
    }

    private void Delete()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Свой файл в своей папке не удаляется разве что при отвалившейся карте памяти. Ценой
            // будет одно лишнее сообщение на следующем запуске — приемлемо; упасть здесь нельзя.
        }
    }

    [LoggerMessage(EventId = 1320, EventName = "Background.Stopped", Level = LogLevel.Warning,
        Message = "Background.Stopped — фоновая работа стояла {Minutes} мин, фаза {Phase}")]
    private partial void LogStopped(int minutes, ConnectionState phase);
}
