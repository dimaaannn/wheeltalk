namespace WheelTalk.Dashboard.Droid;

/// <summary>
/// Пороги панели: где начинается жёлтая зона ШИМ, где красная, и какую долю экрана берёт полоса
/// тревоги в полный голос. Раньше это были три пары «свойство + нульабельный источник» на
/// <see cref="DashboardOptions"/> — с каждым новым порогом пара повторялась бы снова (план 19 Б3,
/// та же болезнь, что план 14 Б1.1 лечил для одного порога). Приложение отдаёт реализацию поверх
/// живого <c>AlertOptions</c>, стенд крутит свою — ручками поверх <see cref="DashboardThresholds"/>.
/// </summary>
public interface IDashboardThresholds
{
    double WarnPwm { get; }
    double DangerPwm { get; }
    double AlertBarCoverage { get; }
}
