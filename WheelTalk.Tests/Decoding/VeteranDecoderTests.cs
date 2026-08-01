using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Ports the Veteran fixtures from the original Android VeteranAdapterTest.kt that were used to
/// cross-check VeteranDecoder 1:1 (see AGENTS.md, "Как проверять изменения в декодере") — pinned
/// here as permanent regression tests instead of the one-off scratch project used then.
/// </summary>
public class VeteranDecoderTests
{
    [Fact]
    public void Decodes_abrams()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(
            "dc5a5c20266d00004aaf00004aaf000000000d9e",
            "0b8800000af00af007d2000300050004");

        var snapshot = harness.Snapshot();
        Assert.Equal(0, Math.Abs(snapshot.RoundedSpeed()));
        Assert.Equal(34, snapshot.TemperatureC);
        Assert.Equal(98.37, snapshot.VoltageV, 2);
        Assert.Equal(0.0, snapshot.PhaseCurrentA, 2);
        Assert.Equal(19.119, snapshot.WheelDistanceKm, 3);
        Assert.Equal(19119, snapshot.TotalDistance);
        Assert.Equal(98, snapshot.Battery);
        Assert.Equal(0.05, snapshot.Angle, 2);
        Assert.Equal("002.0.02", snapshot.Version);
    }

    [Fact]
    public void Decodes_patton_crc()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(
            "dc5a5c452abe00003edc00008562003500000b5c",
            "0dfe000002bc07d00fac000219fb0000006f0000",
            "80808080808004000014ffffffffff32ee029109",
            "df0fd303cb000000006f9a79c2");

        var snapshot = harness.Snapshot();
        Assert.Equal(0, Math.Abs(snapshot.RoundedSpeed()));
        Assert.Equal(29, snapshot.TemperatureC);
        Assert.Equal(109.42, snapshot.VoltageV, 2);
        Assert.Equal(0.0, snapshot.PhaseCurrentA, 2);
        Assert.Equal(16.092, snapshot.WheelDistanceKm, 3);
        Assert.Equal(3507554, snapshot.TotalDistance);
        Assert.Equal(42, snapshot.Battery);
        Assert.Equal(66.51, snapshot.Angle, 2);
        Assert.Equal("004.0.12", snapshot.Version);
    }

    [Fact]
    public void Decodes_lynx_crc()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(
            "dc5a5c53391b000006d000000770000000260bcc",
            "0e08000000fa00c8138c00b4000b014c80c80000",
            "808080808080010008808080800fee0fee0fee0f",
            "ee0fef0fe80fef0fef0ff00ff00ff00fea0fef0f",
            "ef0fefdab22518");

        var snapshot = harness.Snapshot();
        Assert.Equal(0, Math.Abs(snapshot.RoundedSpeed()));
        Assert.Equal(30, snapshot.TemperatureC);
        Assert.Equal(146.19, snapshot.VoltageV, 2);
        Assert.Equal(3.8, snapshot.PhaseCurrentA, 2);
        Assert.Equal(1.744, snapshot.WheelDistanceKm, 3);
        Assert.Equal(1904, snapshot.TotalDistance);
        Assert.Equal(94, snapshot.Battery);
        Assert.Equal(0.11, snapshot.Angle, 2);
        Assert.Equal("005.0.04", snapshot.Version);
    }

    [Fact]
    public void Decodes_sherman_l()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(
            "dc5a5c53397afffe0aa400000df10000000a0b3d",
            "0e0e0000037a035217730064000e00b480c80000",
            "808080808080058080808080800ff30ff50ff50f",
            "f50ff50fef0ff20ff30ff30ff30ff30fed0ff30f",
            "f40ff5378c5145");

        var snapshot = harness.Snapshot();
        Assert.Equal(2, Math.Abs(snapshot.RoundedSpeed()));
        Assert.Equal(28, snapshot.TemperatureC);
        Assert.Equal(147.14, snapshot.VoltageV, 2);
        Assert.Equal(1.0, snapshot.PhaseCurrentA, 2);
        Assert.Equal(2.724, snapshot.WheelDistanceKm, 3);
        Assert.Equal(3569, snapshot.TotalDistance);
        Assert.Equal(97, snapshot.Battery);
        Assert.Equal(0.14, snapshot.Angle, 2);
        Assert.Equal("006.0.03", snapshot.Version);
    }

    /// <summary>
    /// Sherman L (protocol version 6) reports its duty cycle in the packet, and that value must be
    /// used as is. Deriving PWM from speed and voltage instead would report -0.25 % here — wrong
    /// sign, wrong magnitude, and PWM is what the alarms are built on.
    /// </summary>
    [Fact]
    public void Sherman_l_reports_hardware_pwm()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(
            "dc5a5c53397afffe0aa400000df10000000a0b3d",
            "0e0e0000037a035217730064000e00b480c80000",
            "808080808080058080808080800ff30ff50ff50f",
            "f50ff50fef0ff20ff30ff30ff30ff30fed0ff30f",
            "f40ff5378c5145");

        var snapshot = harness.Snapshot();
        Assert.True(harness.Config.HwPwm, "protocol version 6 must switch the decoder to the reported duty cycle");
        Assert.Equal(1.8, snapshot.Pwm, 2);      // packet byte 34 = 0x00B4 = 180 -> 1.80 %
        Assert.Equal(1.8, snapshot.MaxPwm, 2);
        Assert.Equal(0.02, snapshot.CurrentA, 2); // derived from the reported PWM, not the guessed one
    }

    /// <summary>
    /// The switch is driven by the protocol version, not by the model: Abrams reports 002.0.02,
    /// which is already past the threshold, so its duty cycle comes from the packet as well.
    /// (No fixture exists for pre-2 firmware, so the derived-PWM branch is left to the Gotway
    /// tests, which exercise it against real frames.)
    /// </summary>
    [Fact]
    public void Protocol_version_2_also_reports_hardware_pwm()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(
            "dc5a5c20266d00004aaf00004aaf000000000d9e",
            "0b8800000af00af007d2000300050004");

        var snapshot = harness.Snapshot();
        Assert.True(harness.Config.HwPwm);
        Assert.Equal(0.04, snapshot.Pwm, 2);     // packet byte 34 = 0x0004 = 4 -> 0.04 %
    }

    [Fact]
    public void Decodes_oryx_packet_8()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(
            "dc5a5c473e7b000030100002a309000f00000a86",
            "0473000007d007d01f4300a0e43a000080c80000",
            "808080808080080000803ce8c8c8c81e00000000",
            "0001320554a8648037808064e0ca5a");

        var snapshot = harness.Snapshot();
        Assert.Equal(0, Math.Abs(snapshot.RoundedSpeed()));
        Assert.Equal(26, snapshot.TemperatureC);
        Assert.Equal(159.95, snapshot.VoltageV, 2);
        Assert.Equal(0.0, snapshot.PhaseCurrentA, 2);
        Assert.Equal(143.376, snapshot.WheelDistanceKm, 3);
        Assert.Equal(1024777, snapshot.TotalDistance);
        Assert.Equal(62, snapshot.Battery);
        Assert.Equal(-71.1, snapshot.Angle, 2);
        Assert.Equal("008.0.03", snapshot.Version);
    }
}
