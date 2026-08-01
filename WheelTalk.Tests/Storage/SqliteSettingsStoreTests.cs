using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Settings;
using WheelTalk.Storage;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Storage;

/// <summary>
/// Хранилище настроек ничего не решает — решает <see cref="LayeredSettings"/> в ядре. Здесь
/// проверяется только то, за что отвечает база: значение переживает перезапуск, повторная запись
/// не плодит строк, удаление удаляет, и области видимости не смешиваются.
/// </summary>
public class SqliteSettingsStoreTests
{
    private const string Mac = "88:25:83:F5:75:4A";

    [Fact]
    public void A_value_survives_the_app_being_closed_and_opened_again()
    {
        using var temp = new TempDatabase();
        Store(temp).Write(LayeredSettings.GlobalScope, "PwmWarning", "75");

        // Другое открытие базы — то же, что следующий запуск приложения.
        Assert.Equal("75", Store(temp).Read(LayeredSettings.GlobalScope)["PwmWarning"]);
    }

    [Fact]
    public void Writing_the_same_key_twice_replaces_it_instead_of_piling_up()
    {
        using var temp = new TempDatabase();
        var store = Store(temp);

        store.Write(LayeredSettings.GlobalScope, "PwmWarning", "75");
        store.Write(LayeredSettings.GlobalScope, "PwmWarning", "70");

        Assert.Equal("70", store.Read(LayeredSettings.GlobalScope)["PwmWarning"]);
        Assert.Equal(1, temp.Count("setting"));
    }

    [Fact]
    public void A_null_removes_the_row_rather_than_storing_an_empty_string()
    {
        using var temp = new TempDatabase();
        var store = Store(temp);
        store.Write(Mac, "PwmWarning", "75");

        store.Write(Mac, "PwmWarning", null);

        Assert.Empty(store.Read(Mac));
        Assert.Equal(0, temp.Count("setting"));
    }

    [Fact]
    public void Scopes_do_not_see_each_other()
    {
        using var temp = new TempDatabase();
        var store = Store(temp);

        store.Write(LayeredSettings.GlobalScope, "PwmWarning", "80");
        store.Write(Mac, "PwmWarning", "75");

        Assert.Equal("80", store.Read(LayeredSettings.GlobalScope)["PwmWarning"]);
        Assert.Equal("75", store.Read(Mac)["PwmWarning"]);
    }

    /// <summary>
    /// База от более новой сборки не пишется вовсе — то же правило, что для поездок. Настройка,
    /// молча ушедшая в никуда, хуже настройки, которая не сохранилась заметно.
    /// </summary>
    [Fact]
    public void A_database_from_a_newer_build_is_not_written_to()
    {
        using var temp = new TempDatabase();
        temp.Open();
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={temp.Path_}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 999;";
            command.ExecuteNonQuery();
        }

        Store(temp).Write(LayeredSettings.GlobalScope, "PwmWarning", "75");

        Assert.Equal(0, temp.Count("setting"));
    }

    /// <summary>
    /// Слои и хранилище вместе: то, ради чего всё это заводилось, должно работать и на настоящей
    /// базе, а не только на словаре в памяти.
    /// </summary>
    [Fact]
    public void The_layers_work_the_same_over_a_real_database()
    {
        using var temp = new TempDatabase();
        var factory = new Dictionary<string, string> { ["PwmWarning"] = "80" };
        var settings = new LayeredSettings(Store(temp), factory) { Scope = Mac };

        settings.Set("PwmWarning", "75");
        Assert.Equal(new ResolvedSetting("75", SettingOrigin.Wheel), settings.Get("PwmWarning"));

        settings.PromoteToGlobal("PwmWarning");
        Assert.Equal(new ResolvedSetting("75", SettingOrigin.Global), settings.Get("PwmWarning"));

        settings.ClearOverride("PwmWarning");
        Assert.Equal(new ResolvedSetting("75", SettingOrigin.Global), settings.Get("PwmWarning"));
    }

    private static SqliteSettingsStore Store(TempDatabase temp) =>
        new(temp.Open(), NullLogger<SqliteSettingsStore>.Instance);
}
