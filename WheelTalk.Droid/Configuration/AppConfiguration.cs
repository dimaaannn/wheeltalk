using Microsoft.Extensions.Configuration;

namespace WheelTalk.Droid.Configuration;

/// <summary>
/// Two-layer configuration: defaults shipped inside the app package, user changes written to app
/// data on top. Splitting them keeps the user file short — it holds only what was actually
/// changed — and lets a bad user file be deleted without taking the defaults with it.
/// </summary>
public static class AppConfiguration
{
    public const string UserSettingsFileName = "usersettings.json";

    public static string UserSettingsPath =>
        Path.Combine(Android.App.Application.Context.FilesDir!.AbsolutePath, UserSettingsFileName);

    public static IConfiguration Load()
    {
        using var defaults = Android.App.Application.Context.Assets!.Open("appsettings.json");

        var configuration = new ConfigurationBuilder()
            .AddJsonStream(defaults)
            .AddJsonFile(UserSettingsPath, optional: true)
            .Build();

        DropRetiredTelemetryMode(configuration);

        return configuration;
    }

    /// <summary>
    /// Снятое положение переключателя записи — «только в поездке» (решение владельца 05.08.2026
    /// отменило его вместе с привязкой потока к поездке). У того, кто выбрал его до обновления, оно
    /// лежит в файле строкой, которой в перечислении больше нет: биндер на такой падает, а падать
    /// приложению из-за старой настройки нельзя.
    /// <para>
    /// Заменяется на «пишем», а не на «не пишем»: человек соглашался на запись, и молча отнять её у
    /// него — худшее из двух прочтений.
    /// </para>
    /// </summary>
    private static void DropRetiredTelemetryMode(IConfiguration configuration)
    {
        const string key = $"{LoggingOptions.SectionName}:{nameof(LoggingOptions.TelemetryRecording)}";

        if (!string.Equals(configuration[key], "RideOnly", StringComparison.OrdinalIgnoreCase)) return;

        configuration[key] = nameof(TelemetryRecording.Always);
    }
}
