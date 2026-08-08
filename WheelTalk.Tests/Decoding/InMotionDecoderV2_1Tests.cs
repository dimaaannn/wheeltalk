using WheelTalk.Core.Battery;
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

    /// <summary>Тот же кадр с type 9: такой пары нет ни в таблице оригинала, ни у нас.</summary>
    private const string CarTypeUnknown = "AAAA1108820102060902010094";

    /// <summary>P6: series 13, type 1. Кадр снят с колеса владельца 02.08.2026.</summary>
    private const string CarTypeP6 = "aaaa11088201020d0101010094";

    /// <summary>P6 в покое: ток около нуля, скорость ноль, пак 230,04 В.</summary>
    private const string P6RealTimeStanding =
        "aaaa145784dc59feff00000000000000001bfc1602000000003900f3fd6400faff5600a600adfca626da25983a983a401f401f401fe02ee02e50c300000000d4e500e0b0dbced1b02800040000000049000000000000010000000085";

    /// <summary>P6 под нагрузкой: 34,99 км/ч, 24,86 А, просадка пака до 223,38 В.</summary>
    private const string P6RealTimeMoving =
        "aaaa1457844257b60900000000ab0d00006024b10ab1152f0ea702c9006400a2005200a6000b1fa626da25983a983a401f401f401fe02ee02e50c300000000d3e000ddb0daced1b09f000400000000490000000000000100000000b0";

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

    /// <summary>
    /// P6: телеметрию разбираем сами. Числа сверены с дампом 02.08.2026 — доказательства в
    /// <c>docs/inmotion-p6-protocol.md</c>.
    /// </summary>
    [Fact]
    public void P6_decodes_the_proven_half_of_its_frame()
    {
        var harness = DecoderHarness.ForInMotionV2_1();
        var decoder = harness.Decoder.ProtocolDecoder;

        decoder.Decode(Convert.FromHexString(CarTypeP6));
        bool decoded = decoder.Decode(Convert.FromHexString(P6RealTimeMoving));

        Assert.True(decoded);
        var snapshot = harness.Snapshot();
        Assert.Equal("Inmotion P6", snapshot.Model);
        Assert.Equal(223.38, snapshot.VoltageV, 2);
        Assert.Equal(24.86, snapshot.CurrentA, 2);
        Assert.Equal(34.99, snapshot.SpeedKmh, 2);

        // Мощность = напряжению на ток, и это тождество держится во всех кадрах дампа — на нём
        // раскладка первых байт и стоит.
        Assert.Equal(223.38 * 24.86, snapshot.PowerW, 0);

        Assert.Equal(98, snapshot.Battery);
        Assert.Equal(35, snapshot.TemperatureC);
        Assert.Equal(48, snapshot.Temperature2C);
    }

    /// <summary>Стоящее колесо: ноль скорости и мощности, но напряжение и температуры на месте.</summary>
    [Fact]
    public void P6_reads_zero_speed_as_zero()
    {
        var harness = DecoderHarness.ForInMotionV2_1();
        var decoder = harness.Decoder.ProtocolDecoder;

        decoder.Decode(Convert.FromHexString(CarTypeP6));
        decoder.Decode(Convert.FromHexString(P6RealTimeStanding));

        var snapshot = harness.Snapshot();
        Assert.Equal(230.04, snapshot.VoltageV, 2);
        Assert.Equal(-0.02, snapshot.CurrentA, 2);
        Assert.Equal(0, snapshot.SpeedKmh);
        Assert.Equal(0, snapshot.PowerW);
        Assert.Equal(36, snapshot.TemperatureC);
        Assert.Equal(53, snapshot.Temperature2C);
    }

    /// <summary>
    /// Обратная сторона той же честности: поля, которые на местах V13 дают у P6 бессмыслицу, не
    /// пишутся вовсе. Ноль здесь значит «не знаем», и это лучше правдоподобного вранья — крена
    /// ровно 1,00° во всех кадрах, ШИМ без связи со скоростью и двадцати аварий из чужих байт.
    /// </summary>
    [Fact]
    public void P6_stays_silent_about_everything_unproven()
    {
        var harness = DecoderHarness.ForInMotionV2_1();
        var decoder = harness.Decoder.ProtocolDecoder;

        decoder.Decode(Convert.FromHexString(CarTypeP6));
        decoder.Decode(Convert.FromHexString(P6RealTimeMoving));

        var snapshot = harness.Snapshot();
        Assert.Equal("", snapshot.Alert);
        Assert.Equal(0, snapshot.Pwm);
        Assert.Equal(0, snapshot.Roll);
        Assert.Equal(0, snapshot.Angle);
        Assert.Equal(0, snapshot.MotorPower);
        Assert.Equal(0, snapshot.Torque);
        Assert.Equal(0, snapshot.CpuTemp);
        Assert.Equal(0, snapshot.CurrentLimit);
    }

    /// <summary>
    /// Ряд, заданный человеком, бьёт знание протокола и у P6 — как у остальных четырёх (см.
    /// <c>CellCountThroughDecodersTests.A_series_set_by_hand_beats_every_lower_step</c>). Без этого
    /// <c>PackCells.Source</c> у P6 не бывает <see cref="CellCountSource.UserSetting"/> никогда, и
    /// шкала на банку не включается даже при заданном ряде.
    /// </summary>
    [Fact]
    public void P6_lets_a_series_set_by_hand_beat_the_protocol()
    {
        var harness = DecoderHarness.ForInMotionV2_1(config => config.CellsInSeries = 32);
        var decoder = harness.Decoder.ProtocolDecoder;

        decoder.Decode(Convert.FromHexString(CarTypeP6));
        decoder.Decode(Convert.FromHexString(P6RealTimeMoving));

        var snapshot = harness.Snapshot();
        Assert.Equal(new CellCount(32, CellCountSource.UserSetting), snapshot.PackCells);
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
