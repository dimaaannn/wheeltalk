using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Playback;
using WheelTalk.Core.Logging;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Logging;

/// <summary>
/// The point of the raw dump: what the phone writes during a ride has to come back into the
/// decoder byte for byte. Nothing else in the suite proves that — the recorded fixtures are
/// decoded snapshots, so they cannot catch a decoder that misreads a frame, having been produced
/// by that same decoder.
/// </summary>
public class RawFrameRoundTripTests
{
    /// <summary>Sherman L, the same frames VeteranDecoderTests pins the decoder against.</summary>
    private static readonly string[] Frames =
    [
        "dc5a5c53397afffe0aa400000df10000000a0b3d",
        "0e0e0000037a035217730064000e00b480c80000",
        "808080808080058080808080800ff30ff50ff50f",
        "f50ff50fef0ff20ff30ff30ff30ff30fed0ff30f",
        "f40ff5378c5145",
    ];

    private static readonly DateTimeOffset Start =
        new(2026, 7, 27, 22, 5, 3, 0, TimeSpan.FromHours(3));

    [Fact]
    public async Task A_dumped_ride_decodes_the_same_as_the_live_frames()
    {
        var live = DecoderHarness.ForVeteran();
        live.FeedHex(Frames);

        string dump = Path.Combine(Path.GetTempPath(), $"RAW_roundtrip_{Guid.NewGuid():N}.csv");
        await File.WriteAllLinesAsync(dump, Frames.Select(
            (frame, i) => RawFrameLog.FormatLine(Start.AddMilliseconds(i * 43), Convert.FromHexString(frame))));

        var replayed = DecoderHarness.ForVeteran();
        var seen = new List<string>();
        try
        {
            var transport = new ReplayTransport(
                () => new StreamReader(dump), TimeProvider.System, NullLogger<ReplayTransport>.Instance);
            transport.DataReceived += frame =>
            {
                seen.Add(Convert.ToHexStringLower(frame));
                replayed.Decoder.Feed(frame);
            };
            await transport.PlayAsync(realtime: false);
        }
        finally
        {
            File.Delete(dump);
        }

        Assert.Equal(Frames, seen);

        // A rendered ride-log row is the widest single comparison available: speed, voltage,
        // currents, power, PWM, battery, both distances and both temperatures in one string.
        var moment = DateTimeOffset.UnixEpoch;
        Assert.Equal(
            RideLog.FormatLine(moment, live.Snapshot()),
            RideLog.FormatLine(moment, replayed.Snapshot()));
        Assert.Equal(live.Snapshot().Version, replayed.Snapshot().Version);
        Assert.Equal(live.Snapshot().Model, replayed.Snapshot().Model);
    }
}
