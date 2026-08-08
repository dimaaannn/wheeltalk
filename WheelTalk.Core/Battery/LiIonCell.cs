namespace WheelTalk.Core.Battery;

/// <summary>
/// Пределы живой ячейки Li-ion — одни на весь узел: по ним <see cref="CellCountResolver"/>
/// отбрасывает невозможные ряды, а <see cref="CellVoltageResolver"/> ловит неверный ряд по вылету
/// за них. Второй копии этих чисел быть не должно: разойдясь, они дадут ряд, который один класс
/// считает возможным, а другой — нет.
/// </summary>
public static class LiIonCell
{
    /// <summary>
    /// Ниже пакет не ездит. Граница отсекает не разряженное колесо, а показание, которое вообще не
    /// про пакет: до первого кадра напряжение — ноль, и без неё ноль вольт уверенно «опознался» бы
    /// как 16S.
    /// </summary>
    public const double MinVolts = 2.8;

    /// <summary>Выше ячейка Li-ion не живёт. Такой ряд физически невозможен, и процент ему не судья.</summary>
    public const double MaxVolts = 4.25;

    public static bool IsPlausible(double volts) => volts is >= MinVolts and <= MaxVolts;
}
