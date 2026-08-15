using Microsoft.Extensions.Logging;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Services;

namespace WheelTalk.Core.Diagnostics;

/// <summary>
/// Замечает, что фоновую работу остановили не мы. Пока сессия занята колесом, раз в
/// <see cref="BackgroundBeat.Period"/> кладёт на диск короткую отметку «работа шла, фаза, время»;
/// когда работа возобновляется, сравнивает отметку с «сейчас» и, если пропуск велик
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
/// <b>Меряется простой работы, а не простой таймера.</b> Первая мера считала перерыв законченным
/// на первом же тике сердцебиения — и врала вдвое: EMUI размораживает процесс на минуту-другую, а
/// настоящая работа (кадры с колеса) не возобновляется ещё долго. Разбор полной ленты 15.08.2026
/// поймал это дважды: 29 минут заморозки попали в журнал как 13, а слитные 74 — пятью обрывками
/// по 65 в сумме. Поэтому перерыв закрывает <b>кадр с колеса</b> (см. <see cref="OnFrame"/>) или
/// возвращение к людям, а тик — лишь доказательство того, что мы дышим, и то не сразу.
/// </para>
/// <para>
/// <b>Ложных тревог не плодит.</b> Отметка живёт ровно столько, сколько незаконченная работа:
/// сессия ушла в <see cref="ConnectionState.Disconnected"/> — таймер снят, файл удалён. Значит и
/// штатное отключение, и выход из приложения, и снятие руками при неактивной фазе оставляют
/// молчание. Выключенное колесо тоже молчания не нарушает: перерыв растёт, только пока не доказано
/// обратное, а <see cref="BackgroundBeat.Missed"/> ровного дыхания подряд — уже доказательство
/// (см. <see cref="Beat"/>).
/// </para>
/// </summary>
public sealed partial class BackgroundWatch : IDisposable
{
    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly ILogger<BackgroundWatch> _logger;
    private readonly Lock _gate = new();
    private readonly IDisposable _states;
    private readonly IDisposable _frames;

    private ConnectionState _phase = ConnectionState.Disconnected;

    /// <summary>
    /// Миг, когда работа шла в последний раз, и дело, за которым она шла, — он же зеркало файла.
    /// <c>null</c> значит «не при делах». От него и мерится перерыв: не от последнего тика.
    /// </summary>
    private BackgroundBeat? _worked;

    /// <summary>Последнее сердцебиение. По нему видно оттаивание — тик, пришедший много позже срока.</summary>
    private DateTimeOffset _beatAt;

    /// <summary>С какого мига сердцебиение идёт без пропусков; сдвигается каждым оттаиванием.</summary>
    private DateTimeOffset _awakeSince;

    private ITimer? _beats;

    /// <summary>Пропуск, о котором человеку ещё не сказали. Забирается один раз — <see cref="TakeGap"/>.</summary>
    private BackgroundGap? _unsaid;

    public BackgroundWatch(
        string path,
        IObservable<ConnectionState> states,
        IObservable<TelemetrySnapshot> frames,
        TimeProvider time,
        ILogger<BackgroundWatch> logger)
    {
        _path = path;
        _time = time;
        _logger = logger;

        // След прошлого запуска читается ДО подписки, и это не порядок ради порядка: подписка
        // приходит с текущим состоянием сессии, а оно на старте Disconnected — первое же
        // уведомление стёрло бы файл вместе с уликой.
        Notice(Read(), time.GetUtcNow());

        _states = states.Subscribe(OnState);
        _frames = frames.Subscribe(_ => OnFrame());
    }

    /// <summary>
    /// Пропуск, о котором стоит сказать человеку, — и заодно повод проверить себя: спрашивают это
    /// на возвращении экрана, то есть ровно в тот миг, когда замороженный процесс оттаял и его
    /// собственная отметка оказалась старой. Ждать очередного тика таймера тут нельзя — он придёт
    /// когда угодно, в том числе позже показа.
    /// <para>Возвращает раз: сказанное однажды не повторяется до следующего перерыва.</para>
    /// </summary>
    public BackgroundGap? TakeGap()
    {
        lock (_gate)
        {
            // Возвращение к людям — тоже возобновление работы, и перерыв кончается здесь: процесс
            // жив, экран в руках, а число, которое человеку сейчас покажут, расти после показа не
            // должно — иначе следующий кадр досчитает тот же перерыв вторым сообщением.
            WorkGoesOn(_time.GetUtcNow());

            var gap = _unsaid;
            _unsaid = null;
            return gap;
        }
    }

