using WheelTalk.Core.Logging;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Logging;

/// <summary>
/// Потолок сырых дампов (план 11 §4.5). Дамп выключен по умолчанию, но включивший его однажды
/// забудет: мегабайт на десять минут, и не ротируется ничто.
/// <para>
/// Здесь проверяется <b>правило</b>, а не файловая система: что сносится и чего не трогают. Само
/// удаление — дело <c>RawFrameRecorder</c>, которому есть каталог; правило живёт в ядре ровно
/// затем, чтобы проверяться без телефона.
/// </para>
/// </summary>
public class RawDumpCapTests
{
    private static readonly DateTimeOffset Day = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private static DumpFile File(string name, long mb, int hoursAgo) =>
        new(name, mb * 1024 * 1024, Day.AddHours(-hoursAgo));

    private const long Cap = 100 * 1024 * 1024;

    /// <summary>Сумма под потолком — не трогаем ничего: уборка не повод удалять.</summary>
    [Fact]
    public void Nothing_goes_while_the_folder_fits()
    {
        DumpFile[] folder = [File("RAW_1", 30, 5), File("RAW_2", 30, 3), File("RAW_3", 30, 1)];

        Assert.Empty(RawDumpCap.Excess(folder, Cap));
    }

    /// <summary>
    /// Перебор — сносится самое старое, и ровно столько, чтобы влезть: уборка не чистит папку до
    /// дна, а возвращает её под потолок.
    /// </summary>
    [Fact]
    public void The_oldest_goes_first_and_only_as_much_as_it_takes()
    {
        DumpFile[] folder =
        [
            File("RAW_старый", 60, 50),
            File("RAW_вчерашний", 50, 20),
            File("RAW_свежий", 40, 1),
        ];

        var doomed = RawDumpCap.Excess(folder, Cap);

        Assert.Equal(["RAW_старый"], doomed.Select(file => file.Path));
    }

    /// <summary>
    /// Файл, в который пишут прямо сейчас, не трогают: удалить его из-под пишущей руки значит
    /// потерять и запись, и то, ради чего человек её включил.
    /// </summary>
    [Fact]
    public void The_file_being_written_is_never_deleted()
    {
        DumpFile[] folder =
        [
            File("RAW_пишем", 90, 30),
            File("RAW_позавчерашний", 40, 40),
            File("RAW_свежий", 30, 1),
        ];

        var doomed = RawDumpCap.Excess(folder, Cap, keep: "RAW_пишем");

        Assert.DoesNotContain(doomed, file => file.Path == "RAW_пишем");
        Assert.Contains(doomed, file => file.Path == "RAW_позавчерашний");
    }

    /// <summary>
    /// Свежий дамп остаётся, даже если он один тяжелее потолка: за ним запись и включали, и
    /// «убраться», отняв только что записанное, — не уборка.
    /// </summary>
    [Fact]
    public void The_newest_dump_survives_even_alone_over_the_cap()
    {
        DumpFile[] folder = [File("RAW_прошлый", 40, 10), File("RAW_свежий", 300, 1)];

        var doomed = RawDumpCap.Excess(folder, Cap);

        Assert.Equal(["RAW_прошлый"], doomed.Select(file => file.Path));
    }

    /// <summary>Ноль — потолка нет: соглашение оригинала «ноль выключает» держится и здесь.</summary>
    [Fact]
    public void A_zero_cap_means_no_cap()
    {
        DumpFile[] folder = [File("RAW_1", 500, 5), File("RAW_2", 500, 1)];

        Assert.Empty(RawDumpCap.Excess(folder, 0));
    }

    /// <summary>
    /// Уборка обязана быть видна в журнале и звана не из кадра: обход каталога на каждом из двадцати
    /// с лишним кадров в секунду стоил бы дороже самой записи (уроки плана 31).
    /// </summary>
    [Fact]
    public void The_recorder_trims_off_the_frame_path_and_says_so_in_the_log()
    {
        string recorder = RepoFiles.Read("WheelTalk.Droid/Logging/RawFrameRecorder.cs");

        Assert.Contains("RawDumpCap.Excess(dumps, cap, keep)", recorder);
        Assert.Contains("Raw.DumpsTrimmed", recorder);
        Assert.Contains("удалено {Removed} дампов, освобождено {FreedMb} МБ", recorder);

        // Зовётся с закрытия файла и с начала нового — оба места вне обработчика кадра.
        Assert.Contains("Trim(_mac, keep: null);", recorder);
        Assert.Contains("Trim(mac, keep: null);", recorder);

        // Потолок — ручкой, и она рядом с выключателем дампа, а не в настройках базы.
        Assert.Contains(
            "public int RawDumpCapMb { get; set; } = 200;",
            RepoFiles.Read("WheelTalk.Droid/Configuration/LoggingOptions.cs"));
    }
}
