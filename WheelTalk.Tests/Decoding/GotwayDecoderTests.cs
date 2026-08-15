using Microsoft.Extensions.Time.Testing;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Diagnostics;
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

    /// <summary>
    /// Plan 35 §9 / port-deviations.md: bits 1-2 of the alarm byte (frame 0x04, offset 14) are
    /// named after the manufacturer's own app, not WheelLog's "Speed2"/"Speed1" — begode-
    /// comparison.md §2.2 (<c>HomeFragment.java:1312-1369</c>). Bit 1 = MOSFET fault.
    /// </summary>
    [Fact]
    public void Alarm_bit_1_reports_the_manufacturers_mosfet_fault_name()
    {
        var harness = DecoderHarness.ForGotway();
        harness.FeedHex("55AA0000000000000000000000000200000004005A5A5A5A"); // offset14 = 0x02

        Assert.Equal("errMosfet", harness.Snapshot().Alert);
    }

    /// <summary>Same as above — bit 2 = gyroscope fault.</summary>
    [Fact]
    public void Alarm_bit_2_reports_the_manufacturers_gyroscope_fault_name()
    {
        var harness = DecoderHarness.ForGotway();
        harness.FeedHex("55AA0000000000000000000000000400000004005A5A5A5A"); // offset14 = 0x04

        Assert.Equal("errGyroscope", harness.Snapshot().Alert);
    }

    /// <summary>
    /// Plan 35 §9 / begode-comparison.md §2.1: frame 0x00 offset 14-15 is a settings echo on
    /// stock ("GW") firmware, not PWM — the rider's speed-formula gauge (<c>Pwm</c>) must not
    /// move regardless of what's there. The raw bytes still land in internal state
    /// (<c>Output</c>/<c>OutputRaw</c>, unported-but-tracked field) — proving they're kept out
    /// of the display, not simply skipped.
    /// </summary>
    [Fact]
    public void Garbage_at_frame_type_0_offset_14_does_not_reach_the_riders_pwm_gauge()
    {
        var clean = DecoderHarness.ForGotway();
        clean.FeedHex("55AA1770003200000000000000000000000000005A5A5A5A"); // offset14-15 = 0x0000

        var garbage = DecoderHarness.ForGotway();
        garbage.FeedHex("55AA1770003200000000000000007FFF000000005A5A5A5A"); // offset14-15 = 0x7FFF

        var negative = DecoderHarness.ForGotway();
        negative.FeedHex("55AA177000320000000000000000FFFF000000005A5A5A5A"); // offset14-15 = 0xFFFF

        double expectedPwm = clean.Snapshot().Pwm;
        Assert.Equal(expectedPwm, garbage.Snapshot().Pwm, 6);
        Assert.Equal(expectedPwm, negative.Snapshot().Pwm, 6);

        // Sanity: the garbage did reach internal state — it's the *display* that's protected,
        // not the field itself (that field is out of this fix's scope, see port-deviations.md).
        Assert.NotEqual(clean.Snapshot().OutputRaw, garbage.Snapshot().OutputRaw);
    }

    /// <summary>
    /// Plan 35 §9 / port-deviations.md: frame types 0x05/0x06 (third/fourth battery pack, C/D)
    /// used to be entirely absent from the dispatch switch — same gap as upstream WheelLog. They
    /// now reach the dispatcher and get logged (<see cref="LogEvents.Decoding.ThirdFourthPackFrameId"/>)
    /// instead of vanishing silently, and — since <see cref="WheelState"/> only has two BMS
    /// slots, both already claimed by packs A/B (0x02/0x03) — decoding does not invent a mapping
    /// into either one: <c>Bms1</c>/<c>Bms2</c> stay exactly as pack A/B left them.
    /// </summary>
    [Theory]
    [InlineData(0x05, 'C')]
    [InlineData(0x06, 'D')]
    public void Pack_C_D_frames_reach_the_dispatcher_and_are_logged_not_lost(byte frameType, char pack)
    {
        var config = new AppWheelConfig();
        var time = new FakeTimeProvider();
        var state = new WheelState(config, time);
        var logger = new CapturingLogger<GotwayDecoder>();
        var decoder = new GotwayDecoder(state, config, time, logger);

        // Pack A (0x02) first, so we can prove it survives the pack C/D frame untouched.
        byte[] packA = Convert.FromHexString("55AA09C409C509C609C709C809C909CA09CB02005A5A5A5A");
        byte[] packCorD = Convert.FromHexString(frameType == 0x05
            ? "55AA09C409C509C609C709C809C909CA09CB05005A5A5A5A"
            : "55AA09C409C509C609C709C809C909CA09CB06005A5A5A5A");

        var exception = Record.Exception(() =>
        {
            decoder.Decode(packA);
            decoder.Decode(packCorD);
        });

        Assert.Null(exception);
        Assert.Equal(2.5, state.Bms1.Cells[0], 3); // pack A's first cell (0x09C4 = 2500 -> /1000)
        Assert.All(state.Bms2.Cells, cell => Assert.Equal(0.0, cell));
        Assert.Contains(logger.Entries, e =>
            e.EventId.Id == LogEvents.Decoding.ThirdFourthPackFrameId && e.Message.Contains(pack.ToString()));
    }

    /// <summary>
    /// Сетка банок на «Данных» строится по <c>CellCount</c>, а Gotway заполнял только портовый
    /// <c>CellNum</c> — сетка была вечно пустой (аудит экрана 15.08.2026). Теперь размер даёт тот же
    /// каскад счёта ячеек, что у Veteran.
    /// </summary>
    [Fact]
    public void A_bms_cells_frame_fills_cell_count_for_the_grid()
    {
        var config = new AppWheelConfig();
        var time = new FakeTimeProvider();
        var state = new WheelState(config, time);
        var decoder = new GotwayDecoder(state, config, time, new CapturingLogger<GotwayDecoder>());

        decoder.Decode(Convert.FromHexString("55AA09C409C509C609C709C809C909CA09CB02005A5A5A5A"));

        Assert.True(state.Bms1.CellCount > 0,
            $"CellCount = {state.Bms1.CellCount} — сетка банок снова пуста");
    }
}
