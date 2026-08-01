using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Ports the InMotion V1 fixtures from the original Android InmotionAdapterTest.kt (see AGENTS.md,
/// "Как проверять изменения в декодере") — pinned here as permanent regression tests. Each fixture
/// feeds a full frame sequence (fast-info handshake bytes, then a slow-info frame carrying model/
/// serial/version, then a fast-info live-data frame) exactly as recorded by the original test.
/// </summary>
public class InMotionDecoderTests
{
    [Fact]
    public void Decode_with_v5f_full_data()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;

        bool result1 = decoder.Decode(Convert.FromHexString("AAAA1401A5550F7C000000B4720020FE0001001B"));
        bool result2 = decoder.Decode(Convert.FromHexString("0076BA5C28711200000000000000000100000000"));
        bool result3 = decoder.Decode(Convert.FromHexString("000000FA010301FA0103010402020100000000C2"));
        bool result4 = decoder.Decode(Convert.FromHexString("040001C2040001900302010000000000000000A8"));
        bool result5 = decoder.Decode(Convert.FromHexString("6100000010000000000000000000000000000000"));
        bool result6 = decoder.Decode(Convert.FromHexString("0000000100000000000000000000000000000000"));
        bool result7 = decoder.Decode(Convert.FromHexString("0000000200000500000000000000000000000004"));
        bool result8 = decoder.Decode(Convert.FromHexString("020301E35555"));

        bool result11 = decoder.Decode(Convert.FromHexString("AAAA1301A5550F60000000B4720020FE000100FF"));
        bool result12 = decoder.Decode(Convert.FromHexString("3F00003A18DEFF5D01000029F0FFFF29F0FFFFEC"));
        bool result13 = decoder.Decode(Convert.FromHexString("FFFFFF15200000000000001A1A00000000000000"));
        bool result14 = decoder.Decode(Convert.FromHexString("0000001CE3130000000000000026061A03D20721"));
        bool result15 = decoder.Decode(Convert.FromHexString("0000006F0100006F010000F7010000420C00002B"));
        bool result16 = decoder.Decode(Convert.FromHexString("110000070000000000000000000000265555"));

        Assert.False(result1);
        Assert.False(result2);
        Assert.False(result3);
        Assert.False(result4);
        Assert.False(result5);
        Assert.False(result6);
        Assert.False(result7);
        Assert.False(result8);

        var afterHandshake = harness.Snapshot();
        Assert.Equal("1271285CBA76001B", afterHandshake.Serial);
        Assert.Equal("Inmotion V5F", afterHandshake.Model);
        Assert.Equal("1.3.506", afterHandshake.Version);

        Assert.False(result11);
        Assert.False(result12);
        Assert.False(result13);
        Assert.False(result14);
        Assert.False(result15);
        Assert.True(result16);

        var snapshot = harness.Snapshot();
        Assert.Equal(3.82, snapshot.SpeedKmh, 2);
        Assert.Equal(26, snapshot.TemperatureC);
        Assert.Equal(0, snapshot.ImuTemp);
        Assert.Equal(82.13, snapshot.VoltageV, 2);
        Assert.Equal(-0.2, snapshot.CurrentA, 2);
        Assert.Equal(0.0, snapshot.WheelDistanceKm, 3);
        Assert.Equal(1303324, snapshot.TotalDistance);
        Assert.Equal(97, snapshot.Battery);
        Assert.Equal(0.2499847412109375, snapshot.Angle, 10);
        Assert.Equal(5.588888888888889, snapshot.Roll, 10);
    }

    [Fact]
    public void Decode_with_v8f_full_data()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;

        FeedV8FHandshake(decoder);

        bool result11 = decoder.Decode(Convert.FromHexString("AAAA1301A5550F9500000000000000FE0201008F"));
        bool result12 = decoder.Decode(Convert.FromHexString("020000000000000000000054FAFFFF54FAFFFFFB"));
        bool result13 = decoder.Decode(Convert.FromHexString("FFFFFFBE200000000000001B1B24240000000000"));
        bool result14 = decoder.Decode(Convert.FromHexString("000000AF5400000100000000302B140605E00722"));
        bool result15 = decoder.Decode(Convert.FromHexString("00000023000000C50000005D020000D900000006"));
        bool result16 = decoder.Decode(Convert.FromHexString("000000000000000000000000000000004000081B"));
        bool result17 = decoder.Decode(Convert.FromHexString("0000F221000033060000000000000B0000006216"));
        bool result18 = decoder.Decode(Convert.FromHexString("0000F42A0000030000000E000000110106000000"));
        bool result19 = decoder.Decode(Convert.FromHexString("000000000000C500765555"));

        var afterHandshake = harness.Snapshot();
        Assert.Equal("14604A5EBD9B000E", afterHandshake.Serial);
        Assert.Equal("Inmotion V8F", afterHandshake.Model);
        Assert.Equal("2.2.21", afterHandshake.Version);

        Assert.False(result11);
        Assert.False(result12);
        Assert.False(result13);
        Assert.False(result14);
        Assert.False(result15);
        Assert.False(result16);
        Assert.False(result17);
        Assert.False(result18);
        Assert.True(result19);

        var snapshot = harness.Snapshot();
        Assert.Equal(1.37, snapshot.SpeedKmh, 2);
        Assert.Equal(27, snapshot.TemperatureC);
        Assert.Equal(36, snapshot.ImuTemp);
        Assert.Equal(83.82, snapshot.VoltageV, 2);
        Assert.Equal(-0.05, snapshot.CurrentA, 2);
        Assert.Equal(0.001, snapshot.WheelDistanceKm, 3);
        Assert.Equal(21679, snapshot.TotalDistance);
        Assert.Equal(100, snapshot.Battery);
        Assert.Equal(0.0099945068359375, snapshot.Angle, 10);
        Assert.Equal(0.0, snapshot.Roll, 10);
        Assert.Equal("Drive", snapshot.ModeStr);
    }

    [Fact]
    public void Decode_with_v8f_full_data_2()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;

        FeedV8FHandshake(decoder);

        bool result11 = decoder.Decode(Convert.FromHexString("AAAA1301A5550F9500000000000000FE0201007A"));
        bool result12 = decoder.Decode(Convert.FromHexString("14000000000000000000003CFDFFFF3CFDFFFFF6"));
        bool result13 = decoder.Decode(Convert.FromHexString("FFFFFFA7200000400100001C1C2424F8FFFFFFE7"));
        bool result14 = decoder.Decode(Convert.FromHexString("FFFFFFB75400000900000000042C140605E00722"));
        bool result15 = decoder.Decode(Convert.FromHexString("000000E301000023010000AC0500000302000056"));
        bool result16 = decoder.Decode(Convert.FromHexString("0000004C0000000000000000000000004000081C"));
        bool result17 = decoder.Decode(Convert.FromHexString("0000F221000033060000BF020000070100006F16"));
        bool result18 = decoder.Decode(Convert.FromHexString("0000032B0000100000001D000000380256004C00"));
        bool result19 = decoder.Decode(Convert.FromHexString("F8FFE7FFE7FF2301465555"));

        Assert.False(result11);
        Assert.False(result12);
        Assert.False(result13);
        Assert.False(result14);
        Assert.False(result15);
        Assert.False(result16);
        Assert.False(result17);
        Assert.False(result18);
        Assert.True(result19);

        var snapshot = harness.Snapshot();
        Assert.Equal("14604A5EBD9B000E", snapshot.Serial);
        Assert.Equal("Inmotion V8F", snapshot.Model);
        Assert.Equal("2.2.21", snapshot.Version);
        Assert.Equal(0.66, snapshot.SpeedKmh, 2);
        Assert.Equal(28, snapshot.TemperatureC);
        Assert.Equal(36, snapshot.ImuTemp);
        Assert.Equal(83.59, snapshot.VoltageV, 2);
        Assert.Equal(-0.1, snapshot.CurrentA, 2);
        Assert.Equal(0.009, snapshot.WheelDistanceKm, 3);
        Assert.Equal(21687, snapshot.TotalDistance);
        Assert.Equal(100, snapshot.Battery);
        Assert.Equal(0.079986572265625, snapshot.Angle, 10);
        Assert.Equal(0.0, snapshot.Roll, 10);
        Assert.Equal("Drive", snapshot.ModeStr);
    }

    [Fact]
    public void Decode_with_v8s_full_data()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;

        bool result1 = decoder.Decode(Convert.FromHexString("aaaa1401a5550f8500000000000000fe02010006"));
        bool result2 = decoder.Decode(Convert.FromHexString("0146bd5ea5aa7115000000000000000000000000"));
        bool result3 = decoder.Decode(Convert.FromHexString("0000000015000266000000000700036600000000"));
        bool result4 = decoder.Decode(Convert.FromHexString("260301010000000000000a000000000000000800"));
        bool result5 = decoder.Decode(Convert.FromHexString("b888000043100000001000000000000000000000"));
        bool result6 = decoder.Decode(Convert.FromHexString("0000000001000000000000000000000000000000"));
        bool result7 = decoder.Decode(Convert.FromHexString("000000000700000800000000b005004f00000065"));
        bool result8 = decoder.Decode(Convert.FromHexString("00000000801027000001000a01a05555"));

        bool result11 = decoder.Decode(Convert.FromHexString("aaaa1301a5550f9500000000000000fe02010015"));
        bool result12 = decoder.Decode(Convert.FromHexString("eeffff0000000000000000000000000000000007"));
        bool result13 = decoder.Decode(Convert.FromHexString("00000006200000000000001e1e92920000000004"));
        bool result14 = decoder.Decode(Convert.FromHexString("000000af04000000000000000d370c1203d00723"));
        bool result15 = decoder.Decode(Convert.FromHexString("0000000000000000000000bcfeffff1400000000"));
        bool result16 = decoder.Decode(Convert.FromHexString("0000001100000000000000000000000040000892"));
        bool result17 = decoder.Decode(Convert.FromHexString("00007f0500006600000083b205004f0000006502"));
        bool result18 = decoder.Decode(Convert.FromHexString("0000ca45000000000000d51f0000600000001100"));
        bool result19 = decoder.Decode(Convert.FromHexString("0000040004000000bf5555"));

        Assert.False(result1);
        Assert.False(result2);
        Assert.False(result3);
        Assert.False(result4);
        Assert.False(result5);
        Assert.False(result6);
        Assert.False(result7);
        Assert.False(result8);

        var afterHandshake = harness.Snapshot();
        Assert.Equal("1571AA5EBD460106", afterHandshake.Serial);
        Assert.Equal("Inmotion V8S", afterHandshake.Model);
        Assert.Equal("102.2.21", afterHandshake.Version);

        Assert.False(result11);
        Assert.False(result12);
        Assert.False(result13);
        Assert.False(result14);
        Assert.False(result15);
        Assert.False(result16);
        Assert.False(result17);
        Assert.False(result18);
        Assert.True(result19);

        var snapshot = harness.Snapshot();
        Assert.Equal(0.0, snapshot.SpeedKmh, 2);
        Assert.Equal(30, snapshot.TemperatureC);
        Assert.Equal(-110, snapshot.ImuTemp);
        Assert.Equal(81.98, snapshot.VoltageV, 2);
        Assert.Equal(0.07, snapshot.CurrentA, 2);
        Assert.Equal(0.0, snapshot.WheelDistanceKm, 3);
        Assert.Equal(1199, snapshot.TotalDistance);
        Assert.Equal(96, snapshot.Battery);
        Assert.Equal(-0.0699920654296875, snapshot.Angle, 10);
        Assert.Equal(0.0, snapshot.Roll, 10);
        Assert.Equal("Drive", snapshot.ModeStr);
    }

    /// <summary>The wheel escapes its own checksum byte when the value happens to collide with
    /// AA/55/A5 — the unpacker's escape handling must strip that transparently, same as any other
    /// byte in the frame (see InMotionUnpacker's class doc).</summary>
    [Fact]
    public void Decode_data_with_escaped_checksum()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;

        string[] frames =
        [
            "aaaa1401a5550f8500000000000000fe02010001",
            "00da7c5e1a611400000000000000000000000000",
            "0000001500020200000000070003020000000026",
            "0301010000000000000a000000000000000200d0",
            "840000ea0f000000100000000000000000000000",
            "0000000100000000000000000000000000000000",
            "00000006000008000000005b0a006f6e01003a00",
            "0000006c3421000001010a00a5555555",
        ];

        bool result = false;
        foreach (string frame in frames) result = decoder.Decode(Convert.FromHexString(frame));

        Assert.False(result);
        var snapshot = harness.Snapshot();
        Assert.Equal("Inmotion V8F", snapshot.Model);
        Assert.Equal("2.2.21", snapshot.Version);
    }

    [Fact]
    public void Command_builders_match_original_bytes()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;

        Assert.Equal(Convert.FromHexString("aaaa1901a5550f3254769800000000080500001f5555"), decoder.BuildCalibrate());
        Assert.Null(decoder.BuildResetTrip());
        Assert.Null(decoder.BuildUpdatePedalsMode(1));

        Assert.Equal(Convert.FromHexString("aaaa0d01a5550f010000000000000008050000805555"), decoder.BuildSetLightState(true));
        Assert.Equal(Convert.FromHexString("aaaa0d01a5550f0000000000000000080500007f5555"), decoder.BuildSetLightState(false));

        // switchFlashlight() toggles from whatever the config says LightEnabled currently is
        // (false, the harness default) — so the first toggle turns it on.
        Assert.Equal(Convert.FromHexString("aaaa0d01a5550f010000000000000008050000805555"), decoder.BuildSwitchFlashlight());

        // Model still Unknown: wheelBeep() falls back to playSound(4) — old wheels like V8/V5F
        // don't have the dedicated beep command (InMotionAdapter.java:414-417). No original test
        // exercises playSound(4) directly (only playSound(2), via "play sound command"); this is
        // that same byte layout with data[0]=4 instead of 2, checksum shifted by the same +2.
        Assert.Equal(Convert.FromHexString("aaaa0906a5550f040000000000000008050000845555"), decoder.BuildWheelBeep());
    }

    /// <summary>V8F belongs to getWheelModesWheel()'s newer-wheel set, so once the model is known,
    /// wheelBeep() uses the dedicated beep command instead of playSound(4).</summary>
    [Fact]
    public void Wheel_beep_uses_dedicated_command_once_model_is_known()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;
        FeedV8FHandshake(decoder);

        Assert.Equal(Convert.FromHexString("aaaa1601a5550fb200000011000000080500004b5555"), decoder.BuildWheelBeep());
    }

    private static void FeedV8FHandshake(Core.Decoding.IWheelDecoder decoder)
    {
        string[] frames =
        [
            "AAAA1401A5550F8500000000000000FE0201000E",
            "009BBD5E4A601400000000000000000000000000",
            "0000001500020200000000070003020000000026",
            "0301010000000000000A000000000073000000C8",
            "AF00002510000000100000000000000000000000",
            "0000000100000000000000000000000000000000",
            "0000000600000800000000000000000000000000",
            "000000801027000001010A00DC5555",
        ];
        foreach (string frame in frames) decoder.Decode(Convert.FromHexString(frame));
    }
}
