using Android.App;
using Android.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Settings;
using WheelTalk.Droid.Alerts;
using WheelTalk.Droid.App.Composition;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Diagnostics;

namespace WheelTalk.Droid.App;

/// <summary>
/// Composition root — нативный аналог <c>MauiProgram.CreateMauiApp()</c>. Строит
/// <see cref="IServiceProvider"/> тем же составом и в том же порядке, что таблица §2.1 описи
/// (docs/native-rewrite-inventory.md), и держит его в статическом свойстве: MAUI прятал это внутри
/// <c>MauiApp</c>, здесь эквивалента нет, а Activity создаёт сам Android — конструктор с
/// параметрами ему не передать (опись §2.2, п.1—2).
/// <para>
/// Блок «Настройки» (<c>SettingsCatalogue</c>, <c>LayeredSettings</c>, <c>SettingsBinder</c>) и
/// <c>DashboardOptions</c> зарегистрированы здесь же, на тех же местах порядка, что в
/// <c>MauiProgram.cs</c> — экран настроек (<c>SettingsActivity</c>) пока пустая заглушка (следующая
/// задача), но панель на главном экране уже читает пороги/палитру из живого <c>DashboardOptions</c>,
/// а не из литералов, и это единственная причина подключать блок настроек уже сейчас.
/// </para>
/// <para>
/// Регистрации разложены по областям в <c>App/Composition/*.cs</c> (план 14, А2.2) — по образцу
/// <c>WheelTalk/Composition/BusinessLogicServiceCollectionExtensions.cs</c> для консоли. Перехват
/// исключений и подписки, которые обязаны пережить любую Activity, — в <see cref="CrashGuard"/>.
/// </para>
/// </summary>
// Имени здесь больше нет: и оно, и описание объявлены в Properties/AndroidManifest.xml ссылками на
// строковые ресурсы. Два источника одного и того же атрибута — верный способ однажды переименовать
// приложение в одном из них.
//
// Тема названа явно: без неё платформа подставляет светлую, а фон страниц приложение красит по
// системному ночному режиму — см. пояснение в Resources/values/styles.xml.
[Application(Theme = "@style/AppTheme")]
public sealed class MainApplication : Application
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public static IServiceProvider Services { get; private set; } = null!;

    public override void OnCreate()
    {
        base.OnCreate();

        // До всего остального: если прошлый запуск не попрощался, забрать хвост буфера надо
        // прежде, чем его вытеснит то, что мы напишем сейчас. При штатной работе — один
        // File.Exists и ничего больше.
        CrashReport.CollectIfPreviousRunCrashed();

        // Тоже до всего остального (план 11 §1.1): ни один из трёх стандартных перехватчиков не
        // был подписан нигде в солюшене, и падение обнаруживалось постфактум по метке — без единой
        // строки о том, что происходило в приложении в момент смерти. Подписка ничего не стоит
        // сама по себе; она просто должна быть готова раньше, чем что-либо успеет упасть.
        CrashGuard.SubscribeGlobalExceptionHandlers();

        var configuration = AppConfiguration.Load();
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new LogcatLoggerProvider());
            logging.AddProvider(new FileLoggerProvider());
            logging.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddTransportAndSession(configuration);
        services.AddAlerts(configuration);
        services.AddDashboardAndTrace();
        services.AddSettingsAndStorage(configuration);
        services.AddRecording(configuration);

        Services = services.BuildServiceProvider();

        // Место SettingsBinder.Apply() из MauiProgram — сразу после сборки контейнера, до первого
        // чтения настройки. До этого вызова опции всё ещё держат только заводские значения из
        // appsettings.json, а это и есть то, что делает их заводским слоем выше.
        Services.GetRequiredService<SettingsBinder>().Apply();

        CrashGuard.SubscribeAppLevelHandlers();

        // Полоса тревоги поверх любого экрана (требование владельца 05.08.2026). Ставится здесь, а
        // не в CrashGuard: RegisterActivityLifecycleCallbacks — метод самого Application, и другого
        // места, где этот экземпляр есть под рукой, в приложении нет.
        RegisterActivityLifecycleCallbacks(Services.GetRequiredService<AlertOverlay>());

        // Полоса поверх ЧУЖИХ приложений (решение владельца 11.08.2026). Не наблюдатель жизненного
        // цикла — просто первое обращение к синглтону, чтобы его подписки на AlertOverlay и
        // AlertBanner встали до первой же тревоги, а не по случайному первому чтению настроек.
        Services.GetRequiredService<SystemAlertOverlay>();

        Services.GetRequiredService<ILogger<MainApplication>>().LogInformation("App.Started");
    }
}
