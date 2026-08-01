using WheelTalk.Core.Contracts;

namespace WheelTalk.Tests.TestSupport;

/// <summary>
/// Mirrors Android WheelData.getSpeed() = Math.round(mSpeed / 10.0) — the original
/// *AdapterTest.kt fixtures assert against that int getter, not the raw fixed-point field
/// (TelemetrySnapshot.SpeedRaw, which mirrors mSpeed itself).
/// </summary>
public static class SnapshotAssertions
{
    public static int RoundedSpeed(this TelemetrySnapshot snapshot) =>
        (int)Math.Round(snapshot.SpeedRaw / 10.0, MidpointRounding.AwayFromZero);
}
