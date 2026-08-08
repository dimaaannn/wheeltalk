using WheelTalk.Core.Battery;

namespace WheelTalk.Tests.Battery;

/// <summary>
/// Вольт на ячейку. Само деление проверять нечего — проверяются случаи, когда делить нельзя или
/// поделённому нельзя верить, и то, что источник ряда доезжает до ответа целым.
/// </summary>
public class CellVoltageResolverTests
{
    /// <summary>
    /// Один честный случай на каждый источник: число одно и то же, а утверждения разные — потому
    /// источник и едет с ним рядом.
    /// </summary>
    [Theory]
    [InlineData(CellCountSource.UserSetting)]
    [InlineData(CellCountSource.SmartBms)]
    [InlineData(CellCountSource.VoltageWithPercent)]
    [InlineData(CellCountSource.VoltageGuess)]
    public void Source_of_the_series_reaches_the_answer(CellCountSource source)
    {
        CellVoltage result = CellVoltageResolver.Resolve(new CellCount(24, source), 84);

        Assert.Equal(new CellVoltage(3.5, source, CellVoltageStatus.Known), result);
    }

    /// <summary>Нет ряда — нет ответа: ни деления на ноль, ни молчаливого нуля вольт.</summary>
    [Fact]
    public void Unknown_series_gives_no_voltage()
    {
        CellVoltage result = CellVoltageResolver.Resolve(CellCount.Unknown, 84);

        Assert.False(result.IsKnown);
        Assert.Equal(CellVoltageStatus.Unknown, result.Status);
    }

    /// <summary>
    /// Кадра ещё не было. Ряд при этом может быть верен, поэтому это «не знаем», а не обвинение
    /// ряду.
    /// </summary>
    [Fact]
    public void No_voltage_yet_is_not_a_wrong_series()
    {
        CellVoltage result = CellVoltageResolver.Resolve(new CellCount(20, CellCountSource.UserSetting), 0);

        Assert.Equal(CellVoltageStatus.Unknown, result.Status);
    }

    /// <summary>
    /// Вылет за пределы живой ячейки — сигнал о неверном ряде, а не ответ. Сверху: 100,8 В при
    /// 20S дают 5,04 В на ячейку — это 24S. Снизу: 84 В при 60S дают 1,4 — это 20S. Само число
    /// остаётся в ответе, но годится только в журнал.
    /// </summary>
    [Theory]
    [InlineData(20, 100.8, 5.04)]
    [InlineData(60, 84, 1.4)]
    public void Implausible_result_accuses_the_series(int cells, double packVolts, double expectedVolts)
    {
        CellVoltage result = CellVoltageResolver.Resolve(new CellCount(cells, CellCountSource.VoltageGuess), packVolts);

        Assert.False(result.IsKnown);
        Assert.Equal(CellVoltageStatus.ImplausibleSeries, result.Status);
        Assert.Equal(expectedVolts, result.Volts, 3);
    }

    /// <summary>Обе границы включительно: 85 В при 20S — ровно 4,25; 84 В при 30S — ровно 2,8.</summary>
    [Theory]
    [InlineData(20, 85)]
    [InlineData(30, 84)]
    public void Borders_are_still_an_answer(int cells, double packVolts)
    {
        CellVoltage result = CellVoltageResolver.Resolve(new CellCount(cells, CellCountSource.SmartBms), packVolts);

        Assert.True(result.IsKnown);
    }
}
