using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// InMotion V2-1 — надстройка, а не порт, поэтому фикстур оригинала для неё нет: кадры взяты из
/// <see cref="InMotionDecoderV2Tests"/>, а проверяется здесь единственное её решение — пускать
/// кадр в нетронутый <c>InMotionDecoderV2</c> или нет.
/// </summary>
public class InMotionDecoderV2_1Tests
{
    /// <summary>Тип колеса из таблицы оригинала: series 6, type 1 — Inmotion V11.</summary>
    private const string CarTypeV11 = "AAAA110882010206010201009C";

    /// <summary>Тот же кадр с type 9: такой пары в таблице нет — как у P6.</summary>
    private const string CarTypeUnknown = "AAAA1108820102060902010094";

    private const string SerialNumber = "AAAA11178202313438304341313232323037303032420000000000FD";

    /// <summary>Кадр телеметрии с экранированными <c>A5</c> в теле и в контрольной сумме — на нём
    /// видно, что байты доходят до V2 такими же, какими пришли с провода.</summary>
    private const string RealTimeWithEscapes =
        "aaaa1431843020a5a50068025207870080009400882c5fc4b000d7001000f4ff2b037c1564190000d9d9492b00000000000000000000a5a5";

    [Fact]
    public void Known_model_decodes_exactly_as_v2()
    {
        var harness = DecoderHarness.ForInMotionV2_1();
        var decoder = harness.Decoder.ProtocolDecoder;

        decoder.Decode(Convert.FromHexString(CarTypeV11));
        bool decoded = decoder.Decode(Convert.FromHexString(RealTimeWithEscapes));

        Assert.True(decoded);
        var snapshot = harness.Snapshot();
        Assert.Equal("Inmotion V11", snapshot.Model);
        Assert.Equal(6.16, snapshot.SpeedKmh, 2);
        Assert.Equal(82.40, snapshot.VoltageV, 2);
        Assert.Equal(20, snapshot.TemperatureC);
        Assert.Equal(95, snapshot.Battery);
    }

    [Fact]
    public void Unknown_model_keeps_handshake_and_drops_telemetry()
    {
        var harness = DecoderHarness.ForInMotionV2_1();
        var decoder = harness.Decoder.ProtocolDecoder;

        decoder.Decode(Convert.FromHexString(CarTypeUnknown));
        decoder.Decode(Convert.FromHexString(SerialNumber));
        bool decoded = decoder.Decode(Convert.FromHexString(RealTimeWithEscapes));

        Assert.False(decoded);

        var snapshot = harness.Snapshot();
        Assert.Equal("1480CA122207002B", snapshot.Serial);
        Assert.Equal(0, snapshot.SpeedKmh);
        Assert.Equal(0, snapshot.VoltageV);
        Assert.Equal("", snapshot.Alert);
    }
}
