using WheelTalk.Core.Ports;
using WheelTalk.Droid.Resources.Strings;

namespace WheelTalk.Droid.Wheel;

/// <summary>
/// Counts how often telemetry arrives, in two separate rates: decoded snapshots (what the screens
/// show) and raw BLE frames (what the wheel actually sends — several per snapshot). Both are
/// wanted on every screen during field tests, which is why this lives apart from the pages.
/// </summary>
public sealed class TelemetryRate : IDisposable
{
    private readonly ITransport _transport;
    private readonly TimeProvider _timeProvider;
    private readonly long _startedAt;

    private int _snapshots;
    private int _rawFrames;

    public TelemetryRate(ITransport transport, TimeProvider timeProvider)
    {
        _transport = transport;
        _timeProvider = timeProvider;
        _startedAt = timeProvider.GetTimestamp();
        transport.DataReceived += CountRawFrame;
    }

    public int Snapshots => _snapshots;

    public void CountSnapshot() => _snapshots++;

    /// <summary>Rates since counting started, e.g. "данные 5,0 Гц (118) · кадры 23,5 Гц (553)".</summary>
    public string Describe()
    {
        double seconds = _timeProvider.GetElapsedTime(_startedAt).TotalSeconds;
        return string.Format(AppStrings.RateFormat, _snapshots / seconds, _snapshots, _rawFrames / seconds, _rawFrames);
    }

    public void Dispose() => _transport.DataReceived -= CountRawFrame;

    // Raised from the BLE callback thread, hence the interlocked increment.
    private void CountRawFrame(byte[] frame) => Interlocked.Increment(ref _rawFrames);
}
