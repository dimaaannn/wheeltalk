using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WheelTalk.Core.Diagnostics;
using WheelTalk.Core.Playback;
using WheelTalk.Core.Detection;
using WheelTalk.Core.Ports;
using WheelTalk.Core.Services;
using WheelTalk.Droid.Ble;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Diagnostics;
using WheelTalk.Droid.Logging;

namespace WheelTalk.Droid.App.Composition;

/// <summary>
/// Wheel config, the BLE/replay transport, and the one <see cref="WheelSession"/> shared by every
/// screen — split out of <c>MainApplication.OnCreate</c> (plan 14, А2.2), registrations moved as-is.
/// </summary>
public static class TransportServiceCollectionExtensions
{
    public static IServiceCollection AddTransportAndSession(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AppWheelConfig>(configuration.GetSection("WheelConfig"));
        services.Configure<WheelOptions>(configuration.GetSection(WheelOptions.SectionName));
        services.AddSingleton<UserSettingsStore>();

        // Decoders write reported settings back into this instance, so every consumer must see the
        // same object — hence the options instance itself rather than a fresh binding per resolve.
        services.TryAddSingleton<IWheelConfig>(sp => sp.GetRequiredService<IOptions<AppWheelConfig>>().Value);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IEventSink, LoggingEventSink>();

        services.Configure<ReplayOptions>(configuration.GetSection(ReplayOptions.SectionName));
        services.AddSingleton<AndroidBleClient>();
        services.AddSingleton<ITransport>(sp =>
        {
            string dump = sp.GetRequiredService<IOptions<ReplayOptions>>().Value.DumpFile;
            if (string.IsNullOrWhiteSpace(dump))
            {
                return sp.GetRequiredService<AndroidBleClient>();
            }

            return new ReplayTransport(
                () => new StreamReader(RideFiles.Resolve(dump)),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<ReplayTransport>>(),
                sp.GetRequiredService<IOptions<ReplayOptions>>().Value.Speed);
        });

        services.Configure<ConnectionOptions>(configuration.GetSection(ConnectionOptions.SectionName));

        // Кто перед нами — по дереву GATT, а не по выбору человека. Сервис без состояния, но в
        // контейнере: он сверяется с таблицей отпечатков и пишет в журнал, а такое место в
        // приложении должно быть одно.
        services.AddSingleton<WheelDetector>();

        // One session for the whole app: activities come and go, the wheel connection does not.
        services.AddSingleton(sp => new WheelSession(
            sp.GetRequiredService<ITransport>(),
            sp.GetRequiredService<IWheelConfig>(),
            sp.GetRequiredService<IEventSink>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOptions<ConnectionOptions>>().Value,
            sp.GetRequiredService<WheelDetector>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<IOptions<ReplayOptions>>().Value.Protocol));

        // Сердцебиение фона — рядом с сессией, потому что живёт ровно её незаконченной работой.
        // Поднимается в CrashGuard, до первого подключения: свой файл он обязан прочитать раньше,
        // чем сам начнёт его писать. Кадры телеметрии ему нужны наравне с фазой: перерыв кончается
        // не тиком его собственного таймера, а первым кадром с колеса.
        services.AddSingleton(sp => new BackgroundWatch(
            Path.Combine(RideFiles.Root, "background.beat"),
            sp.GetRequiredService<WheelSession>().State,
            sp.GetRequiredService<WheelSession>().Telemetry,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<BackgroundWatch>>()));

        return services;
    }
}