    public void Dispose()
    {
        _states.Dispose();
        _frames.Dispose();

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

            // Смена фазы перерыва не закрывает: погоня объявляет «Reconnecting» на каждой попытке,
            // и оттаявший процесс успевает выкрикнуть их несколько — приняв это за работу, мы бы
            // снова резали слитную заморозку на куски. Отметка начинается только там, где работа
            // началась: из отключённого состояния.
            if (_worked is { } worked)
            {
                _worked = worked with { Phase = state };
                return;
            }

            // Отметка в памяти — да, на диск — нет: сюда приходят потоком той стороны, что меняла
            // состояние (подключение ждут с экрана, значит это бывает и главный поток), а диску там
            // не место. Файл пишет один только таймер, и первая запись случится через период.
            var now = _time.GetUtcNow();
            _beatAt = _awakeSince = now;
            _worked = new BackgroundBeat(now, state);
        }
    }

    /// <summary>
    /// Тик сердцебиения: отмечается на диске и решает, засчитывать ли себе работу. Сам по себе тик
    /// её не доказывает — замороженный процесс оттаивает на минуту-другую и успевает тикнуть, — а
    /// вот <see cref="BackgroundBeat.Missed"/> ровного дыхания подряд доказывает: столько подряд
    /// пропускает только остановленный. Этим же и закрывается перерыв при выключенном колесе, когда
    /// кадров не будет вовсе: молчание живого процесса — забота сторожа данных, а не наша.
    /// </summary>
    private void Beat()
    {
        lock (_gate)
        {
            if (_worked is not { } worked) return;

            var now = _time.GetUtcNow();

            // Тик много позже срока — это и есть оттаявший процесс: между тиками не работал никто,
            // и счёт ровного дыхания начинается заново.
            if (now - _beatAt >= BackgroundBeat.Missed) _awakeSince = now;
            _beatAt = now;

            if (now - worked.At < BackgroundBeat.Missed)
            {
                // Работа идёт своим чередом, стоять нечему.
                _worked = new BackgroundBeat(now, _phase);
            }
            else if (now - _awakeSince >= BackgroundBeat.Missed)
            {
                // Перерыв кончился оттаиванием, а не этим тиком: минуты, за которые процесс доказал
                // свою жизнь, он работал — просто колесу было нечего сказать.
                WorkResumed(_awakeSince, now);
            }

            Write(_worked.Value);
        }
    }

    /// <summary>
    /// Кадр с колеса — бесспорное доказательство работы: заморозке кадры не достаются, их некому
    /// принять. Он и закрывает перерыв, каким бы длинным тот ни был.
    /// <para>
    /// Замок берётся на каждом кадре, десятки раз в секунду, и это по карману: под ним два
    /// присваивания. Раз в минуту кадр подождёт, пока тик допишет отметку, — миллисекунда, которой
    /// никто не заметит.
    /// </para>
    /// </summary>
    private void OnFrame()
    {
        lock (_gate) WorkGoesOn(_time.GetUtcNow());
    }

    private void StopBeating()
    {
        // Перерыв досчитывается и здесь: то, что фон стоял два часа, остаётся правдой, даже если к
        // моменту разбора райдер уже нажал «Отключить».
        WorkGoesOn(_time.GetUtcNow());

        _beats?.Dispose();
        _beats = null;
        _worked = null;
        Delete();
    }

    /// <summary>
    /// Работа доказана вот сейчас — обычный случай: пришёл кадр, вернулся экран, райдер отключился.
    /// </summary>
    private void WorkGoesOn(DateTimeOffset now) => WorkResumed(now, now);

    /// <summary>
    /// Перерыв, если он был, кончился в <paramref name="resumedAt"/> — его и заносим. Дальше счёт
    /// идёт от <paramref name="countFrom"/>, поэтому один и тот же пропуск не попадёт в журнал
    /// дважды.
    /// <para>
    /// Два мига, а не один, ради единственного случая: перерыв, закрытый ровным дыханием, кончился
    /// не на том тике, который это доказал, а на оттаивании — пятью минутами раньше. Считать их
    /// простоем значило бы соврать в другую сторону: пятиминутную заморозку такой счёт превратил бы
    /// в одиннадцатиминутную.
    /// </para>
    /// </summary>
    private void WorkResumed(DateTimeOffset resumedAt, DateTimeOffset countFrom)
    {
        if (_worked is not { } worked) return;

        _worked = new BackgroundBeat(countFrom, _phase);
        Notice(worked, resumedAt);
    }

    private void Notice(BackgroundBeat? beat, DateTimeOffset now)
    {
        if (beat is not { } last || BackgroundBeat.Gap(last, now) is not { } gap) return;

        // Свежий перерыв важнее давнего: человеку показывается последний, в журнале остаются оба.
        // Момент обнаружения едет вместе с длительностью (слово владельца 15.08.2026): показ может
        // случиться часами позже — когда экран наконец включат, — и без этой метки разбор гадает,
        // к какому месту журнала относится сообщение (так было с «пропущено 13 мин» 15.08).
        _unsaid = new BackgroundGap(gap, now);
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
