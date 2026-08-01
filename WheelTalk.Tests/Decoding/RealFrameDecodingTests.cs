using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Playback;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// The byte-in/value-out check this suite went without until 28.07.2026: frames as a Sherman L
/// actually sent them, straight off the wheel, decoded here.
/// <para>
/// Everything else that looks like a fixture in this repo is a decoded snapshot — it came out of
/// the decoder and therefore cannot catch the decoder misreading a frame. These two files came out
/// of the wheel. See <c>logs/field-2026-07-28/report.md</c> for how they were taken.
/// </para>
/// </summary>
public class RealFrameDecodingTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name);

    private static async Task<List<TelemetrySnapshot>> Decode(string fixture)
    {
        var harness = DecoderHarness.ForVeteran(config => config.GotwayNegative = "0");
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

    /// <summary>
    /// The wheel spun up on a stand until its duty cycle hit the ceiling and it cut out. Values are
    /// pinned at the saturation point because that is where the wheel itself stops being able to go
    /// faster — the one moment where the reported duty cycle has an unambiguous physical meaning.
    /// </summary>
    [Fact]
    public async Task A_spin_up_to_the_duty_ceiling_decodes_to_its_measured_values()
    {
        var snapshots = await Decode("shermanl_raw_spinup_20260728.csv");

        Assert.NotEmpty(snapshots);
        Assert.All(snapshots, s => Assert.Equal("Sherman L", s.Model));
        Assert.All(snapshots, s => Assert.Equal("006.0.10", s.Version));

        // The ceiling: 145.0 km/h at 144.8 V with the wheel reporting 100 % duty.
        Assert.Equal(145.0, snapshots.Max(s => s.SpeedKmh), 1);
        Assert.Equal(100.0, snapshots.Max(s => s.Pwm), 2);

        var saturated = snapshots.First(s => s.Pwm >= 100);
        Assert.InRange(saturated.VoltageV, 143.0, 145.5);

        // Regenerative braking pushed the pack above its resting voltage on the way down.
        Assert.InRange(snapshots.Max(s => s.VoltageV), 147.0, 149.0);
    }

    /// <summary>
    /// Two minutes of ordinary riding, recorded with the phone's screen off. Nothing here is
    /// pinned to a single frame — the point is that a long stretch of real traffic decodes to
    /// values that stay physically sane, which no synthetic dump can demonstrate.
    /// </summary>
    [Fact]
    public async Task Two_minutes_of_riding_decode_within_physical_bounds()
    {
        var snapshots = await Decode("shermanl_raw_ride_20260728.csv");

        Assert.True(snapshots.Count > 500, $"ожидались сотни отсчётов, получено {snapshots.Count}");

        // A 150 V pack under load and regen, never near a value that would mean a misread frame.
        Assert.All(snapshots, s => Assert.InRange(s.VoltageV, 130.0, 155.0));
        Assert.All(snapshots, s => Assert.InRange(s.SpeedKmh, 0.0, 60.0));
        Assert.All(snapshots, s => Assert.InRange(s.TemperatureC, 20, 60));
        Assert.All(snapshots, s => Assert.InRange(s.Pwm, -5.0, 60.0));

        // The odometer only ever counts up, and it moved: this really was a ride.
        var distances = snapshots.Select(s => s.TotalDistance).ToList();
        Assert.Equal(distances.OrderBy(d => d), distances);
        Assert.True(distances[^1] - distances[0] > 300, "за две минуты пробег должен заметно вырасти");
    }
}
