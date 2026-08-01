namespace WheelTalk.Dashboard.Droid;

/// <summary>
/// Умолчание для <see cref="DashboardOptions.Thresholds"/>: числа WheelLog (78/92) и стендовое
/// 0,096 (dashboard-feedback.md). Мутабельный — стенд крутит эти же поля своими ручками напрямую,
/// приложение подставляет вместо него реализацию поверх живого <c>AlertOptions</c>.
/// </summary>
public sealed class DashboardThresholds : IDashboardThresholds
{
    public double WarnPwm { get; set; } = 78;
    public double DangerPwm { get; set; } = 92;
    public double AlertBarCoverage { get; set; } = 0.096;
}
