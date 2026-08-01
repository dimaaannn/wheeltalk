namespace WheelTalk.Core.Alerts;

/// <summary>
/// Ритм тревоги по ШИМ, снятый с оригинала: <b>период постоянный, растёт только длина сигнала</b>.
/// Двести миллисекунд от начала одного сигнала до начала следующего всегда, а сам сигнал тянется
/// от 20 мс на пороге тревоги до тех же 200 мс на пороге полной
/// (<c>Alarms.kt:117-121</c>, <c>checkPeriod = 200</c>).
/// <para>
/// Отсюда всё и следует. На подходе к пределу писки коротки и редки, ближе к нему — длиннее и
/// плотнее, а на потолке сигнал занимает весь период и <b>сливается в сплошной сам</b>. Отдельного
/// «сплошного режима» нет и не нужно: он получается из тех же двух чисел, а значит нет и
/// переключения между режимами — того самого, что дребезжало на границе.
/// </para>
/// <para>
/// Своя версия этого была сложнее и звучала хуже: длина сигнала и тишина считались порознь, из-за
/// чего на потолке между сигналами оставался зазор, а сплошной режим приходилось включать
/// отдельно, с запасом на дребезг. Пропорции оригинала проверены годами эксплуатации, наши — нет.
/// </para>
/// </summary>
public static class AlertRhythm
{
    /// <summary>От начала одного сигнала до начала следующего. Постоянный — в этом весь смысл.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromMilliseconds(200);

    /// <summary>Длина сигнала на самом пороге тревоги: короткий писк в редкой сетке.</summary>
    public static readonly TimeSpan ShortestTone = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// Длина сигнала на пороге полной тревоги. Равна периоду — поэтому там и получается сплошной
    /// звук, без единой оговорки в коде.
    /// </summary>
    public static readonly TimeSpan LongestTone = Period;

    public static TimeSpan ToneLength(double intensity)
    {
        double t = Math.Clamp(intensity, 0, 1);
        return ShortestTone + (LongestTone - ShortestTone) * t;
    }

    /// <summary>
    /// Звучит ли сигнал сейчас. <paramref name="sincePeriodStart"/> отсчитывается от начала
    /// текущего периода.
    /// </summary>
    public static bool IsSounding(TimeSpan sincePeriodStart, double intensity) =>
        sincePeriodStart < ToneLength(intensity);

    /// <summary>Пора ли начинать следующий период.</summary>
    public static bool IsPeriodOver(TimeSpan sincePeriodStart) => sincePeriodStart >= Period;
}
