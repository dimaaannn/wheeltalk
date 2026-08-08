namespace WheelTalk.Core.Battery;

/// <summary>
/// Откуда взялось число ячеек. Порядок членов — порядок каскада из плана 27, по убыванию
/// надёжности: каждая следующая ступень слабее предыдущей.
/// <para>
/// Источник едет вместе с числом не для красоты: «24 ячейки, потому что так сказал BMS» и
/// «24 ячейки, потому что мы поделили на 4,2» — разные утверждения, и показывать их райдеру надо
/// по-разному.
/// </para>
/// </summary>
public enum CellCountSource
{
    /// <summary>Не ответил ни один источник. Законный ответ, а не отказ.</summary>
    Unknown,

    /// <summary>Задано человеком. Он знает своё колесо лучше любой эвристики — не спорим.</summary>
    UserSetting,

    /// <summary>Ответил умный BMS. Это измерение, а не догадка.</summary>
    SmartBms,

    /// <summary>Разобрано парой «напряжение + процент от колеса», снятых в один момент.</summary>
    VoltageWithPercent,

    /// <summary>
    /// Догадка по одному напряжению. Ошибка у неё односторонняя: полупустой 32S назовётся 30S,
    /// обратного не бывает — значит ряд занижается, а вольт на ячейку выходит оптимистичнее истины.
    /// </summary>
    VoltageGuess,
}

/// <summary>
/// Сколько ячеек стоит <b>последовательно</b> (S) — и откуда это известно. Параллели (P) на
/// напряжение не влияют вовсе и здесь не участвуют: 20S4P и 20S8P дают одни и те же 84 В.
/// </summary>
/// <param name="Cells">Ряд S. При <see cref="CellCountSource.Unknown"/> — ноль.</param>
/// <param name="Source">Ступень каскада, давшая ответ.</param>
public readonly record struct CellCount(int Cells, CellCountSource Source)
{
    public static CellCount Unknown => new(0, CellCountSource.Unknown);

    /// <summary>
    /// Спрашивать <b>до</b> обращения к <see cref="Cells"/>: у незнания там ноль, а им делят
    /// напряжение при расчёте кривой заряда.
    /// </summary>
    public bool IsKnown => Source != CellCountSource.Unknown;
}
