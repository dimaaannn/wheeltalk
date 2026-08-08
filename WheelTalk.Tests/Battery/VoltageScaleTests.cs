using WheelTalk.Core.Battery;

namespace WheelTalk.Tests.Battery;

/// <summary>
/// Чем меряет левая шкала (план 27 §27.4). Проверяется правило, на котором держится весь показ:
/// <b>лента не гадает</b> — она делит на число из настройки колеса, и ни на что другое. Догадка
/// живёт за кнопкой «рассчитать», и её ответ попадает сюда только через эту настройку.
/// </summary>
public class VoltageScaleTests
{
    private const double PackVolts = 84;

    /// <summary>Двадцать банок по слову человека — ровно 4,2 В на банку при 84 В пакета.</summary>
    private static readonly CellCount ByHand = new(20, CellCountSource.UserSetting);

    [Fact]
    public void The_cell_scale_divides_by_the_number_from_the_setting()
    {
        double divisor = VoltageScale.Divisor(VoltageScaleMode.Cells, PackVolts, ByHand);

        Assert.Equal(20, divisor);
        Assert.Equal(4.2, PackVolts / divisor, 3);
    }

    [Fact]
    public void The_pack_scale_divides_by_nothing_whatever_else_is_known()
    {
        Assert.Equal(1, VoltageScale.Divisor(VoltageScaleMode.Pack, PackVolts, ByHand));
    }

    /// <summary>
    /// Ряд, добытый каскадом, а не человеком, на ленту не пускается: показ делит на записанное, и
    /// догадка под видом настройки была бы подлогом. Проценты заряда таким рядом считаются
    /// по-прежнему — это разные потребители одного ответа.
    /// </summary>
    [Theory]
    [InlineData(CellCountSource.Unknown)]
    [InlineData(CellCountSource.SmartBms)]
    [InlineData(CellCountSource.Protocol)]
    [InlineData(CellCountSource.VoltageWithPercent)]
    [InlineData(CellCountSource.VoltageGuess)]
    public void The_cell_scale_takes_nothing_but_the_number_from_the_setting(CellCountSource source)
    {
        double divisor = VoltageScale.Divisor(VoltageScaleMode.Cells, PackVolts, new CellCount(20, source));

        Assert.Equal(1, divisor);
    }

    /// <summary>Число не задано — лента возвращается к вольтам пакета, а не показывает вздор.</summary>
    [Fact]
    public void Without_a_number_the_scale_falls_back_to_pack_volts()
    {
        Assert.Equal(1, VoltageScale.Divisor(VoltageScaleMode.Cells, PackVolts, CellCount.Unknown));
    }

    /// <summary>
    /// До первого кадра делить нечего вовсе. Ноль в знаменателе дал бы бесконечность, а она на
    /// шкале выглядит не ошибкой, а пустотой.
    /// </summary>
    [Fact]
    public void Before_the_first_frame_there_is_nothing_to_divide()
    {
        Assert.Equal(1, VoltageScale.Divisor(VoltageScaleMode.Cells, packVolts: 0, ByHand));
    }

    /// <summary>Ряд из одной банки — не шкала, а испорченные данные: лента остаётся пакетной.</summary>
    [Fact]
    public void A_series_of_one_is_broken_data_and_shows_as_pack_volts()
    {
        double divisor = VoltageScale.Divisor(
            VoltageScaleMode.Cells, PackVolts, new CellCount(1, CellCountSource.UserSetting));

        Assert.Equal(1, divisor);
    }
}
