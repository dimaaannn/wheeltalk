namespace WheelTalk.Core.Battery;

/// <summary>Чего стоит полученное число вольт на ячейку.</summary>
public enum CellVoltageStatus
{
    /// <summary>Считать было не из чего: ряд неизвестен либо напряжения ещё не видели.</summary>
    Unknown,

    /// <summary>Поделили, и вышло похожее на правду.</summary>
    Known,

    /// <summary>
    /// Поделили — и вышло за пределы живой ячейки. Виноват <b>ряд</b>, а не ячейка: пакет,
    /// показывающий 4,9 В на ячейку, — это не пакет, а неверный делитель. Печатать такое число
    /// райдеру нельзя; это сигнал, что ряд надо задать руками.
    /// </summary>
    ImplausibleSeries,
}

/// <summary>
/// Вольт на ячейку — и происхождение того ряда, которым получено. Источник едет вместе с числом
/// весь путь: «3,7 В по данным BMS» и «3,7 В по догадке» — разные утверждения, и на экране они
/// однажды разойдутся. Как именно их различать, решает тот, кто показывает; потерять источник по
/// дороге он не может, потому что его тут не отделить от числа.
/// </summary>
/// <param name="Volts">Вольт на ячейку. При <see cref="CellVoltageStatus.Unknown"/> — ноль; при
/// <see cref="CellVoltageStatus.ImplausibleSeries"/> — то самое неправдоподобное число, оставленное
/// для журнала и разбора, но не для экрана.</param>
/// <param name="Source">Откуда взялся ряд, которым делили.</param>
/// <param name="Status">Годится ли число в дело.</param>
public readonly record struct CellVoltage(double Volts, CellCountSource Source, CellVoltageStatus Status)
{
    public static CellVoltage Unknown => new(0, CellCountSource.Unknown, CellVoltageStatus.Unknown);

    public bool IsKnown => Status == CellVoltageStatus.Known;
}
