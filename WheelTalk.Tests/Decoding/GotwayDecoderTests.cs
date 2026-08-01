using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Ports the Gotway/Begode fixtures from the original Android GotwayAdapterTest.kt that were
/// used to cross-check GotwayDecoder 1:1 (see AGENTS.md, "Как проверять изменения в декодере") —
/// pinned here as permanent regression tests instead of the one-off scratch project used then.
/// </summary>
public class GotwayDecoderTests
{
    [Fact]
    public void Decodes_2020_board_data()
    {
        var harness = DecoderHarness.ForGotway();
        harness.FeedHex(
            "55AA19C1000000000000008CF0000001FFF80018",
            "5A5A5A5A55AA000060D248001C20006400010007",
            "000804185A5A5A5A");

        var snapshot = harness.Snapshot();
        Assert.Equal(0, Math.Abs(snapshot.RoundedSpeed()));
        Assert.Equal(24, snapshot.TemperatureC);
        Assert.Equal(65.93, snapshot.VoltageV, 2);
        Assert.Equal(1.4, snapshot.PhaseCurrentA, 2);
        Assert.Equal(0.0, snapshot.WheelDistanceKm, 3);
        Assert.Equal(24786, snapshot.TotalDistance);
        Assert.Equal(100, snapshot.Battery);
    }

    [Fact]
    public void Decodes_new_board_data()
    {
        var harness = DecoderHarness.ForGotway();
        string[] frames =
        [
            "55aa17750538007602eefb64f494148100090018",
            "5a5a5a5a55aa0032000004b10000000013880000",
            "000001005a5a5a5a55aa00000000000000000000",
            "00000000000003005a5a5a5a55aa003c278c4900",
            "1c2000c800000000001204185a5a5a5a55aa022c",
            "000000000000000000000000000007185a5a5a5a",
        ];
        // Fed twice, matching the Kotlin fixture — the frame-B counter (distance/alarm) only
        // resolves to newDataFound=true on the second pass through.
        harness.FeedHex(frames);
        harness.FeedHex(frames);

        var snapshot = harness.Snapshot();
        Assert.Equal(481, Math.Abs(snapshot.RoundedSpeed()));
        Assert.Equal(27, snapshot.TemperatureC);
        Assert.Equal(120.10, snapshot.VoltageV, 2);
        Assert.Equal(-11.8, snapshot.PhaseCurrentA, 2);
        Assert.Equal(-5.56, snapshot.CurrentA, 2);
        Assert.Equal(0.75, snapshot.WheelDistanceKm, 3);
        Assert.Equal(3942284, snapshot.TotalDistance);
        Assert.Equal(55, snapshot.Battery);
    }

    [Fact]
    public void Decodes_new_board_data_2()
    {
        var harness = DecoderHarness.ForGotway();
        string[] frames =
        [
            "55aa177007390076001103b6f387148100090018",
            "5a5a5a5a55aa0032000004b00000000013880000",
            "000001025a5a5a5a55aa00000000000000000000",
            "00000000000003025a5a5a5a55aa003c24af4900",
            "1c2000c800000100001204185a5a5a5a55aafd7e",
            "000000000000000000000000000007185a5a5a5a",
        ];
        harness.FeedHex(frames);
        harness.FeedHex(frames);

        var snapshot = harness.Snapshot();
        Assert.Equal(666, Math.Abs(snapshot.RoundedSpeed()));
        Assert.Equal(27, snapshot.TemperatureC);
        Assert.Equal(120.00, snapshot.VoltageV, 2);
        Assert.Equal(9.5, snapshot.PhaseCurrentA, 2);
        Assert.Equal(6.42, snapshot.CurrentA, 2);
        Assert.Equal(0.017, snapshot.WheelDistanceKm, 3);
        Assert.Equal(3941551, snapshot.TotalDistance);
        Assert.Equal(54, snapshot.Battery);
    }

    [Fact]
    public void Decodes_strange_board_data()
    {
        var harness = DecoderHarness.ForGotway();
        harness.FeedHex(
            "55AA19A0000C00000000032AF8150001FFF80018",
            "5A5A5A5A",
            "55AA000026E324001C19001E0001000700080418",
            "5A5A5A5A");

        var snapshot = harness.Snapshot();
        Assert.Equal(4, Math.Abs(snapshot.RoundedSpeed()));
        Assert.Equal(30, snapshot.TemperatureC);
        Assert.Equal(65.6, snapshot.VoltageV, 2);
        Assert.Equal(8.1, snapshot.PhaseCurrentA, 2);
        Assert.Equal(0.0, snapshot.WheelDistanceKm, 3);
        Assert.Equal(9955, snapshot.TotalDistance);
        Assert.Equal(97, snapshot.Battery);
    }

    [Fact]
    public void Handshake_parses_name_and_firmware_version()
    {
        var harness = DecoderHarness.ForGotway();
        harness.FeedHex(
            "475732303032303031", // "GW2002001"
            "4e414d453a45584e0d0a", // "NAME:EXN\r\n"
            "204d505536353030b3f5cabcbbafb3c9b9a620"); // " MPU6500..."

        var snapshot = harness.Snapshot();
        Assert.Equal("EXN", snapshot.Model);
        Assert.Equal("2002001", snapshot.Version);
    }
}
