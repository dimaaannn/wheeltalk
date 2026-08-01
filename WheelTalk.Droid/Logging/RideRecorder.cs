using Microsoft.Extensions.Logging;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Services;
using WheelTalk.Storage;

namespace WheelTalk.Droid.Logging;

/// <summary>
/// The ride history, started and stopped by the rider. It lives next to the session rather than on
/// a page: recording has to keep going with the screen off, which is exactly when no page is alive.
/// <para>
/// Everything about how a ride is stored now belongs to <see cref="RideStore"/> — this class only
/// decides when to subscribe and hands snapshots over. Which is most of what it ever did: the file
/// it used to open, the alert it used to drain and the wheel change it used to watch for are all
/// things the store has to know about anyway, and having them in two places is how they drift.
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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RideRecorder> _logger;

    private IDisposable? _subscription;

    public RideRecorder(WheelSession session, RideStore store, TimeProvider timeProvider, ILogger<RideRecorder> logger)
    {
        _session = session;
        _store = store;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool IsRecording => _subscription is not null;

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
        if (IsRecording) return;

        _subscription = _session.Telemetry.Subscribe(Write);
        LogStarted();
        Changed?.Invoke();
    }

    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;

        // Closing is a write like any other and happens on the store's own thread; the button that
        // called this is on the UI thread and has nothing to wait for.
        _ = _store.CloseRideAsync().ContinueWith(
            _ => Changed?.Invoke(), TaskScheduler.Default);

        LogStopped();
        Changed?.Invoke();
    }

    public void Dispose() => Stop();

    private void Write(TelemetrySnapshot snapshot)
    {
        string mac = _session.Address ?? "";
        if (mac.Length == 0) return;

        // Local time, as in the original — a ride is read back in the timezone it was ridden in,
        // and the store keeps the offset so the export can print it that way.
        // Протокол к этому моменту уже опознан: строка пишется на приход кадра, а кадр — это то,
        // чем он и опознаётся. Пустая строка осталась бы только у записи без единого кадра.
        _store.Write(mac, _session.Protocol?.ToString() ?? "", snapshot, _timeProvider.GetLocalNow());
    }

    [LoggerMessage(EventId = 1500, EventName = "Ride.RecordingStarted", Level = LogLevel.Information,
        Message = "Ride.RecordingStarted")]
    private partial void LogStarted();

    [LoggerMessage(EventId = 1501, EventName = "Ride.RecordingStopped", Level = LogLevel.Information,
        Message = "Ride.RecordingStopped")]
    private partial void LogStopped();
}
