using WheelTalk.Core.Contracts;
using WheelTalk.Storage;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Storage;

/// <summary>
/// Срок хранения <b>поездок</b> (план 11 §4.5) — в отличие от суточного срока потока
/// (<see cref="TelemetryRetentionTests"/>), он выключен по умолчанию.
/// <para>
/// <b>Умолчание здесь и есть решение.</b> На поездках стоит план 9: прогнозы считаются по
/// накопленному, а сама поездка весит десятки байт. Заводское значение, которое молча стирает
/// историю, — то, чего не прощают; срок ставит человек и знает, что делает.
/// </para>
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class RideRetentionTests
{
    private const string Mac = "88:25:83:F5:75:4A";

    private static readonly DateTimeOffset Start =
        new(2026, 6, 1, 12, 0, 0, TimeSpan.FromHours(3));

    /// <summary>Заводское — «не удалять»: ноль, и ни одна поездка не уходит, сколько бы ни ждали.</summary>
    [Fact]
    public async Task By_default_no_ride_is_ever_deleted()
    {
        Assert.Equal(TimeSpan.Zero, new StorageOptions().RideRetention);

        using var temp = new TempDatabase();
        var database = temp.Open();
        await Ride(temp, database, Start);

        database.PurgeOldRides(new StorageOptions().RideRetention, Start.AddYears(1));

        Assert.Equal(1, temp.Count("ride"));
    }

    /// <summary>Срок поставлен — старое уходит, молодое остаётся. Ровно то, о чём человека спросили.</summary>
    [Fact]
    public async Task With_a_term_the_old_rides_go_and_the_young_ones_stay()
    {
        using var temp = new TempDatabase();
        var database = temp.Open();

        await Ride(temp, database, Start);
        await Ride(temp, database, Start.AddDays(40));

        database.PurgeOldRides(TimeSpan.FromDays(30), Start.AddDays(45));

        Assert.Equal(1, temp.Count("ride"));
        Assert.Equal(1, temp.Count("ride", $"started_at >= {Start.AddDays(40).ToUnixTimeMilliseconds()}"));
    }

    /// <summary>
    /// Незакрытая поездка не трогается никогда: у неё нет конца, от которого считать срок, — а
    /// брошенные к этому моменту уже закрыты открытием базы. Значит «открытая» здесь значит «идёт
    /// прямо сейчас», и удалить её было бы удалением того, что пишется.
    /// </summary>
    [Fact]
    public async Task A_ride_still_open_is_left_alone()
    {
        using var temp = new TempDatabase();
        var database = temp.Open();
        await Ride(temp, database, Start);

        temp.Execute("UPDATE ride SET ended_at = NULL;");
        database.PurgeOldRides(TimeSpan.FromDays(1), Start.AddYears(1));

        Assert.Equal(1, temp.Count("ride"));
    }

    /// <summary>
    /// Чистка зовётся на старте приложения, <b>после</b> того как слои настроек легли на опции: сам
    /// срок лежит настройкой в этой же базе, и на её открытии читать его ещё неоткуда — чистка там
    /// работала бы по заводскому нулю всегда.
    /// </summary>
    [Fact]
    public void The_purge_runs_after_the_settings_are_applied()
    {
        string startup = RepoFiles.Read("WheelTalk.Droid/App/MainApplication.cs");

        int applied = startup.IndexOf("SettingsBinder>().Apply();", StringComparison.Ordinal);
        int purged = startup.IndexOf("PurgeOldRides(", StringComparison.Ordinal);

        Assert.True(applied >= 0 && purged > applied,
            "чистка поездок стоит раньше настроек — она увидит заводской ноль, а не выбранный срок");
    }

    /// <summary>
    /// Настройка общая, а не по колесу: база одна на все колёса, и «удалять старше месяца» не может
    /// значить разное в зависимости от того, к какому колесу сейчас подключены.
    /// </summary>
    [Fact]
    public void The_term_is_a_global_setting()
    {
        string page = RepoFiles.Read("WheelTalk.Droid/Settings/Catalogue/AppPage.cs");
        int key = page.IndexOf("Key = \"Storage:RideRetention\"", StringComparison.Ordinal);

        Assert.True(key >= 0, "настройки срока нет в каталоге");

        string entry = page[key..page.IndexOf("},", key, StringComparison.Ordinal)];

        Assert.Contains("GlobalOnly = true", entry);
        Assert.Contains("SectionKey = \"SectionStorage\"", entry);
        Assert.Contains("UnitKey = \"UnitDays\"", entry);

        // Подсказка не прячет необратимость: удалённую поездку не вернуть.
        string words = RepoFiles.Read("WheelTalk.Droid/Resources/Strings/AppStrings.resx");

        Assert.Contains("SettingRideRetentionHint", words);
        Assert.Contains("не вернуть", words);
    }

    private static async Task Ride(TempDatabase temp, RideDatabase database, DateTimeOffset at)
    {
        await using var store = temp.Store(database);

        store.BeginRide();
        for (int i = 0; i < 5; i++) store.Write(Mac, "Veteran", Sample(), at.AddSeconds(i));
        await store.CloseRideAsync();
    }

    private static TelemetrySnapshot Sample() => new()
    {
        SpeedRaw = 3600,
        VoltageRaw = 15012,
        TotalDistance = 987_654,
        TemperatureRaw = 3400,
        WheelType = WheelType.Veteran,
    };
}
