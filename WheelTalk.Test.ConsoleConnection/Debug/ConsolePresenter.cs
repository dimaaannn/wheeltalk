using WheelTalk.Core.Contracts;
using WheelTalk.Core.Services;

namespace WheelTalk.Debug;

/// <summary>
/// Keeps one live telemetry line on screen, rewritten in place with '\r' instead of scrolling —
/// while riding, the numbers are a dashboard, not a history (the history goes to the Serilog file
/// sink and to <see cref="TelemetryCsvWriter"/>). A log line landing on the console does break the
/// line visually, but the next update repaints it a line lower, so it self-heals.
///
/// With output redirected (piped to a file) '\r' is useless, so each update is printed as its own
/// line at the old, slower rate.
/// </summary>
public sealed class ConsolePresenter : IDisposable
{
    private static readonly TimeSpan InPlaceThrottle = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RedirectedThrottle = TimeSpan.FromSeconds(1);

    private readonly bool _redirected = Console.IsOutputRedirected;
    private readonly TimeSpan _throttle;
    private readonly IDisposable _subscription;
    private DateTimeOffset _lastPrint = DateTimeOffset.MinValue;
    private int _lastLineLength;

    public ConsolePresenter(WheelService wheelService)
    {
        _throttle = _redirected ? RedirectedThrottle : InPlaceThrottle;
        _subscription = wheelService.Telemetry.Subscribe(OnSnapshotUpdated);
    }

    private void OnSnapshotUpdated(TelemetrySnapshot s)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastPrint < _throttle) return;
        _lastPrint = now;

        // Fields are ordered by how much you want them mid-ride: a narrow window truncates the
        // tail (BMS cells, model) rather than the speed.
        string line =
            $"speed {s.SpeedKmh,5:F1} km/h | pwm {s.Pwm,5:F1}% | {s.VoltageV,6:F2} V | {s.CurrentA,6:F2} A | " +
            $"bat {s.Battery,3}% | {s.TemperatureC,3} C | trip {s.WheelDistanceKm,6:F2} km | " +
            $"total {s.TotalDistanceKm,8:F2} km | ang {s.Angle,5:F1} | ph {s.PhaseCurrentA,6:F2} A | " +
            $"bms1 {s.Bms1.MinCell:F3}/{s.Bms1.MaxCell:F3} d{s.Bms1.CellDiff:F3} | " +
            $"bms2 {s.Bms2.MinCell:F3}/{s.Bms2.MaxCell:F3} d{s.Bms2.CellDiff:F3} | {s.Model} v{s.Version}";

        if (_redirected)
        {
            Console.WriteLine(line);
            return;
        }

        // A line that wraps can't be rewritten by '\r' — the wrapped remainder stays on screen.
        int width = Math.Max(20, Console.WindowWidth - 1);
        if (line.Length > width) line = line[..width];

        Console.Write($"\r{line.PadRight(_lastLineLength)}");
        _lastLineLength = line.Length;
    }

    public void Dispose()
    {
        _subscription.Dispose();
        if (_lastLineLength > 0)
        {
            Console.WriteLine(); // leave the last reading on screen, cursor on a fresh line
        }
    }
}
