using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Services;
using WheelTalk.Droid.Configuration;
using WheelTalk.Storage;

namespace WheelTalk.Droid.Logging;

/// <summary>
/// Кто и когда пишет поток телеметрии, и где в нём начинается покатушка. Живёт рядом с сессией, а
/// не на странице: запись обязана продолжаться с погашенным экраном, а это ровно тот случай, когда
/// ни одной страницы нет в живых.
/// <para>
/// Everything about how a ride is stored belongs to <see cref="RideStore"/> — this class only
/// decides what to hand over. Which is most of what it ever did: the file it used to open, the
/// alert it used to drain and the wheel change it used to watch for are all things the store has to
/// know about anyway, and having them in two places is how they drift.
/// </para>
/// <para>
/// <b>Поток и разметка разошлись</b> (план 23 §5.7). Подписка на телеметрию живёт всегда — писать
/// ли из неё, решает <see cref="LoggingOptions.TelemetryRecording"/> на каждом отсчёте, а не
/// подписка с отписками: настройка живая, и переключённая посреди поездки она обязана подействовать
/// сразу. Проверка стоит пять сравнений в секунду — дешевле, чем вторая правда о том, пишем ли мы.
/// </para>
/// <para>
/// A dropped link does not end the recording — the session reconnects and rows resume in the same
/// ride, otherwise one ride would come out as a heap of stubs. A different wheel does end it.
/// </para>
/// </summary>
public sealed partial class RideRecorder : IDisposable
{
    private readonly WheelSession _session;
    private readonly RideStore _store;
    private readonly LoggingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RideRecorder> _logger;

    private readonly IDisposable _subscription;

    /// <summary>
    /// Идёт ли разметка — то, что человек видит кнопкой «Запись». Volatile, потому что снять её
    /// может и поток записи: разрыв дольше порога закрывает поездку и отжимает кнопку
    /// (<see cref="RideStore.MarkingBrokenOff"/>).
    /// </summary>
    private volatile bool _marking;

    public RideRecorder(
        WheelSession session,
        RideStore store,
        IOptions<LoggingOptions> options,
        TimeProvider timeProvider,
        ILogger<RideRecorder> logger)
    {
        _session = session;
        _store = store;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _subscription = _session.Telemetry.Subscribe(Write);
        _store.MarkingBrokenOff += OnMarkingBrokenOff;
    }

    /// <summary>
    /// Размечается ли поездка прямо сейчас. Смысл кнопки от настройки зависит, а этого признака —
    /// нет: «Всегда» пишет поток и без неё, и кнопка там означает «отсюда покатушка».
    /// </summary>
    public bool IsRecording => _marking;

    /// <summary>Можно ли вообще размечать: при «Никогда» размечать нечего — потока нет.</summary>
    private bool CanRecord => _options.TelemetryRecording != TelemetryRecording.Never;

    /// <summary>The ride being written, or 0 until the first snapshot has opened one.</summary>
    public long RideId => _store.CurrentRideId;

    /// <summary>Rows committed so far — what the recording screen counts.</summary>
    public int RowsWritten => _store.RowsWritten;

    /// <summary>Raised when recording starts or stops, so a screen can catch up.</summary>
    public event Action? Changed;

    public void Toggle()
    {
        if (IsRecording) Stop();
        else Start();
    }

    public void Start()
    {
        if (_marking || !CanRecord) return;

        _marking = true;
        _store.BeginRide();
        LogStarted();
        Changed?.Invoke();
    }

    /// <summary>
    /// Closing is a write like any other and happens on the store's own thread; the button that
    /// calls this is on the UI thread and has nothing to wait for.
    /// </summary>
    public void Stop() => _ = StopAsync();

    /// <summary>
    /// То же, но с ожиданием «легло на диск». Нужно ровно одному месту — выходу из приложения:
    /// поездка заканчивается явно, кнопкой либо выходом (план 23 §5.4), и уйти раньше, чем конец
    /// записан, значит оставить покатушку выглядеть так же, как смерть телефона.
    /// </summary>
    public async Task StopAsync()
    {
        if (!_marking) return;

        _marking = false;
        LogStopped();
        Changed?.Invoke();

        await _store.CloseRideAsync();
        Changed?.Invoke();
    }

    /// <summary>
    /// Подписка снимается совсем: приложение уходит. Поездку закрывает сам
    /// <see cref="RideStore"/> при остановке — иначе штатный выход выглядел бы как смерть телефона.
    /// </summary>
    public void Dispose()
    {
        _marking = false;
        _store.MarkingBrokenOff -= OnMarkingBrokenOff;
        _subscription.Dispose();
    }

    /// <summary>
    /// Поездку закрыл разрыв, а не кнопка (план 23 §5.4, решение владельца 04.08.2026). Кнопка
    /// обязана показать правду: запись прекратилась — значит она не нажата. Возобновлять здесь
    /// нечего: явное нажатие было разовым намерением, а постоянное живёт флагом автозаписи, и она
    /// нажмёт сама, когда колесо снова поедет быстрее порога (<c>CrashGuard</c>).
    /// <para>
    /// Приходит с потока записи; наружу это уходит тем же <see cref="Changed"/>, что и кнопка, —
    /// экраны на нём уже сидят.
    /// </para>
    /// </summary>
    private void OnMarkingBrokenOff()
    {
        if (!_marking) return;

        _marking = false;
        LogBrokenOff();
        Changed?.Invoke();
    }

    private void Write(TelemetrySnapshot snapshot)
    {
        if (!ShouldWrite()) return;

        string mac = _session.Address ?? "";
        if (mac.Length == 0) return;

        // Local time, as in the original — a ride is read back in the timezone it was ridden in,
        // and the store keeps the offset so the export can print it that way.
        // Протокол к этому моменту уже опознан: строка пишется на приход кадра, а кадр — это то,
        // чем он и опознаётся. Пустая строка осталась бы только у записи без единого кадра.
        _store.Write(mac, _session.Protocol?.ToString() ?? "", snapshot, _timeProvider.GetLocalNow());
    }

    /// <summary>Три положения переключателя, план 23 §5.7. Спрашивается на каждом отсчёте: настройка живая.</summary>
    private bool ShouldWrite() => _options.TelemetryRecording switch
    {
        TelemetryRecording.Always => true,
        TelemetryRecording.RideOnly => _marking,
        _ => false,
    };

    [LoggerMessage(EventId = 1500, EventName = "Ride.RecordingStarted", Level = LogLevel.Information,
        Message = "Ride.RecordingStarted")]
    private partial void LogStarted();

    [LoggerMessage(EventId = 1501, EventName = "Ride.RecordingStopped", Level = LogLevel.Information,
        Message = "Ride.RecordingStopped")]
    private partial void LogStopped();

    [LoggerMessage(EventId = 1502, EventName = "Ride.RecordingBrokenOff", Level = LogLevel.Warning,
        Message = "Ride.RecordingBrokenOff — silence longer than the gap; the button is released")]
    private partial void LogBrokenOff();
}
