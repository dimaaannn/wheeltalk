namespace WheelTalk.Core.Battery;

/// <summary>
/// Вольт на ячейку: напряжение пакета, делённое на ряд. Чистая функция без состояния и
/// зависимостей — как и <see cref="CellCountResolver"/>, чьим ответом она кормится.
/// <para>
/// Арифметика тут в одну строку, и весь класс не про неё, а про три случая, в которых делить
/// нельзя или полученному нельзя верить.
/// </para>
/// </summary>
public static class CellVoltageResolver
{
    /// <param name="cells">Ответ определителя ряда — целиком, вместе с источником.</param>
    /// <param name="packVolts">Напряжение пакета, вольты. Ноль — «кадра ещё не было».</param>
    public static CellVoltage Resolve(CellCount cells, double? packVolts)
    {
        if (!cells.IsKnown || packVolts is not > 0) return CellVoltage.Unknown;

        double cellVolts = packVolts.Value / cells.Cells;
        CellVoltageStatus status = LiIonCell.IsPlausible(cellVolts)
            ? CellVoltageStatus.Known
            : CellVoltageStatus.ImplausibleSeries;

        return new CellVoltage(cellVolts, cells.Source, status);
    }
}
