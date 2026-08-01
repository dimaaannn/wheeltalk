using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Playback;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Byte-in/value-out check against real InMotion BLE traffic — the original's own
/// <c>RAW_inmotion_V5F.csv</c>/<c>RAW_inmotion_V8S.csv</c>/<c>RAW_inmotion_alerts.csv</c>
/// (<c>Wheellog.Android/app/src/test/resources/</c>), the only InMotion recordings that exist
/// anywhere near this port — no InMotion wheel has been in the owner's hands. Same role as
/// <see cref="RealFrameDecodingTests"/> plays for Sherman L: these bytes came off a real wheel, so
/// a decoder that silently misreads a field would show up here even though every synthetic
/// InmotionAdapterTest.kt fixture passes.
/// </summary>
public class RealInMotionFrameDecodingTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name);

    private static async Task<List<TelemetrySnapshot>> Decode(string fixture)
    {
        var harness = DecoderHarness.ForInMotion();
        var snapshots = new List<TelemetrySnapshot>();

        var transport = new ReplayTransport(
            () => new StreamReader(Fixture(fixture)), TimeProvider.System, NullLogger<ReplayTransport>.Instance);
        transport.DataReceived += frame =>
        {
            harness.Decoder.Feed(frame);
            var snapshot = harness.Snapshot();
            if (snapshot.VoltageRaw != 0) snapshots.Add(snapshot);
        };
        await transport.PlayAsync(realtime: false);

        return snapshots;
    }

    [Fact]
    public async Task V5F_recording_decodes_within_physical_bounds()
    {
        var snapshots = await Decode("RAW_inmotion_V5F.csv");

        Assert.NotEmpty(snapshots);
        Assert.All(snapshots, s => Assert.InRange(s.VoltageV, 60.0, 90.0));
        Assert.All(snapshots, s => Assert.InRange(s.SpeedKmh, 0.0, 40.0));
        Assert.All(snapshots, s => Assert.InRange(s.Battery, 0, 100));
        Assert.All(snapshots, s => Assert.InRange(s.TemperatureC, 0, 60));

        var distances = snapshots.Select(s => s.TotalDistance).ToList();
        Assert.Equal(distances.OrderBy(d => d), distances);

        // Unlike RAW_inmotion_V8S.csv, this recording never carries a slow-info (0x0F550114)
        // response — only fast-info telemetry — so the model never becomes known within it. Real
        // recorded data, not a decoder gap: nothing here exercises the handshake.
    }

    [Fact]
    public async Task V8S_recording_decodes_within_physical_bounds()
    {
        var snapshots = await Decode("RAW_inmotion_V8S.csv");

        Assert.NotEmpty(snapshots);
        Assert.All(snapshots, s => Assert.InRange(s.VoltageV, 60.0, 90.0));
        Assert.All(snapshots, s => Assert.InRange(s.SpeedKmh, 0.0, 45.0));
        Assert.All(snapshots, s => Assert.InRange(s.Battery, 0, 100));
        Assert.All(snapshots, s => Assert.InRange(s.TemperatureC, 0, 60));

        var distances = snapshots.Select(s => s.TotalDistance).ToList();
        Assert.Equal(distances.OrderBy(d => d), distances);

        Assert.Equal("Inmotion V8S", snapshots[^1].Model);
    }

    /// <summary>Recorded specifically to carry Alert (0x0F780101) frames — confirms the alert path
    /// actually fires on real traffic, not just the synthetic single-frame fixture in
    /// InMotionDecoderTests.</summary>
    [Fact]
    public async Task Alerts_recording_reaches_the_alert_path()
    {
        var harness = DecoderHarness.ForInMotion();
        var alerts = new List<string>();

        var transport = new ReplayTransport(
            () => new StreamReader(Fixture("RAW_inmotion_alerts.csv")), TimeProvider.System, NullLogger<ReplayTransport>.Instance);
        transport.DataReceived += frame =>
        {
            harness.Decoder.Feed(frame);
            string alert = harness.Snapshot().Alert;
            if (alert.Length > 0) alerts.Add(alert);
        };
        await transport.PlayAsync(realtime: false);

        Assert.NotEmpty(alerts);
        Assert.All(alerts, a => Assert.Contains('[', a));
    }
}
