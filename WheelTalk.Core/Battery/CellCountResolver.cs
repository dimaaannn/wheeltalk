namespace WheelTalk.Core.Battery;

/// <summary>
/// Сколько ячеек стоит в пакете последовательно — чистая функция без состояния и зависимостей
/// (план 27 §27.2). Каскад сверху вниз: настройка человека → ответ BMS → напряжение вместе с
/// процентом от колеса → догадка по одному напряжению. Ответил кто-то выше — ниже не спускаемся.
/// <para>
/// Сила ответа не в знании верного, а в отбрасывании невозможного: рядов на свете мало
/// (<see cref="PlausibleSeries"/>, ресерч в <c>docs/wheel-voltages.md</c>), и потолок физики
/// 4,25 В на ячейку убирает часть из них ещё до всякого процента.
/// </para>
/// <para>
/// Модель колеса ступенью не является: §27.1а показал, что тесные пары рядов (40/42, 56/60,
/// 30/32) не пересекают границы протоколов, а протокол декодеру известен всегда. Справочника
/// моделей поэтому нет ни здесь, ни в ядре вообще.
/// </para>
/// </summary>
public static class CellCountResolver
{
    /// <summary>
    /// Одиннадцать рядов, какие встречаются у колёс от 64 В (<c>docs/wheel-voltages.md</c>).
    /// Все чётные; нет ни 28, ни 34, ни 44, ни 48. Ряда вне этого списка алгоритм не назовёт
    /// никогда — иначе догадка перестаёт быть догадкой и становится делением.
    /// </summary>
    private static readonly int[] PlausibleSeries = [16, 20, 24, 30, 32, 36, 40, 42, 50, 56, 60];

    /// <summary>Выше этого ячейка Li-ion не живёт: такой ряд физически невозможен, и процент ему не судья.</summary>
    private const double MaxCellVolts = 4.25;

    /// <summary>
    /// Ниже этого пакет не ездит. Порог отсекает не разряженное колесо, а показание, которое
    /// вообще не про пакет: до первого кадра напряжение — ноль, и без границы снизу ноль вольт
    /// уверенно «опознался» бы как 16S.
    /// </summary>
    private const double MinCellVolts = 2.8;

    // Кривая заряда — наша собственная, из GotwayDecoder/KingsongDecoder (ветка UseBetterPercents):
    // все её модельные ветки сводятся к одному и тому же на ячейку — 3,2 В = 0 %, 4,175 В = 100 %,
    // с изломом на 3,4 В (~8,8 %). Коэффициенты получены делением 84-вольтовой ветки на 20 ячеек.
    private const double FullCellVolts = 4.175;
    private const double KneeCellVolts = 3.4;
    private const double EmptyCellVolts = 3.2;
    private const double UpperSegmentZeroVolts = 3.325;
    private const double UpperSegmentVoltsPerPercent = 0.0085;
    private const double LowerSegmentVoltsPerPercent = 0.0225;

    /// <summary>Делитель догадки: «напряжение колеса» в спеках — это заряд под завязку, S × 4,2.</summary>
    private const double FullChargeCellVolts = 4.2;

    /// <summary>
    /// Ниже этого процент не разрешает ничего: под изломом кривая полога и ступенчата — 30S и 32S
    /// расходятся там на 4–9 пунктов, то есть неразличимы. Честнее спуститься к догадке.
    /// </summary>
    private const int MinTrustedPercent = 9;

    /// <summary>
    /// Насколько должны разойтись проценты, предсказанные двумя рядами при одном напряжении, чтобы
    /// выбор между ними считался решённым. Порог грубый нарочно: §27.1а посчитал разнос — у тесных
    /// пар 20–33 пункта, у всех прочих 45–70. Точность прошивочного процента такова, что тонкий
    /// порог был бы самообманом.
    /// </summary>
    private const double SeriesSeparationPercent = 20;

