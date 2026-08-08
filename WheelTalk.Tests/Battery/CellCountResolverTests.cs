using WheelTalk.Core.Battery;

namespace WheelTalk.Tests.Battery;

/// <summary>
/// Каскад определения ряда ячеек (план 27 §27.2). Проверяется таблицей случаев — ни телефона, ни
/// колеса, ни BLE тут не нужно: класс чистый, а числа взяты из <c>docs/wheel-voltages.md</c>.
/// </summary>
public class CellCountResolverTests
{
    private static CellCount Resolve(
        double? volts = null,
        int? percent = null,
        double? maxVolts = null,
        int? configured = null,
        int? bms = null,
        int? protocol = null) =>
        CellCountResolver.Resolve(new CellCountInputs
        {
            ConfiguredCells = configured,
            SmartBmsCells = bms,
            ProtocolCells = protocol,
            PackVolts = volts,
            WheelPercent = percent,
            MaxPackVolts = maxVolts,
        });

    [Fact]
    public void Empty_input_is_a_legal_dont_know()
    {
        CellCount result = Resolve();

        Assert.False(result.IsKnown);
        Assert.Equal(CellCountSource.Unknown, result.Source);
    }

    /// <summary>Человек знает своё колесо лучше эвристики — спорить не с чем даже при полном заряде.</summary>
    [Fact]
    public void Human_setting_outranks_every_measurement()
    {
        CellCount result = Resolve(volts: 84, percent: 100, bms: 32, configured: 24);

        Assert.Equal(new CellCount(24, CellCountSource.UserSetting), result);
    }

    /// <summary>Ответ BMS — измерение; знание протокола и напряжение ниже него ступенями.</summary>
    [Fact]
    public void Smart_bms_outranks_the_protocol()
    {
        CellCount result = Resolve(volts: 84, percent: 100, bms: 32, protocol: 20);

        Assert.Equal(new CellCount(32, CellCountSource.SmartBms), result);
    }

    /// <summary>
    /// Знание протокола выше пары «напряжение + процент»: 84 В при 100 % кричат «20S», а протокол
    /// говорит 24 — и он прав, потому что рукопожатие не гадает. На этом стоит весь шаг 27.3.
    /// </summary>
    [Fact]
    public void Protocol_outranks_voltage_with_percent()
    {
        CellCount result = Resolve(volts: 84, percent: 100, protocol: 24);

        Assert.Equal(new CellCount(24, CellCountSource.Protocol), result);
    }

    /// <summary>
    /// Число протокола не сверяется со списком правдоподобных рядов и не правится: спорить с
    /// декодером — не дело этой ступени. Иначе подмена в 27.3 тихо сдвинула бы проценты заряда.
    /// </summary>
    [Theory]
    [InlineData(28)]
    [InlineData(32)]
    public void Protocol_answer_passes_through_verbatim(int protocolCells)
    {
        Assert.Equal(protocolCells, Resolve(volts: 117.6, protocol: protocolCells).Cells);
    }

    /// <summary>Ноль — это «не задано» у обоих: так молчит и настройка, и не ответивший BMS.</summary>
    [Fact]
    public void Zeroes_are_silence_not_answers()
    {
        CellCount result = Resolve(volts: 84, configured: 0, bms: 0);

        Assert.Equal(new CellCount(20, CellCountSource.VoltageGuess), result);
    }

    /// <summary>
    /// Пара «напряжение + процент» разбирает то, чего напряжение в одиночку не разбирает: 84 В —
    /// это и полный 20S, и наполовину разряженный 24S. 90 В при 100 % показывают отбраковку
    /// потолком физики: 20S дал бы 4,5 В на ячейку, и никакой процент ему уже не поможет.
    /// </summary>
    [Theory]
    [InlineData(84, 100, 20)]
    [InlineData(84, 50, 24)]
    [InlineData(126, 100, 30)]
    [InlineData(126, 70, 32)]
    [InlineData(90, 100, 24)]
    public void Voltage_with_percent_tells_the_series(double volts, int percent, int expected)
    {
        Assert.Equal(new CellCount(expected, CellCountSource.VoltageWithPercent), Resolve(volts, percent));
    }

    /// <summary>
    /// Догадка по одному напряжению. 117,6 В — тот самый случай «ряд вне списка»: делением выходит
    /// 28, которых не бывает, и берётся ближайший существующий, 30. 118,4 В — 32S, виденный только
    /// полупустым: занижается до 30, и это та самая односторонняя ошибка из плана.
    /// </summary>
    [Theory]
    [InlineData(67.2, 16)]
    [InlineData(84, 20)]
    [InlineData(117.6, 30)]
    [InlineData(118.4, 30)]
    [InlineData(235.2, 56)]
    public void Voltage_alone_is_only_a_guess(double volts, int expected)
    {
        Assert.Equal(new CellCount(expected, CellCountSource.VoltageGuess), Resolve(volts));
    }

    /// <summary>Ниже излома кривой процент не значит ничего — спускаемся к максимуму напряжения.</summary>
    [Fact]
    public void Almost_empty_percent_is_not_trusted()
    {
        CellCount result = Resolve(volts: 100.8, percent: 5, maxVolts: 134.4);

        Assert.Equal(new CellCount(32, CellCountSource.VoltageGuess), result);
    }

    /// <summary>
    /// 30S и 32S на исходе заряда предсказывают почти один и тот же процент — пара неразличима,
    /// и ступень честно молчит, отдавая ответ догадке.
    /// </summary>
    [Fact]
    public void Indistinguishable_pair_falls_through_to_the_guess()
    {
        CellCount result = Resolve(volts: 100, percent: 10);

        Assert.Equal(new CellCount(24, CellCountSource.VoltageGuess), result);
    }

    /// <summary>До первого кадра напряжение — ноль; «16S» на нём было бы выдумкой.</summary>
    [Fact]
    public void Zero_voltage_answers_nothing()
    {
        Assert.False(Resolve(volts: 0, percent: 50).IsKnown);
    }

    /// <summary>Колесо за пределами таблицы рядов: 300 В не даёт ни одного правдоподобного ряда.</summary>
    [Fact]
    public void Voltage_beyond_the_known_series_answers_nothing()
    {
        Assert.False(Resolve(volts: 300).IsKnown);
    }
}
