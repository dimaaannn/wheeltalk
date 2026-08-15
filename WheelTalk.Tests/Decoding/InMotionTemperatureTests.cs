using WheelTalk.Core.Decoding;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Байтовые тесты для <see cref="InMotionDecoderV2.DecodeTemperatureC"/> — общий счёт температуры
/// для всех 26 мест InMotion V2/P6 (24 в <c>InMotionDecoderV2.cs</c>, 2 в
/// <c>InMotionP6RealTime.cs</c>). Формула производителя (см. <c>docs/inmotion-loeuc-comparison.md</c>,
/// «Формула температуры», и запись отклонения — <c>docs/port-deviations.md</c>) — знаковый байт + 80.
/// Старая портовая формула, унаследованная от WheelLog, — <c>(b &amp; 0xff) + 80 - 256</c>: обе
/// совпадают при сыром байте ≥ 0x80 и расходятся ровно на 256 при байте &lt; 0x80.
/// </summary>
public class InMotionTemperatureTests
{
    /// <summary>
    /// Граница 127/128 — ровно та, где старая и новая формулы расходятся/совпадают.
    /// 127 (0x7F, последний байт горячей зоны): старая формула дала бы 127 + 80 - 256 = -49
    /// (то самое враньё в −176…−97 °C), новая — (sbyte)127 + 80 = 207 (перегрев, как говорит колесо).
    /// 128 (0x80, первый байт холодной зоны): обе формулы дают -48 — здесь порт не менял поведения.
    /// </summary>
    [Theory]
    [InlineData(127, 207)]
    [InlineData(128, -48)]
    public void Boundary_between_old_and_new_formula(byte raw, int expectedC)
    {
        Assert.Equal(expectedC, InMotionDecoderV2.DecodeTemperatureC(raw));
    }

    /// <summary>
    /// Холодные типовые значения (диапазон, в котором держались оба наших дампа P6, и на котором
    /// формулы всегда совпадали) — старое поведение в зоне совпадения не сломано.
    /// </summary>
    [Theory]
    [InlineData(0xB0, 0)]   // 176 — реально снятый байт «cpuTemp» на непопулированном датчике P6
    [InlineData(0xD1, 33)]  // 209 — типовое рабочее значение (см. Decode_v11_new_fw_with_pwm)
    [InlineData(0xFF, 79)]  // 255 — верхняя граница холодной зоны
    public void Cold_zone_values_are_unchanged(byte raw, int expectedC)
    {
        int oldFormula = (raw & 0xff) + 80 - 256;
        Assert.Equal(expectedC, oldFormula);
        Assert.Equal(expectedC, InMotionDecoderV2.DecodeTemperatureC(raw));
    }

    /// <summary>
    /// Тот же счёт достижим и с другой стороны границы, за пределами теста самого метода: сырой
    /// байт 0x00 (эксплуатируется в <see cref="InMotionDecoderV2Tests.Decode_with_v11_full_data"/>)
    /// — старая формула дала -176, новая — +80.
    /// </summary>
    [Fact]
    public void Hot_zone_zero_byte_no_longer_wraps_to_negative_176()
    {
        int oldFormula = (0 & 0xff) + 80 - 256;
        Assert.Equal(-176, oldFormula);
        Assert.Equal(80, InMotionDecoderV2.DecodeTemperatureC(0));
    }

    /// <summary>
    /// Сквозная проверка через P6: реальный перегрев (сырой байт 40, глубоко в горячей зоне — как
    /// формулировал владелец, «+80…+207 °C, приборный перегрев») доходит из
    /// <see cref="InMotionP6RealTime.Apply"/> в состояние верным числом, а не завёрнутым в минус.
    /// </summary>
    [Fact]
    public void P6_real_time_reports_overheat_instead_of_wrapping_negative()
    {
        var config = new AppWheelConfig();
        var state = new WheelState(config, TimeProvider.System);
        byte[] data = new byte[86];
        data[58] = 40;  // mosTemp raw: (sbyte)40 + 80 = 120 °C — реальный перегрев MOSFET
        data[59] = 128; // motTemp raw: холодная зона, обе формулы дают -48 °C — не тронуто

        bool applied = InMotionP6RealTime.Apply(data, state, config);

        Assert.True(applied);
        Assert.Equal(120, state.Temperature / 100);
        Assert.Equal(-48, state.Temperature2 / 100);
    }
}
