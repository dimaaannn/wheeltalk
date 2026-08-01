using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WheelTalk.Core.Alerts;
using WheelTalk.Core.Services;
using WheelTalk.Dashboard.Droid;

namespace WheelTalk.Droid.App.Composition;

/// <summary>
/// The instrument panel's live options and the ride trace it draws — split out of
/// <c>MainApplication.OnCreate</c> (plan 14, А2.2), registrations moved as-is.
/// </summary>
public static class DashboardServiceCollectionExtensions
{
    public static IServiceCollection AddDashboardAndTrace(this IServiceCollection services)
    {
        // Приборная панель. Один экземпляр настроек на приложение: рисующие её виджеты читают поля
        // на каждой отрисовке, поэтому правка настройки видна на экране сразу и оповещать некого.
        // Пороги (Thresholds, план 19 Б3) заданы реализацией поверх Alerts:* один раз здесь, а не
        // зеркалированием на каждом кадре (план 14, Б1.1 — было в MainActivity.FrameTick): экран,
        // плеер и любой будущий потребитель панели читают одно и то же место.
        services.AddSingleton(sp =>
        {
            var alerts = sp.GetRequiredService<IOptions<AlertOptions>>().Value;
            return new DashboardOptions { Thresholds = new LiveThresholds(alerts) };
        });
        services.AddSingleton(sp => new RideTrace(sp.GetRequiredService<TimeProvider>()));

        return services;
    }

    /// <summary>Пороги панели поверх живых Alerts:* — правка страницы «Предупреждения» видна панели сразу.</summary>
    private sealed class LiveThresholds(AlertOptions alerts) : IDashboardThresholds
    {
        public double WarnPwm => alerts.PwmWarning;
        public double DangerPwm => alerts.PwmCritical;
        public double AlertBarCoverage => alerts.MaxBorderCoverage;
    }
}
