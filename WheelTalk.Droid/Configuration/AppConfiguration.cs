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

        return new ConfigurationBuilder()
            .AddJsonStream(defaults)
            .AddJsonFile(UserSettingsPath, optional: true)
            .Build();
    }
}
