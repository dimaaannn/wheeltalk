using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WheelTalk.Core.Alerts;
using WheelTalk.Core.Services;
using WheelTalk.Core.Settings;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Diagnostics;
using WheelTalk.Droid.Logging;
using WheelTalk.Droid.Settings.Catalogue;
using WheelTalk.Storage;

namespace WheelTalk.Droid.App.Composition;

/// <summary>
/// Settings in three layers (store, factory defaults, binder) and the ride storage they sit next
/// to — split out of <c>MainApplication.OnCreate</c> (plan 14, А2.2), registrations moved as-is.
/// </summary>
public static class SettingsServiceCollectionExtensions
{
    public static IServiceCollection AddSettingsAndStorage(this IServiceCollection services, IConfiguration configuration)
    {
        // Settings, in three layers. The order here is the whole of it: the descriptors are built
        // over the freshly bound options objects, so reading them right now gives the factory
        // layer — the packaged defaults, already parsed once, without parsing the file again.
        // Как зовут колесо: алиас приходит слоем этого колеса, имя анонса спрашивается у адаптера.
        services.AddSingleton<WheelIdentity>();
        services.Configure<ScreenOptions>(configuration.GetSection(ScreenOptions.SectionName));
        services.Configure<PowerOptions>(configuration.GetSection(PowerOptions.SectionName));

        services.AddSingleton<ISettingsStore>(sp => new SqliteSettingsStore(
            sp.GetRequiredService<RideDatabase>(), sp.GetRequiredService<ILogger<SqliteSettingsStore>>()));
        services.AddSingleton(sp => SettingsCatalogue.Build(new CatalogueContext(
            sp.GetRequiredService<IOptions<AppWheelConfig>>().Value,
            sp.GetRequiredService<IOptions<AlertOptions>>().Value,
            sp.GetRequiredService<IOptions<AlertSignalOptions>>().Value,
            sp.GetRequiredService<IOptions<ConnectionOptions>>().Value,
            sp.GetRequiredService<IOptions<WheelOptions>>().Value,
            sp.GetRequiredService<DashboardOptions>(),
            DiagnosticsShare.Send,
            sp.GetRequiredService<WheelIdentity>(),
            sp.GetRequiredService<IOptions<ScreenOptions>>().Value,
            sp.GetRequiredService<IOptions<PowerOptions>>().Value,
            // Лениво и через делегат: сессия строится позже описаний, а спрашивают её уже во время
            // отрисовки страницы, когда колесо назвалось.
            () => sp.GetRequiredService<WheelSession>().Protocol)));
        services.AddSingleton(sp => new LayeredSettings(
            sp.GetRequiredService<ISettingsStore>(),
            SettingsBinder.FactoryDefaults(sp.GetRequiredService<IReadOnlyList<SettingDescriptor>>()))
        {
            // The wheel we are set to talk to owns the top layer from the first frame drawn, not
            // from the first connection: its settings have to be right before it answers.
            Scope = sp.GetRequiredService<IOptions<WheelOptions>>().Value.Address,
        });
        services.AddSingleton(sp => new SettingsBinder(
            sp.GetRequiredService<LayeredSettings>(),
            sp.GetRequiredService<IReadOnlyList<SettingDescriptor>>()));

        // Recording belongs to the session, not to a screen: it has to carry on with the screen off.
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        // Next to the ride files rather than in the app's private directory: the internal one is
        // reachable only through `run-as`, and pulling a database off the phone would stop working.
        services.AddSingleton(sp => RideDatabase.Open(
            Path.Combine(RideFiles.Root, "rides.db"),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<RideDatabase>>()));
        services.AddSingleton(sp => new RideStore(
            sp.GetRequiredService<RideDatabase>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOptions<StorageOptions>>().Value,
            sp.GetRequiredService<ILogger<RideStore>>()));
        services.AddSingleton<RideExporter>();

        return services;
    }
}
