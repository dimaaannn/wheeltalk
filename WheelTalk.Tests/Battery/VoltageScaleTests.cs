using WheelTalk.Core.Battery;

namespace WheelTalk.Tests.Battery;

/// <summary>
/// Чем меряет левая шкала (план 27 §27.4). Проверяется не арифметика деления, а два правила, на
/// которых держится выбор из трёх пунктов: <b>режимы не смешиваются</b> и <b>режим, которому нечем
/// считать, возвращается к вольтам пакета</b>.
/// </summary>
public class VoltageScaleTests
{
    private const double PackVolts = 84;

    /// <summary>Двадцать банок по слову человека — ровно 4,2 В на банку при 84 В пакета.</summary>
    private static readonly CellCount ByHand = new(20, CellCountSource.UserSetting);

    /// <summary>
    /// Главное правило. У колеса живой BMS, и его среднее заведомо расходится с частным «пакет ÷
    /// введённое число» — в расчётном режиме шкала обязана показать второе.
    /// <para>
    /// Стоит подмешать сюда измеренные банки, и два пункта из трёх станут одним и тем же: человеку
    /// будет не из чего выбирать, а его собственное число — нечем проверить.
    /// </para>
    /// </summary>
    [Fact]
    public void The_calculated_mode_ignores_the_bms_even_when_it_is_talking()
    {
        // BMS насчитал бы 24 банки: 3,5 В при 84 В пакета. Человек сказал 20.
        double divisor = VoltageScale.Divisor(VoltageScaleMode.Cells, PackVolts, bmsCellVolts: 3.5, ByHand);

        Assert.Equal(20, divisor);
        Assert.Equal(4.2, PackVolts / divisor, 3);
    }

    /// <summary>И наоборот: режим BMS не заглядывает в число человека — он на то и другой пункт.</summary>
    [Fact]
    public void The_bms_mode_ignores_the_number_typed_by_hand()
    {
        double divisor = VoltageScale.Divisor(VoltageScaleMode.Bms, PackVolts, bmsCellVolts: 3.5, ByHand);

        Assert.Equal(24, divisor);
    }

    [Fact]
    public void The_pack_mode_divides_by_nothing_whatever_else_is_known()
    {
        Assert.Equal(1, VoltageScale.Divisor(VoltageScaleMode.Pack, PackVolts, bmsCellVolts: 3.5, ByHand));
    }

    /// <summary>
    /// Ряд, добытый каскадом, а не человеком, на шкалу не пускается: пункт «по числу ячеек» про
    /// введённое число, и догадка под его видом была бы подлогом. Проценты заряда таким рядом
    /// считаются по-прежнему — это разные потребители одного ответа.
    /// </summary>
    [Theory]
    [InlineData(CellCountSource.Unknown)]
    [InlineData(CellCountSource.SmartBms)]
    [InlineData(CellCountSource.Protocol)]
    [InlineData(CellCountSource.VoltageWithPercent)]
    [InlineData(CellCountSource.VoltageGuess)]
    public void The_calculated_mode_takes_nothing_but_the_number_typed_by_hand(CellCountSource source)
    {
        double divisor = VoltageScale.Divisor(VoltageScaleMode.Cells, PackVolts, bmsCellVolts: 0, new CellCount(20, source));

        Assert.Equal(1, divisor);
    }

    /// <summary>Колесо без BMS: пункт выбран, считать нечем — лента возвращается к вольтам пакета.</summary>
    [Fact]
    public void A_mode_with_nothing_to_divide_by_falls_back_to_pack_volts()
    {
        Assert.Equal(1, VoltageScale.Divisor(VoltageScaleMode.Bms, PackVolts, bmsCellVolts: 0, CellCount.Unknown));
        Assert.Equal(1, VoltageScale.Divisor(VoltageScaleMode.Cells, PackVolts, bmsCellVolts: 0, CellCount.Unknown));
    }

    /// <summary>
    /// До первого кадра делить нечего вовсе. Ноль в знаменателе дал бы бесконечность, а она на
    /// шкале выглядит не ошибкой, а пустотой.
    /// </summary>
    [Fact]
    public void Before_the_first_frame_there_is_nothing_to_divide()
    {
        Assert.Equal(1, VoltageScale.Divisor(VoltageScaleMode.Cells, packVolts: 0, bmsCellVolts: 0, ByHand));
        Assert.Equal(1, VoltageScale.Divisor(VoltageScaleMode.Bms, packVolts: 0, bmsCellVolts: 3.5, ByHand));
    }

    /// <summary>
    /// Банка выше целого пакета — испорченные данные, а не шкала: делитель меньше единицы растянул
    /// бы ленту вдвое и выглядел бы исправным прибором.
    /// </summary>
    [Fact]
    public void A_cell_above_the_whole_pack_is_broken_data_and_shows_as_pack_volts()
    {
        Assert.Equal(1, VoltageScale.Divisor(VoltageScaleMode.Bms, packVolts: 3, bmsCellVolts: 3.5, CellCount.Unknown));
    }
}
