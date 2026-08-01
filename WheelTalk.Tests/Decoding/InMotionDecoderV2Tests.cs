using WheelTalk.Core.Decoding;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Ports the InMotion V2 fixtures from the original Android InmotionAdapterV2Test.kt (see
/// AGENTS.md, "Как проверять изменения в декодере") — pinned here as permanent regression tests.
/// No RAW BLE logs exist for this protocol anywhere (unlike V1) — these fixtures are the only
/// ground truth this port has, and the plan's owner has no InMotion V2 wheel to record one with.
/// </summary>
public class InMotionDecoderV2Tests
{
    [Fact]
    public void Decode_with_v11_full_data()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;

        bool result1 = decoder.Decode(Convert.FromHexString("AAAA110882010206010201009C")); // wheel type
        bool result2 = decoder.Decode(Convert.FromHexString("AAAA11178202313438304341313232323037303032420000000000FD")); // s/n
        bool result3 = decoder.Decode(Convert.FromHexString("AAAA111D820622080004030F000602214000010110000602230D00010107000001F3")); // versions
        bool result4 = decoder.Decode(Convert.FromHexString("AAAA141AA0207C15C800106464140000000058020000006400001500100010")); // settings
        bool result5 = decoder.Decode(Convert.FromHexString("AAAA142B900001142614000000803E498AE00FB209D109CEB000C7DF010000BE720000AB1300008F040000AB0600004C")); // statistics
        bool result6 = decoder.Decode(Convert.FromHexString("AAAA141991E86C000066191C002DB2040064E60000974D050000C7DF01A4")); // totals
        bool result7 = decoder.Decode(Convert.FromHexString("AAAA143184E61EEB0561094A11AE04A004DF01402958CBB000CE004A010000D4FF7C15641900000000492B00000000000000000000C6"));

        Assert.False(result1);
        Assert.False(result2);
        Assert.False(result3);
        Assert.False(result4);
        Assert.False(result5);
        Assert.False(result6);
        Assert.True(result7);

        var snapshot = harness.Snapshot();
        Assert.Equal("1480CA122207002B", snapshot.Serial);
        Assert.Equal("Inmotion V11", snapshot.Model);
        Assert.Equal("Main:1.1.64 Drv:3.4.8 BLE:1.1.13", snapshot.Version);

        Assert.Equal(24.01, snapshot.SpeedKmh, 2);
        Assert.Equal(27, snapshot.TemperatureC);
        Assert.Equal(30, snapshot.Temperature2C);
        Assert.Equal(-176, snapshot.ImuTemp);
        Assert.Equal(-176, snapshot.CpuTemp);
        Assert.Equal(1184.0, snapshot.MotorPower, 2);
        Assert.Equal(65.00, snapshot.CurrentLimit, 2);
        Assert.Equal(55.00, snapshot.SpeedLimit, 2);
        Assert.Equal(44.26, snapshot.Torque, 2);
        Assert.Equal(79.10, snapshot.VoltageV, 2);
        Assert.Equal(15.15, snapshot.CurrentA, 2);
        Assert.Equal(4.79, snapshot.WheelDistanceKm, 3);
        Assert.Equal(278800, snapshot.TotalDistance);
        Assert.Equal(88, snapshot.Battery);
        Assert.Equal(1198.0, snapshot.PowerW, 2);
        Assert.Equal(3.3, snapshot.Angle, 2);
        Assert.Equal(-0.44, snapshot.Roll, 2);
    }

    [Fact]
    public void Decode_with_v11_escape_data()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;
        ((InMotionDecoderV2)decoder).SetModel(InMotionV2Model.V11);

        bool result = decoder.Decode(Convert.FromHexString(
            "aaaa1431843020a5a50068025207870080009400882c5fc4b000d7001000f4ff2b037c1564190000d9d9492b00000000000000000000a5a5"));

        Assert.True(result);
        var snapshot = harness.Snapshot();
        Assert.Equal(6.16, snapshot.SpeedKmh, 2);
        Assert.Equal(20, snapshot.TemperatureC);
        Assert.Equal(39, snapshot.Temperature2C);
        Assert.Equal(41, snapshot.ImuTemp);
        Assert.Equal(41, snapshot.CpuTemp);
        Assert.Equal(128.0, snapshot.MotorPower, 2);
        Assert.Equal(65.00, snapshot.CurrentLimit, 2);
        Assert.Equal(55.00, snapshot.SpeedLimit, 2);
        Assert.Equal(18.74, snapshot.Torque, 2);
        Assert.Equal(82.40, snapshot.VoltageV, 2);
        Assert.Equal(1.65, snapshot.CurrentA, 2);
        Assert.Equal(1.48, snapshot.WheelDistanceKm, 2);
        Assert.Equal(95, snapshot.Battery);
        Assert.Equal(135.0, snapshot.PowerW, 2);
        Assert.Equal(0.16, snapshot.Angle, 2);
        Assert.Equal(8.11, snapshot.Roll, 2);
    }

    [Fact]
    public void Decode_v11_new_fw_with_pwm()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;
        ((InMotionDecoderV2)decoder).SetModel(InMotionV2Model.V11);
        ((InMotionDecoderV2)decoder).SetProto(1);

        bool result = decoder.Decode(Convert.FromHexString(
            "aaaa143384411f8e03a5a506e90bd80242021600122a5acbb000cc002a0000000bfd7c1564190000d4d1ff09490a0000000000000000000010"));

        Assert.True(result);
        var snapshot = harness.Snapshot();
        Assert.Equal(17.01, snapshot.SpeedKmh, 2);
        Assert.Equal(27, snapshot.TemperatureC);
        Assert.Equal(28, snapshot.Temperature2C);
        Assert.Equal(33, snapshot.ImuTemp);
        Assert.Equal(36, snapshot.CpuTemp);
        Assert.Equal(578.0, snapshot.MotorPower, 2);
        Assert.Equal(65.00, snapshot.CurrentLimit, 2);
        Assert.Equal(55.00, snapshot.SpeedLimit, 2);
        Assert.Equal(30.49, snapshot.Torque, 2);
        Assert.Equal(80.01, snapshot.VoltageV, 2);
        Assert.Equal(9.1, snapshot.CurrentA, 2);
        Assert.Equal(0.22, snapshot.WheelDistanceKm, 2);
        Assert.Equal(90, snapshot.Battery);
        Assert.Equal(728.0, snapshot.PowerW, 2);
        Assert.Equal(0.42, snapshot.Angle, 2);
        Assert.Equal(-7.57, snapshot.Roll, 2);
    }

    [Fact]
    public void Decode_with_v11_escape_data2()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;
        ((InMotionDecoderV2)decoder).SetModel(InMotionV2Model.V11);
        ((InMotionDecoderV2)decoder).SetProto(1);

        bool result = decoder.Decode(Convert.FromHexString(
            "aaaa143184a5aa1e8100640b1301650059001504a0234cc0b000ce00180000007c007c1564190000d1d3492b00000000000000000000a5a5"));

        Assert.True(result);
        var snapshot = harness.Snapshot();
        Assert.Equal(29.16, snapshot.SpeedKmh, 2);
        Assert.Equal(16, snapshot.TemperatureC);
        Assert.Equal(30, snapshot.Temperature2C);
        Assert.Equal(35, snapshot.ImuTemp);
        Assert.Equal(33, snapshot.CpuTemp);
        Assert.Equal(89.0, snapshot.MotorPower, 2);
        Assert.Equal(65.00, snapshot.CurrentLimit, 2);
        Assert.Equal(55.00, snapshot.SpeedLimit, 2);
        Assert.Equal(2.75, snapshot.Torque, 2);
        Assert.Equal(78.50, snapshot.VoltageV, 2);
        Assert.Equal(1.29, snapshot.CurrentA, 2);
        Assert.Equal(10.45, snapshot.WheelDistanceKm, 2);
        Assert.Equal(76, snapshot.Battery);
        Assert.Equal(101.0, snapshot.PowerW, 2);
        Assert.Equal(0.24, snapshot.Angle, 2);
        Assert.Equal(1.24, snapshot.Roll, 2);
    }

    [Fact]
    public void Decode_with_v11_v1_4_0()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;
        ((InMotionDecoderV2)decoder).SetModel(InMotionV2Model.V11);
        ((InMotionDecoderV2)decoder).SetProto(2);

        bool result = decoder.Decode(Convert.FromHexString(
            "aaaa1445842d1d10000000efff070000000000000000002b0300000000000000008a149612e02e8813641900000000cbb000cccad1000028000000000049140000000000000000000021"));

        Assert.True(result);
        var snapshot = harness.Snapshot();
        Assert.Equal(0, snapshot.SpeedKmh, 2);
        Assert.Equal(27, snapshot.TemperatureC);
        Assert.Equal(28, snapshot.Temperature2C);
        Assert.Equal(33, snapshot.ImuTemp);
        Assert.Equal(26, snapshot.CpuTemp);
        Assert.Equal(0, snapshot.MotorPower, 2);
        Assert.Equal(65.00, snapshot.CurrentLimit, 2);
        Assert.Equal(50.00, snapshot.SpeedLimit, 2);
        Assert.Equal(-0.17, snapshot.Torque, 2);
        Assert.Equal(74.69, snapshot.VoltageV, 2);
        Assert.Equal(0.16, snapshot.CurrentA, 2);
        Assert.Equal(0, snapshot.WheelDistanceKm, 2);
        Assert.Equal(53, snapshot.Battery);
        Assert.Equal(0, snapshot.PowerW, 2);
        Assert.Equal(0, snapshot.Angle, 2);
        Assert.Equal(0, snapshot.Roll, 2);
    }

    [Fact]
    public void Decode_version_with_v11_v1_4_0()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;
        ((InMotionDecoderV2)decoder).SetModel(InMotionV2Model.V11);

        bool result = decoder.Decode(Convert.FromHexString("aaaa111d820622000003040300070221000004011a000602230d00010107000001b9"));

        Assert.False(result);
        Assert.Equal("Main:1.4.0 Drv:4.3.0 BLE:1.1.13", harness.Snapshot().Version);
    }

    [Fact]
    public void Decode_version_with_v12()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;
        ((InMotionDecoderV2)decoder).SetModel(InMotionV2Model.V11);

        bool result = decoder.Decode(Convert.FromHexString("aaaa111d820622790002042000060221040005017d000602233700010203000402bb"));

        Assert.False(result);
        Assert.Equal("Main:1.5.4 Drv:4.2.121 BLE:2.1.55", harness.Snapshot().Version);
    }

    [Fact]
    public void Decode_with_v12_full_data()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;

        bool result1 = decoder.Decode(Convert.FromHexString("aaaa110882010207010103009c"));
        bool result2 = decoder.Decode(Convert.FromHexString("aaaa11178202413033313135353133303030393733300000000000fb"));
        bool result3 = decoder.Decode(Convert.FromHexString("aaaa111d820622700002042000060221180004017d000602232400010203000402bc"));
        bool result5 = decoder.Decode(Convert.FromHexString("aaaa142b900001082608000000c1b55622330000000000cdceb0ce0000000000000000000000000000000008000000ce"));
        bool result6 = decoder.Decode(Convert.FromHexString("aaaa1419916350000074471800d1140400c68e00007d350200b0ce000039"));
        bool result7 = decoder.Decode(Convert.FromHexString("aaaa144384cd26090000000e00040000000000000000000000eafb000062009d2450463b1b581b000000000000cdce00ced1d0b03d2828000000004900000000000000000000008c"));

        Assert.False(result1);
        Assert.False(result2);
        Assert.False(result3);
        Assert.False(result5);
        Assert.False(result6);
        Assert.True(result7);

        var snapshot = harness.Snapshot();
        Assert.Equal("A031155130009730", snapshot.Serial);
        Assert.Equal("Inmotion V12 HS", snapshot.Model);
        Assert.Equal("Main:1.4.24 Drv:4.2.112 BLE:2.1.36", snapshot.Version);

        Assert.Equal(0.0, snapshot.SpeedKmh, 2);
        Assert.Equal(29, snapshot.TemperatureC);
        Assert.Equal(30, snapshot.Temperature2C);
        Assert.Equal(32, snapshot.ImuTemp);
        Assert.Equal(33, snapshot.CpuTemp);
        Assert.Equal(0, snapshot.MotorPower, 2);
        Assert.Equal(70.00, snapshot.CurrentLimit, 2);
        Assert.Equal(69.71, snapshot.SpeedLimit, 2);
        Assert.Equal(0.14, snapshot.Torque, 2);
        Assert.Equal(99.33, snapshot.VoltageV, 2);
        Assert.Equal(0.09, snapshot.CurrentA, 2);
        Assert.Equal(0.0, snapshot.WheelDistanceKm, 2);
        Assert.Equal(205790, snapshot.TotalDistance);
        Assert.Equal(1, snapshot.Battery); // old FW issue, matches original
        Assert.Equal(0.0, snapshot.PowerW, 2);
        Assert.Equal(0.0, snapshot.Angle, 2);
        Assert.Equal(-10.46, snapshot.Roll, 2);
    }

    [Fact]
    public void Decode_with_v12_full_data_2()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;

        decoder.Decode(Convert.FromHexString("aaaa110882010207010103009c"));
        decoder.Decode(Convert.FromHexString("aaaa11178202413033313135353133303030393733300000000000fb"));
        decoder.Decode(Convert.FromHexString("aaaa111d820622700002042000060221180004017d000602232400010203000402bc"));
        decoder.Decode(Convert.FromHexString("aaaa142b900001082608000000c1b55622330000000000cdceb0ce0000000000000000000000000000000008000000ce"));
        decoder.Decode(Convert.FromHexString("aaaa1419916350000074471800d1140400c68e00007d350200b0ce000039"));
        bool result7 = decoder.Decode(Convert.FromHexString(
            "aaaa144384ae24600479135909c61536085a0b00003f000000eb003700a5aa21b61f50463b1b581b000000000000ddd900dfe5e4b0f9646400000000490800000000000000000000dd"));

        Assert.True(result7);
        var snapshot = harness.Snapshot();
        Assert.Equal("Inmotion V12 HS", snapshot.Model);
        Assert.Equal(49.85, snapshot.SpeedKmh, 2);
        Assert.Equal(45, snapshot.TemperatureC);
        Assert.Equal(41, snapshot.Temperature2C);
        Assert.Equal(52, snapshot.ImuTemp);
        Assert.Equal(53, snapshot.CpuTemp);
        Assert.Equal(2906.0, snapshot.MotorPower, 2);
        Assert.Equal(70.00, snapshot.CurrentLimit, 2);
        Assert.Equal(69.71, snapshot.SpeedLimit, 2);
        Assert.Equal(23.93, snapshot.Torque, 2);
        Assert.Equal(93.90, snapshot.VoltageV, 2);
        Assert.Equal(11.20, snapshot.CurrentA, 2);
        Assert.Equal(0.55, snapshot.WheelDistanceKm, 2);
        Assert.Equal(205790, snapshot.TotalDistance);
        Assert.Equal(86, snapshot.Battery);
        Assert.Equal(2102.0, snapshot.PowerW, 2);
        Assert.Equal(0.63, snapshot.Angle, 2);
        Assert.Equal(2.35, snapshot.Roll, 2);
    }

    [Fact]
    public void Decode_with_v12_data_3()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;
        ((InMotionDecoderV2)decoder).SetModel(InMotionV2Model.V12HS);

        bool result = decoder.Decode(Convert.FromHexString(
            "aaaa14438415273500930496014b0535003a0000008d000000fdfe010010271c255046581b581b000000000000ceca00cfd1d0b08d646400000000490000000000000000000000bc"));

        Assert.True(result);
        var snapshot = harness.Snapshot();
        Assert.Equal(11.71, snapshot.SpeedKmh, 2);
        Assert.Equal(30, snapshot.TemperatureC);
        Assert.Equal(26, snapshot.Temperature2C);
        Assert.Equal(32, snapshot.ImuTemp);
        Assert.Equal(33, snapshot.CpuTemp);
        Assert.Equal(58.0, snapshot.MotorPower, 2);
        Assert.Equal(70.00, snapshot.CurrentLimit, 2);
        Assert.Equal(70.00, snapshot.SpeedLimit, 2);
        Assert.Equal(4.06, snapshot.Torque, 2);
        Assert.Equal(100.05, snapshot.VoltageV, 2);
        Assert.Equal(0.53, snapshot.CurrentA, 2);
        Assert.Equal(0.01, snapshot.WheelDistanceKm, 2);
        Assert.Equal(100, snapshot.Battery);
        Assert.Equal(53.0, snapshot.PowerW, 2);
        Assert.Equal(1.41, snapshot.Angle, 2);
        Assert.Equal(-2.59, snapshot.Roll, 2);
    }

    [Fact]
    public void Decode_with_v12_data_4()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;
        ((InMotionDecoderV2)decoder).SetModel(InMotionV2Model.V12HS);

        bool result = decoder.Decode(Convert.FromHexString(
            "aaaa1443842627090000000000060000000000000000000000b3fd000010271c255046581b581b000000000000ceca00ced0cfb048282800000000490000000000000000000000ef"));

        Assert.True(result);
        var snapshot = harness.Snapshot();
        Assert.Equal(0.0, snapshot.SpeedKmh, 2);
        Assert.Equal(30, snapshot.TemperatureC);
        Assert.Equal(26, snapshot.Temperature2C);
        Assert.Equal(31, snapshot.ImuTemp);
        Assert.Equal(32, snapshot.CpuTemp);
        Assert.Equal(0.0, snapshot.MotorPower, 2);
        Assert.Equal(70.0, snapshot.CurrentLimit, 2);
        Assert.Equal(70.0, snapshot.SpeedLimit, 2);
        Assert.Equal(0.0, snapshot.Torque, 2);
        Assert.Equal(100.22, snapshot.VoltageV, 2);
        Assert.Equal(0.09, snapshot.CurrentA, 2);
        Assert.Equal(0.0, snapshot.WheelDistanceKm, 2);
        Assert.Equal(100, snapshot.Battery);
        Assert.Equal(0.0, snapshot.PowerW, 2);
        Assert.Equal(0.0, snapshot.Angle, 2);
        Assert.Equal(-5.89, snapshot.Roll, 2);
    }

    [Fact]
    public void Decode_with_v12_pro_full_data()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;

        decoder.Decode(Convert.FromHexString("aaaa110882010207030101009c"));
        decoder.Decode(Convert.FromHexString("aaaa11178202413033313138333135303031333832340000000000f7"));
        decoder.Decode(Convert.FromHexString("aaaa111d820622120005060300080221100007016b00080223420001020000050281"));
        decoder.Decode(Convert.FromHexString("aaaa142ca0200000000006030008e0151815111600004038114b6464010028646428b80b45450000000000000db0000067"));
        decoder.Decode(Convert.FromHexString("aaaa142b90000140264f000000c5708649380000000000c8c8b0c8000000000000000000000000000000001a000000a7"));
        decoder.Decode(Convert.FromHexString("aaaa141991e12603006567dd00f6f117001cf40400954f1300b0c80000ca"));
        bool result8 = decoder.Decode(Convert.FromHexString(
            "aaaa144384b7261100000085ff5c00000000000000fcff0000eafe000076266d26803ee015581b000000000000c8c800c9b0c7b0b700000000000049000000000000000000000081"));

        Assert.True(result8);
        var snapshot = harness.Snapshot();
        Assert.Equal("A031183150013824", snapshot.Serial);
        Assert.Equal("Inmotion V12 PRO", snapshot.Model);
        Assert.Equal("Main:1.7.16 Drv:6.5.18 BLE:2.1.66", snapshot.Version);

        Assert.Equal(0.0, snapshot.SpeedKmh, 2);
        Assert.Equal(24, snapshot.TemperatureC);
        Assert.Equal(24, snapshot.Temperature2C);
        Assert.Equal(23, snapshot.ImuTemp);
        Assert.Equal(0, snapshot.CpuTemp);
        Assert.Equal(0.0, snapshot.MotorPower, 2);
        Assert.Equal(70.00, snapshot.CurrentLimit, 2);
        Assert.Equal(56.00, snapshot.SpeedLimit, 2);
        Assert.Equal(-1.23, snapshot.Torque, 2);
        Assert.Equal(99.11, snapshot.VoltageV, 2);
        Assert.Equal(0.17, snapshot.CurrentA, 2);
        Assert.Equal(0.0, snapshot.WheelDistanceKm, 2);
        Assert.Equal(2065610, snapshot.TotalDistance);
        Assert.Equal(98, snapshot.Battery);
        Assert.Equal(0.0, snapshot.PowerW, 2);
        Assert.Equal(-0.04, snapshot.Angle, 2);
        Assert.Equal(-2.78, snapshot.Roll, 2);
    }

    [Fact]
    public void Decode_with_v13_full_data_1()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;

        decoder.Decode(Convert.FromHexString("aaaa1108820102080101010091"));
        decoder.Decode(Convert.FromHexString("aaaa111782024130333131364231383030303130343600000000008a"));
        decoder.Decode(Convert.FromHexString("aaaa112f8206223a000005030008022115000002cf000802230a0002020000050224070001010200010125070001010200010172"));
        decoder.Decode(Convert.FromHexString("aaaa142b9000010126010000004390a7d5010251000701cdcec9d000000000080000000000000004000000070000006c"));
        decoder.Decode(Convert.FromHexString("aaaa1419915e010000b7660000500900008c0600002d8b0000c9d000007e"));
        bool result = decoder.Decode(Convert.FromHexString(
            "aaaa145984092f3807000036003735000025130f27b108111d4203b00664fee703050000000000f225e225204e28233421401f401f204e401f709400000000cdccc9d1b0d10000b0286400000000004910000000000000001800000000b3"));

        Assert.True(result);
        var snapshot = harness.Snapshot();
        Assert.Equal("A03116B180001046", snapshot.Serial);
        Assert.Equal("Inmotion V13", snapshot.Model);
        Assert.Equal("Main:2.0.21 Drv:5.0.58 BLE:2.2.10", snapshot.Version);

        Assert.Equal(136.23, snapshot.SpeedKmh, 2);
        Assert.Equal(29, snapshot.TemperatureC);
        Assert.Equal(28, snapshot.Temperature2C);
        Assert.Equal(33, snapshot.ImuTemp);
        Assert.Equal(0, snapshot.CpuTemp);
        Assert.Equal(1712.0, snapshot.MotorPower, 2);
        Assert.Equal(80.00, snapshot.CurrentLimit, 2);
        Assert.Equal(90.00, snapshot.SpeedLimit, 2);
        Assert.Equal(74.41, snapshot.Torque, 2);
        Assert.Equal(120.41, snapshot.VoltageV, 2);
        Assert.Equal(18.48, snapshot.CurrentA, 2);
        Assert.Equal(4.901, snapshot.WheelDistanceKm, 3);
        Assert.Equal(3500, snapshot.TotalDistance);
        Assert.Equal(97, snapshot.Battery);
        Assert.Equal(2225.0, snapshot.PowerW, 2);
        Assert.Equal(0.54, snapshot.Angle, 2);
        Assert.Equal(-4.12, snapshot.Roll, 2);
    }

    [Fact]
    public void Decode_with_v14_full_data_1()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;

        decoder.Decode(Convert.FromHexString("aaaa1108820102090201010093"));
        decoder.Decode(Convert.FromHexString("aaaa1117820241303332313743304230303131323245000000000084"));
        decoder.Decode(Convert.FromHexString("aaaa11418206223c00060503000802212800000301000902230100000208000201240200000501000204260200000501000204250200000501000204270200000501000204eb"));
        decoder.Decode(Convert.FromHexString("aaaa142b9000011d261d00000044c5895e2c08ac049205d0d1cbd0510000001e0f0000fc010000070100003401000051"));
        decoder.Decode(Convert.FromHexString("aaaa1419911d9c000059293800d01106007134010097110600cbd051001c"));
        bool result = decoder.Decode(Convert.FromHexString(
            "aaaa1459847c334000000000002c0800009900430866004f002700efff6400bfff5e0000000000a5aa26a4261027581b581b401f401f401f401fb88800000000cdcfcad0b0d00000b0cc640000000000491000000000000000000000000064"));

        Assert.True(result);
        var snapshot = harness.Snapshot();
        Assert.Equal("A03217C0B001122E", snapshot.Serial);
        Assert.Equal("Inmotion V14 50S", snapshot.Model);
        Assert.Equal("Main:3.0.40 Drv:5.6.60 BLE:2.0.1", snapshot.Version);

        Assert.Equal(20.92, snapshot.SpeedKmh, 2);
        Assert.Equal(29, snapshot.TemperatureC);
        Assert.Equal(31, snapshot.Temperature2C);
        Assert.Equal(32, snapshot.ImuTemp);
        Assert.Equal(0, snapshot.CpuTemp);
        Assert.Equal(79.0, snapshot.MotorPower, 2);
        Assert.Equal(80.00, snapshot.CurrentLimit, 2);
        Assert.Equal(70.00, snapshot.SpeedLimit, 2);
        Assert.Equal(1.53, snapshot.Torque, 2);
        Assert.Equal(131.80, snapshot.VoltageV, 2);
        Assert.Equal(0.64, snapshot.CurrentA, 2);
        Assert.Equal(0.94, snapshot.WheelDistanceKm, 2);
        Assert.Equal(399650, snapshot.TotalDistance);
        Assert.Equal(99, snapshot.Battery);
        Assert.Equal(102.0, snapshot.PowerW, 2);
        Assert.Equal(0.39, snapshot.Angle, 2);
        Assert.Equal(-0.17, snapshot.Roll, 2);
    }

    [Fact]
    public void Decode_with_v11y_full_data_1()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;

        decoder.Decode(Convert.FromHexString("aaaa110882010206020101009c"));
        decoder.Decode(Convert.FromHexString("aaaa1117820241303332313831304430303130303139000000000083"));
        decoder.Decode(Convert.FromHexString("aaaa112f8206220800030603000802213400050201000902230300030108000201240d00010101000101250d00010101000101ac"));
        decoder.Decode(Convert.FromHexString("aaaa1428a0200410100e401f401f0000006464323232000000005802000a28645a280000144001040100250d92"));
        decoder.Decode(Convert.FromHexString("aaaa142b9000011f261f0000004456569ac5024c005400ccc5d0cb030000003e000000000000002000000073000000f5"));
        decoder.Decode(Convert.FromHexString("aaaa141991c82e0000266708008d62000091e400005e720300d0cb03009e"));
        bool result = decoder.Decode(Convert.FromHexString(
            "aaaa145984941e11000000000087000000090104020000000000006502000000000300000000004b20451fe02e0410100e401f401fa816a816c05d00000000ccc5cecdb0cd0000b0c36400000000004900000000000000000000000000fe"));

        Assert.True(result);
        var snapshot = harness.Snapshot();
        Assert.Equal("A0321810D0010019", snapshot.Serial);
        Assert.Equal("Inmotion V11y", snapshot.Model);
        Assert.Equal("Main:2.5.52 Drv:6.3.8 BLE:1.3.3", snapshot.Version);

        Assert.Equal(1.35, snapshot.SpeedKmh, 2);
        Assert.Equal(28, snapshot.TemperatureC);
        Assert.Equal(21, snapshot.Temperature2C);
        Assert.Equal(29, snapshot.ImuTemp);
        Assert.Equal(0, snapshot.CpuTemp);
        Assert.Equal(0.0, snapshot.MotorPower, 2);
        Assert.Equal(58.00, snapshot.CurrentLimit, 2);
        Assert.Equal(41.00, snapshot.SpeedLimit, 2);
        Assert.Equal(2.65, snapshot.Torque, 2);
        Assert.Equal(78.28, snapshot.VoltageV, 2);
        Assert.Equal(0.17, snapshot.CurrentA, 2);
        Assert.Equal(0.03, snapshot.WheelDistanceKm, 2);
        Assert.Equal(119760, snapshot.TotalDistance);
        Assert.Equal(81, snapshot.Battery);
        Assert.Equal(0.0, snapshot.PowerW, 2);
        Assert.Equal(0.0, snapshot.Angle, 2);
        Assert.Equal(6.13, snapshot.Roll, 2);
    }

    [Fact]
    public void Decode_with_v9_full_data_1()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;

        decoder.Decode(Convert.FromHexString("aaaa11088201020c0101010095"));
        decoder.Decode(Convert.FromHexString("aaaa11178202413134323139353041303030343635460000000000fd"));
        decoder.Decode(Convert.FromHexString("aaaa11388206222800040719000802212600080101000902230a0004010a0002012401000102010001012501000102010001012f0500050101000000b8"));
        decoder.Decode(Convert.FromHexString("aaaa142ca0202a000000071900089411a00f9511000058020064641a020a28646428d0071e32010001012501053015009c"));
        decoder.Decode(Convert.FromHexString("aaaa142b900001162617000000c59d4980520367003100cdc9c9c9060000005d0000000000000044000000ca010000cf"));
        decoder.Decode(Convert.FromHexString("aaaa14199191620000c1a216008bc301006ffe000037890200ffffd5fe55"));
        bool result = decoder.Decode(Convert.FromHexString(
            "aaaa1457843e1e0c000000000000000000afffc30000000000ffffd7fe000000000600000000009a17191670178510a00f401f401fa00fa00f983a00000000cdc900ceb0cec8ceb03a6400000000004900000000000000000000003f"));

        Assert.True(result);
        var snapshot = harness.Snapshot();
        Assert.Equal("A1421950A000465F", snapshot.Serial);
        Assert.Equal("Inmotion V9", snapshot.Model);
        Assert.Equal("Main:1.8.38 Drv:7.4.40 BLE:1.4.10", snapshot.Version);

        Assert.Equal(0.0, snapshot.SpeedKmh, 2);
        Assert.Equal(29, snapshot.TemperatureC);
        Assert.Equal(25, snapshot.Temperature2C);
        Assert.Equal(30, snapshot.ImuTemp);
        Assert.Equal(0, snapshot.CpuTemp);
        Assert.Equal(0.0, snapshot.MotorPower, 2);
        Assert.Equal(40.00, snapshot.CurrentLimit, 2);
        Assert.Equal(42.29, snapshot.SpeedLimit, 2);
        Assert.Equal(-0.81, snapshot.Torque, 2);
        Assert.Equal(77.42, snapshot.VoltageV, 2);
        Assert.Equal(0.12, snapshot.CurrentA, 2);
        Assert.Equal(0.06, snapshot.WheelDistanceKm, 2);
        Assert.Equal(252330, snapshot.TotalDistance);
        Assert.Equal(58, snapshot.Battery);
        Assert.Equal(0.0, snapshot.PowerW, 2);
        Assert.Equal(-0.01, snapshot.Angle, 2);
        Assert.Equal(-2.97, snapshot.Roll, 2);
    }

    [Fact]
    public void Decode_with_v12s_full_data_1()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;

        decoder.Decode(Convert.FromHexString("aaaa11088201020b0101010092"));
        decoder.Decode(Convert.FromHexString("aaaa1117820241313432313934303730303333353943000000000084"));
        decoder.Decode(Convert.FromHexString("aaaa11418206220e0011060300080221380008016b000802232a0003010a0002012400000301040000002508000101040000002e18000001000000012f050005010100000087"));
        decoder.Decode(Convert.FromHexString("aaaa142ca0200000000006030008581b581bb80b0000580210646415020a28646428d0073232040000002508053014008e"));
        decoder.Decode(Convert.FromHexString("aaaa142b900001252626000000050629d50000000000008282828200000000000000000000000000000000090000007d"));
        decoder.Decode(Convert.FromHexString("aaaa1419911d81000080711c00bd92020019000100cd2002008282000037"));
        bool result = decoder.Decode(Convert.FromHexString(
            "aaaa145784b520010000000000000000000000000000000000d7e40000d7e400000000000000002427f026e02e581b581b401f401f581b581b786900000000cdce00ceb0cbccceb0216403000000000000000000000000000000001b"));

        Assert.True(result);
        var snapshot = harness.Snapshot();
        Assert.Equal("A14219407003359C", snapshot.Serial);
        Assert.Equal("Inmotion V12S", snapshot.Model);
        Assert.Equal("Main:1.8.56 Drv:6.17.14 BLE:1.3.42", snapshot.Version);

        Assert.Equal(0.0, snapshot.SpeedKmh, 2);
        Assert.Equal(29, snapshot.TemperatureC);
        Assert.Equal(30, snapshot.Temperature2C);
        Assert.Equal(27, snapshot.ImuTemp);
        Assert.Equal(0, snapshot.CpuTemp);
        Assert.Equal(0.0, snapshot.MotorPower, 2);
        Assert.Equal(70.00, snapshot.CurrentLimit, 2);
        Assert.Equal(70.00, snapshot.SpeedLimit, 2);
        Assert.Equal(0.00, snapshot.Torque, 2);
        Assert.Equal(83.73, snapshot.VoltageV, 2);
        Assert.Equal(0.01, snapshot.CurrentA, 2);
        Assert.Equal(0.0, snapshot.WheelDistanceKm, 2);
        Assert.Equal(330530, snapshot.TotalDistance);
        Assert.Equal(100, snapshot.Battery);
        Assert.Equal(0.0, snapshot.PowerW, 2);
        Assert.Equal(-69.53, snapshot.Angle, 2);
        Assert.Equal(0.0, snapshot.Roll, 2);
    }

    // Settings-data fixtures: the original asserts only that decode() returns true once the
    // following real-time frame lands, not any settings-derived field (see InMotionDecoderV2's
    // class doc on why field extraction there isn't ported).

    [Fact]
    public void Decode_with_v13_settings_data_1()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;

        bool result1 = decoder.Decode(Convert.FromHexString("AAAA1108820102080101010091"));
        bool result2 = decoder.Decode(Convert.FromHexString("AAAA1428A02028233421401F401F5A00006464302C21000000005802000A28645A2800005500010410000000EB"));
        bool result3 = decoder.Decode(Convert.FromHexString(
            "AAAA1459845E2F0F000000F8FF00000000CC11750000000000C900E9FFC8000000000000000000FB20FB1F204E28233421401F401F204E401F709400000000CACCC9CAB0C80000B092000000000000491000000000000000000000000093"));

        Assert.False(result1);
        Assert.False(result2);
        Assert.True(result3);
    }

    [Fact]
    public void Decode_with_v14_settings_data_1()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;

        bool result1 = decoder.Decode(Convert.FromHexString("aaaa1108820102090201010093"));
        bool result2 = decoder.Decode(Convert.FromHexString("AAAA1428A02064196419401F401F6AFF10382F3E324500000000040B000A28385A2800001500000444001E1E55"));
        bool result3 = decoder.Decode(Convert.FromHexString(
            "AAAA145984A7311600000000000000000049000B00000000006CFFA5AAFC6AFF0000000000000000911E1E1E102764196419401F401F401F401FB88800000000CACDC9CAB0C70000B0EA6400000000004910120000000000000000000000D5"));

        Assert.False(result1);
        Assert.False(result2);
        Assert.True(result3);
    }

    [Fact]
    public void Decode_with_v12_settings_data_1()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;

        bool result1 = decoder.Decode(Convert.FromHexString("aaaa110882010207030101009c"));
        bool result2 = decoder.Decode(Convert.FromHexString("AAAA142CA020000000000000000070177C1570179CFF403811575701000064323328AC0D241C800C000000000510010096"));
        bool result3 = decoder.Decode(Convert.FromHexString(
            "AAAA144384A026FEFF000000000000000000000000C4EDC4ED09000000C125AF25983A7017581B000000000000C5C400C6B0C5B06B282800000000000B00000000000000000000D7"));

        Assert.False(result1);
        Assert.False(result2);
        Assert.True(result3);
    }

    [Fact]
    public void Decode_with_v11_settings_data_1()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;

        bool result1 = decoder.Decode(Convert.FromHexString("AAAA110882010206010201009C"));
        bool result2 = decoder.Decode(Convert.FromHexString("AAAA141EA02018155000106464130000000040380000006461501500141001000000E9"));
        bool result3 = decoder.Decode(Convert.FromHexString(
            "AAAA144584631CF6FF000053004E0000000000000051006AFD4F0000000000000023102F0EFD075214641900000000CEB000CED5D3000028000000000049140000000000000000000027"));

        Assert.False(result1);
        Assert.False(result2);
        Assert.True(result3);
    }

    [Fact]
    public void Decode_with_v11y_settings_data_1()
    {
        var harness = DecoderHarness.ForInMotionV2();
        var decoder = harness.Decoder.ProtocolDecoder;

        bool result1 = decoder.Decode(Convert.FromHexString("aaaa110882010206020101009c"));
        bool result2 = decoder.Decode(Convert.FromHexString("AAAA1428A020FC080807401F401F000000474764321F000000005802000A28645A2800001000040400002D1845"));
        bool result3 = decoder.Decode(Convert.FromHexString(
            "AAAA145984671E0D000000000000000000EFFF2B000000000000003704000000000000000000006B192D18E02E9411A00F401F401FA816A816C05D00000000CAC9C7CAB0C90000B070640000000000490012000000000000000000000003"));

        Assert.False(result1);
        Assert.False(result2);
        Assert.True(result3);
    }
}
