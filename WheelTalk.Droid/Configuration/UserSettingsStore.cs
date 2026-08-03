using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Settings;

namespace WheelTalk.Droid.Configuration;

/// <summary>
/// Writes the layer that sits on top of the shipped defaults. Changes are applied twice: to the
/// live options instance, so they take effect immediately, and to the file, so they survive a
/// restart — the configuration itself is never re-read.
/// </summary>
public sealed class UserSettingsStore(
    IOptions<WheelOptions> wheelOptions,
    LayeredSettings settings,
    ILogger<UserSettingsStore> logger)
{
    private static readonly JsonSerializerOptions Formatting = new() { WriteIndented = true };

    /// <summary>
    /// Запоминает колесо, к которому приложение подключается само. Протокола здесь больше нет: он
    /// не выбирается человеком, а опознаётся на каждом подключении — по дереву GATT и по заголовку
    /// первого кадра. Хранить его значило бы держать копию, которая однажды разойдётся с колесом.
    /// </summary>
    public void SaveWheel(string address, string name = "")
    {
        var options = wheelOptions.Value;
        options.Address = address;

        // Which wheel to connect to is what *defines* the scope, so it cannot live inside one —
        // it stays in the user file. Moving it does move the scope, though, and the layer under
        // every other setting has to follow before the next frame is decoded.
        settings.Scope = address;

        var root = Read();
        root[WheelOptions.SectionName] = new JsonObject
        {
            [nameof(WheelOptions.Address)] = address,
        };

        File.WriteAllText(AppConfiguration.UserSettingsPath, root.ToJsonString(Formatting));
        logger.LogInformation("Settings.WheelSaved {Mac}", address);
    }

    /// <summary>
    /// Пишет раздел журналов целиком: оба значения берутся из живого объекта настроек, поэтому
    /// сохранение одного не теряет другое.
    /// </summary>
    public void SaveLogging(LoggingOptions logging)
    {
        var root = Read();
        // Раздел переписывается целиком, поэтому здесь обязано быть **каждое** поле
        // LoggingOptions: забытое затрётся при первом же переключении соседнего тумблера.
        root[LoggingOptions.SectionName] = new JsonObject
        {
            [nameof(LoggingOptions.RawDump)] = logging.RawDump,
            // Строкой, не числом перечисления: конфигурация читает её тем же биндером, что и
            // appsettings.json ("RideOnly" и т. п.), а не порядковым номером, который сдвинется
            // от любой правки enum-а.
            [nameof(LoggingOptions.TelemetryRecording)] = logging.TelemetryRecording.ToString(),
            [nameof(LoggingOptions.AutoStartRide)] = logging.AutoStartRide,
            [nameof(LoggingOptions.AutoStartAboveKmh)] = logging.AutoStartAboveKmh,
        };

        File.WriteAllText(AppConfiguration.UserSettingsPath, root.ToJsonString(Formatting));
        logger.LogInformation("Settings.LoggingSaved {RawDump} {TelemetryRecording} {AutoStartRide} {AutoStartAboveKmh}",
            logging.RawDump, logging.TelemetryRecording, logging.AutoStartRide, logging.AutoStartAboveKmh);
    }

    private JsonObject Read()
    {
        if (!File.Exists(AppConfiguration.UserSettingsPath)) return [];

        try
        {
            return JsonNode.Parse(File.ReadAllText(AppConfiguration.UserSettingsPath)) as JsonObject ?? [];
        }
        catch (JsonException ex)
        {
            // A corrupt user file must not brick the app: the defaults still work, and rewriting
            // it from scratch loses nothing that the user cannot set again.
            logger.LogWarning(ex, "Settings.UserFileUnreadable — rewriting it");
            return [];
        }
    }
}
