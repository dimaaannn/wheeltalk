using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WheelTalk.Core.Alerts;
using WheelTalk.Core.Services;
using WheelTalk.Droid.Alerts;
using WheelTalk.Droid.Configuration;

namespace WheelTalk.Droid.App.Composition;

/// <summary>
/// The one alert stream for the whole app and the channels it fires through — split out of
/// <c>MainApplication.OnCreate</c> (plan 14, А2.2), registrations moved as-is.
/// </summary>
public static class AlertsServiceCollectionExtensions
{
    public static IServiceCollection AddAlerts(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AlertOptions>(configuration.GetSection(AlertOptions.SectionName));

        // One alert stream for the whole app, shared by whoever reacts to it: the signals below and
        // (later) the screen border. Two independent evaluations could disagree about the same moment.
        services.AddSingleton<IObservable<AlertState>>(sp => AlertEvaluator
            .Create(
                sp.GetRequiredService<WheelSession>().Telemetry,
                sp.GetRequiredService<WheelSession>().State,
                sp.GetRequiredService<IOptions<AlertOptions>>().Value,
                DefaultScheduler.Instance)
            .Publish()
            .RefCount());

        services.Configure<AlertSignalOptions>(configuration.GetSection(AlertSignalOptions.SectionName));
        services.AddSingleton<AlertSignals>();

        return services;
    }
}
