namespace WheelTalk.Core.Alerts;

/// <summary>
/// Thresholds the alert engine works to. Values are per wheel — what counts as alarming depends on
/// the machine — so they live in the wheel's section of the user settings.
/// </summary>
public sealed class AlertOptions
{
    public const string SectionName = "Alerts";

    /// <summary>
    /// How long a reading keeps the alert up after it stops being exceeded. Also the width of the
    /// window the peak is taken over, which is what makes a single spike between frames count.
    /// </summary>
    public TimeSpan Hold { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How often the window is re-evaluated. Finer means a quicker release, not a quicker trigger.</summary>
    public TimeSpan Step { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Сколько телеметрия может отсутствовать, прежде чем тревога отпускается. Нужно потому, что
    /// окно уже́ промежутка между отсчётами: колесо шлёт снэпшот раз в 200 мс, окно — 500 мс, и
    /// один потерянный пакет опустошает его целиком. Пустое окно, понятое как «тихо», глушило
    /// тревогу на предельной скважности — ровно там, где она нужнее всего.
    /// </summary>
    public TimeSpan Silence { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>PWM where the warning starts, percent — the original's <c>alarm_factor1</c>.</summary>
    public double PwmWarning { get; set; } = 80;

    /// <summary>PWM of maximum alarm, percent — the original's <c>alarm_factor2</c>.</summary>
    public double PwmCritical { get; set; } = 90;

    /// <summary>
    /// Speed that raises the soft alert, km/h. Zero switches it off, as it does in the original
    /// (<c>warning_speed</c>, and off is what it ships with) — a rider who wants a speed warning
    /// says at what speed.
    /// </summary>
    public double SpeedThreshold { get; set; }

    /// <summary>
    /// How often the soft speed signal repeats while the speed alert holds. Five seconds is what
    /// the original ends up using: it stores <c>warning_speed_period</c> as zero, which its own
    /// check reads as "off", and the settings screen replaces the zero with five the first time it
    /// is opened. Off lives on <see cref="SpeedThreshold"/> here, so this is only a period.
    /// </summary>
    public TimeSpan SpeedRepeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Silences the speed alert while PWM is alarming. Two signals at once, at the moment the wheel
    /// is closest to its limit, help nobody.
    /// </summary>
    public bool SuppressSpeedWhilePwmAlert { get; set; } = true;

    /// <summary>
    /// How much of the screen the alert border may eat at full intensity, as a fraction of the
    /// shorter side. Caps how far it can grow inward so the readings never end up underneath it.
    /// </summary>
    public double MaxBorderCoverage { get; set; } = 0.096;
}
