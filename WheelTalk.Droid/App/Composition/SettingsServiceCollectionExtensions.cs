using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WheelTalk.Core.Alerts;
using WheelTalk.Core.Metrics;
using WheelTalk.Core.Services;
using WheelTalk.Core.Settings;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Droid.Alerts;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Diagnostics;
using WheelTalk.Droid.Logging;
using WheelTalk.Droid.Main;
using WheelTalk.Droid.Scan;
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
        services.Configure<DiagnosticsOptions>(configuration.GetSection(DiagnosticsOptions.SectionName));

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
            DiagnosticsShare.SendFullLog,
            sp.GetRequiredService<WheelIdentity>(),
            sp.GetRequiredService<IOptions<ScreenOptions>>().Value,
            sp.GetRequiredService<IOptions<PowerOptions>>().Value,
            sp.GetRequiredService<IOptions<StorageOptions>>().Value,
            sp.GetRequiredService<IOptions<DiagnosticsOptions>>().Value,
            // Лениво и через делегат: сессия строится позже описаний, а спрашивают её уже во время
            // отрисовки страницы, когда колесо назвалось.
            () => sp.GetRequiredService<WheelSession>().Protocol,
            // Тем же ленивым способом и по той же причине: пароль правят и с главного экрана, и со
            // страницы настроек, а разговор с колесом после правки обязан начаться заново в обоих
            // случаях. До первого подключения зов безвреден — сессии нечего перезапускать.
            () => sp.GetRequiredService<WheelSession>().RestartAuthentication(),
            // Тоже лениво: звук нужен только тому, кто открыл «Предупреждения», а описания строятся
            // на запуске — поднимать ради них звуковой поток незачем.
            intensity => sp.GetRequiredService<AlertSignals>().Preview(intensity),
            // Последний кадр — по нему считает кнопка ряда и предупреждает строка ряда (план 27
            // §27.4). Лениво и здесь: до подключения сессии ещё нет, а спрашивают её с открытой
            // страницы настроек.
            () => sp.GetRequiredService<WheelSession>().LastSnapshot,
            // Запись — через биндер: слой у настройки колеса свой, и знать о нём кнопке незачем.
            // Область тоже: биндер пишет по боевой, а не по той, что открыта на странице.
            cells => sp.GetRequiredService<SettingsBinder>()
                .Set(ExperimentalPage.CellsKey, cells.ToString(CultureInfo.InvariantCulture)),
            sp.GetRequiredService<PanelVariants>())));
        services.AddSingleton(sp => new LayeredSettings(
            sp.GetRequiredService<ISettingsStore>(),
            SettingsBinder.FactoryDefaults(sp.GetRequiredService<IReadOnlyList<SettingDescriptor>>()))
        {
            // The wheel we are set to talk to owns the top layer from the first frame drawn, not
            // from the first connection: its settings have to be right before it answers.
            // Второе и последнее место, где боевая область назначается: дальше её меняет только
            // выбор колеса (UserSettingsStore.SaveWheel) — план 29 §29.3.
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
            sp.GetRequiredService<ILogger<RideDatabase>>(),
            // Открытие базы — это и закрытие брошенных поездок, и досчёт итогов, и чистка потока по
            // сроку. Сроки живут тут же, рядом с интервалом записи (план 23 §5.1, §5.4).
            sp.GetRequiredService<IOptions<StorageOptions>>().Value));
        services.AddSingleton(sp => new RideStore(
            sp.GetRequiredService<RideDatabase>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IOptions<StorageOptions>>().Value,
            sp.GetRequiredService<ILogger<RideStore>>()));
        services.AddSingleton<RideExporter>();

        // Колёса, к которым подключались (план 24 §А). Отметку ставит подписка на состояние сессии
        // в CrashGuard, читает её экран поиска — через BoundWheels, который добавляет к истории
        // подключений слой настроек колеса.
        services.AddSingleton<KnownWheels>();
        services.AddSingleton<BoundWheels>();

        // История для плиток-графиков (план 23 §5.6). Колесо делегатом, а не строкой: адрес меняется
        // в живом приложении, а читатель один.
        //
        // Именно IOptions, а не IOptionsMonitor: живой экземпляр WheelOptions один — тот, чей адрес
        // правит UserSettingsStore.SaveWheel, — и это экземпляр IOptions.Value. У монитора СВОЙ
        // кэш и свой экземпляр, правок в первом он не видит, и через CurrentValue сюда приезжал
        // адрес колеса, с которым приложение запустилось: после смены колеса в поиске все графики
        // плиток продолжали показывать прежнее колесо (баг владельца 09.08.2026 — «Наклон» на
        // Gotway, которого у Gotway не бывает).
        services.AddSingleton<IMetricHistory>(sp => new MetricHistoryReader(
            sp.GetRequiredService<RideDatabase>(),
            () => sp.GetRequiredService<IOptions<WheelOptions>>().Value.Address));

        return services;
    }
}