    public static CellCount Resolve(CellCountInputs inputs)
    {
        if (inputs.ConfiguredCells > 0) return new CellCount(inputs.ConfiguredCells.Value, CellCountSource.UserSetting);
        if (inputs.SmartBmsCells > 0) return new CellCount(inputs.SmartBmsCells.Value, CellCountSource.SmartBms);

        if (SeriesFromVoltageAndPercent(inputs.PackVolts, inputs.WheelPercent) is { } byPair)
        {
            return new CellCount(byPair, CellCountSource.VoltageWithPercent);
        }

        return SeriesFromVoltageAlone(inputs.MaxPackVolts ?? inputs.PackVolts) is { } byVoltage
            ? new CellCount(byVoltage, CellCountSource.VoltageGuess)
            : CellCount.Unknown;
    }

    /// <summary>
    /// Процент задаёт напряжение на ячейке, напряжение пакета — деление, деление — ряд. Ответ
    /// принимается, только если соседние ряды предсказывают при этом же напряжении заметно другой
    /// процент: сошлись ближе <see cref="SeriesSeparationPercent"/> — пара неразличима, и ступень
    /// молчит вместо того, чтобы гадать под видом расчёта.
    /// </summary>
    private static int? SeriesFromVoltageAndPercent(double? packVolts, int? wheelPercent)
    {
        if (packVolts is not > 0 || wheelPercent is not (>= MinTrustedPercent and <= 100)) return null;

        double volts = packVolts.Value;
        List<int> candidates = PossibleSeries(volts);
        if (candidates.Count == 0) return null;

        double cellVoltsByPercent = UpperSegmentZeroVolts + wheelPercent.Value * UpperSegmentVoltsPerPercent;
        int nearest = candidates.MinBy(series => Math.Abs(series - volts / cellVoltsByPercent));

        double nearestPercent = PercentForCellVolts(volts / nearest);
        bool distinguishable = candidates.All(series => series == nearest
            || Math.Abs(PercentForCellVolts(volts / series) - nearestPercent) > SeriesSeparationPercent);

        return distinguishable ? nearest : null;
    }

    /// <summary>
    /// Последняя ступень: делим на 4,2 и берём ближайший правдоподобный ряд из уцелевших после
    /// границ по вольту на ячейку.
    /// <para>
    /// Отсюда ответ на «а если ряд вышел вне списка» — например 117,6 В дают ровно 28, которых не
    /// бывает: <b>берётся ближайший существующий, то есть 30</b>, и помечается догадкой. Отказ был
    /// бы неверен по существу — точное кратное 4,2 случается только у полного колеса, а
    /// полуразряженное даёт нецелое всегда, и отказывать пришлось бы почти всем. 117,6 В — это
    /// обычный 126-вольтовый пак на 3,92 В/ячейку; 32S на 3,675 В тоже возможен, и как раз его
    /// берёт декодер Begode при настройке <c>"3"</c>. Расхождение известно, чинится оно шагом 27.3
    /// осознанно, а не здесь молча.
    /// </para>
    /// </summary>
    private static int? SeriesFromVoltageAlone(double? packVolts)
    {
        if (packVolts is not > 0) return null;

        double volts = packVolts.Value;
        List<int> candidates = PossibleSeries(volts);

        return candidates.Count == 0
            ? null
            : candidates.MinBy(series => Math.Abs(series - volts / FullChargeCellVolts));
    }

    /// <summary>Ряды, при которых вольт на ячейку остаётся в пределах живого Li-ion.</summary>
    private static List<int> PossibleSeries(double packVolts) =>
        [.. PlausibleSeries.Where(series => packVolts / series is >= MinCellVolts and <= MaxCellVolts)];

    private static double PercentForCellVolts(double cellVolts) => cellVolts switch
    {
        >= FullCellVolts => 100,
        >= KneeCellVolts => (cellVolts - UpperSegmentZeroVolts) / UpperSegmentVoltsPerPercent,
        > EmptyCellVolts => (cellVolts - EmptyCellVolts) / LowerSegmentVoltsPerPercent,
        _ => 0,
    };
}
