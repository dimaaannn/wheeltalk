using WheelTalk.Core.Diagnostics;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Diagnostics;

/// <summary>
/// Полный журнал за сутки — отдельная кнопка рядом с «Отладочной информацией» (решение владельца
/// 15.08.2026).
/// <para>
/// <b>Повод.</b> В комплект диагностики идёт <c>diagnostics.log</c> — склейка получасовых выжимок,
/// и между ними суточные дыры. Разбор заморозки 15.08.2026 упёрся ровно в это: полная лента
/// (<c>wheeltalk.log</c> и её прошлое поколение) лежала на телефоне и всё бы решила, но наружу не
/// выходила. Целиком её слать незачем — четыре мегабайта потолком, — а сутки закрывают любой
/// разбор «что было вчера вечером».
/// </para>
/// <para>
/// Срез считается числами и проверяется числами; экран и кнопка живут в android-проекте и потому
/// стерегутся по исходникам — тем же приёмом, что остальные замки диагностики.
/// </para>
/// </summary>
public class FullLogTests
{
    private const string Bundle = "WheelTalk.Droid/Diagnostics/DiagnosticsBundle.cs";

    private const string Activity = "WheelTalk.Droid/Diagnostics/DiagnosticsShareActivity.cs";

    private static readonly DateTime Now = new(2026, 8, 15, 12, 0, 0);

    private static string Stamped(DateTime when, string text) =>
        $"{when:yyyy-MM-dd HH:mm:ss.fff} {text}";

    /// <summary>Глубина среза — ровно сутки, и это слово владельца, а не догадка о разумном.</summary>
    [Fact]
    public void The_window_is_exactly_a_day()
    {
        Assert.Equal(TimeSpan.FromHours(24), LogWindow.Window);
    }

    /// <summary>
    /// Граница: строка ровно на сутках входит, строка на секунду старше — нет. Проверяется обе
    /// стороны, потому что ошибка на одну секунду тут не видна ничем, кроме такого теста.
    /// </summary>
    [Fact]
    public void The_edge_of_the_window_is_kept_to_the_second()
    {
        string[] lines =
        [
            Stamped(Now.AddHours(-24).AddSeconds(-1), "позавчерашнее"),
            Stamped(Now.AddHours(-24), "ровно сутки"),
            Stamped(Now.AddHours(-1), "час назад"),
            Stamped(Now, "сейчас"),
        ];

        var tail = LogWindow.Tail(lines, Now).ToList();

        Assert.Equal(3, tail.Count);
        Assert.DoesNotContain(tail, line => line.EndsWith("позавчерашнее", StringComparison.Ordinal));
        Assert.EndsWith("ровно сутки", tail[0], StringComparison.Ordinal);
        Assert.EndsWith("сейчас", tail[^1], StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Стек идёт со своей строкой.</b> Продолжения пишутся без метки времени, и оторвать их от
    /// заголовка значило бы положить в срез «!!! упало» без самого исключения — то есть ровно то,
    /// ради чего срез и берут.
    /// </summary>
    [Fact]
    public void A_continuation_goes_where_its_own_line_went()
    {
        string[] lines =
        [
            Stamped(Now.AddDays(-2), "!!! старое падение"),
            "   at Old.Frame()",
            "   at Older.Frame()",
            Stamped(Now.AddMinutes(-5), "!!! свежее падение"),
            "System.NullReferenceException: ссылка пуста",
            "   at Fresh.Frame()",
        ];

        var tail = LogWindow.Tail(lines, Now).ToList();

        Assert.Equal(3, tail.Count);
        Assert.EndsWith("!!! свежее падение", tail[0], StringComparison.Ordinal);
        Assert.Equal("System.NullReferenceException: ссылка пуста", tail[1]);
        Assert.Equal("   at Fresh.Frame()", tail[2]);
    }

    /// <summary>
    /// Продолжения <b>до первой метки</b> не берём: они принадлежат записи, которую унесла ротация,
    /// и отнести их не к чему. Строка из будущего, наоборот, остаётся: это переведённые часы
    /// телефона, а вокруг них — те самые строки, ради которых срез и снимают.
    /// </summary>
    [Fact]
    public void An_orphan_head_is_dropped_and_a_future_stamp_is_kept()
    {
        string[] lines =
        [
            "   at Torn.Frame()",
            "продолжение без начала",
            Stamped(Now.AddMinutes(-1), "живая строка"),
            Stamped(Now.AddHours(3), "часы перевели вперёд"),
        ];

        var tail = LogWindow.Tail(lines, Now).ToList();

        Assert.Equal(2, tail.Count);
        Assert.EndsWith("живая строка", tail[0], StringComparison.Ordinal);
        Assert.EndsWith("часы перевели вперёд", tail[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// Пустая лента даёт пустой срез, а не крах и не выдуманную строку. Дальше пустой состав
    /// отсеет <see cref="DiagnosticsBundlePlan.Compose"/>, и экран честно скажет, что отправлять
    /// нечего.
    /// </summary>
    [Fact]
    public void An_empty_log_gives_an_empty_slice()
    {
        Assert.Empty(LogWindow.Tail([], Now));
        Assert.Empty(LogWindow.Tail(["", "   ", "мусор без метки"], Now));

        Assert.Empty(DiagnosticsBundlePlan.Compose([new DiagnosticsPart("wheeltalk-log-24h.log", "", 0)]));
    }

    /// <summary>Метка разбирается только в начале строки: дата внутри сообщения — не метка.</summary>
    [Fact]
    public void A_date_inside_the_message_is_not_a_stamp()
    {
        Assert.Null(LogWindow.Stamp("I [Ride] поездка от 2026-08-15 01:30:07.123 удалена"));
        Assert.Null(LogWindow.Stamp("короткая"));
        Assert.Equal(new DateTime(2026, 8, 15, 1, 30, 7, 123), LogWindow.Stamp(Stamped(new(2026, 8, 15, 1, 30, 7, 123), "x")));
    }

    /// <summary>
    /// Срез собирается из <b>обоих</b> поколений ленты по порядку: сутки почти всегда пересекают
    /// ротацию, и без предыдущего файла окно обрывалось бы на самом интересном. Читается потоком —
    /// файлы мегабайтные.
    /// </summary>
    [Fact]
    public void The_slice_is_taken_from_both_generations_in_order()
    {
        string bundle = RepoFiles.Read(Bundle);

        Assert.Contains("[FileLog.PreviousPath, FileLog.Path]", bundle);
        Assert.Contains("File.ReadLines(path)", bundle);
        Assert.Contains("LogWindow.Tail(Lines(), DateTime.Now)", bundle);

        // Полная лента остаётся вне комплекта: там по-прежнему выжимка и итоги поездок.
        string prepare = RepoFiles.MethodBody(bundle, "public static IReadOnlyList<DiagnosticsPart> Prepare()");

        Assert.DoesNotContain("FileLog", prepare);
        Assert.DoesNotContain("FullLogFile", prepare);
    }

    /// <summary>
    /// <b>Жмётся до предела</b> (решение владельца: «максимальное сжатие»), и только этот архив: у
    /// комплекта килобайты, и лишняя работа там не окупается.
    /// </summary>
    [Fact]
    public void Only_the_full_log_is_squeezed_to_the_smallest_size()
    {
        string bundle = RepoFiles.Read(Bundle);

        Assert.Contains("Pack(parts, \"wheeltalk-log24h\", CompressionLevel.SmallestSize)", bundle);
        Assert.Contains("Pack(parts, \"wheeltalk-diagnostics\", CompressionLevel.Optimal)", bundle);
    }

    /// <summary>
    /// Кнопка стоит в «Диагностике» рядом с «Отладочной информацией» и ведёт на тот же экран
    /// состава — признаком в намерении, а не второй активностью: вопрос у экрана один и тот же,
    /// «что уйдёт», и вторая копия разошлась бы с первой первой же правкой.
    /// </summary>
    [Fact]
    public void The_button_stands_next_to_the_debug_information_one()
    {
        string page = RepoFiles.Read("WheelTalk.Droid/Settings/Catalogue/AppPage.cs");

        Assert.Contains("Key = \"Diagnostics:FullLog\"", page);
        Assert.Contains("LabelKey = \"SettingFullLog\"", page);
        Assert.Contains("HintKey = \"SettingFullLogHint\"", page);
        Assert.Contains("SectionKey = \"SectionDiagnostics\"", page);
        Assert.Contains("Apply = _ => fullLog(),", page);

        string share = RepoFiles.Read("WheelTalk.Droid/Diagnostics/DiagnosticsShare.cs");

        Assert.Contains("public static void SendFullLog() => Open(fullLog: true);", share);
        Assert.Contains("screen.PutExtra(DiagnosticsShareActivity.ExtraFullLog, true);", share);
    }

    /// <summary>
    /// На экране полного режима видно, <b>сколько будет передано</b>, — вес уже сжатого архива, а не
    /// вес файла на диске (владелец просил именно передаваемый размер). Оттого архив и собирается
    /// при открытии: другого способа узнать его вес, кроме как собрать, нет.
    /// </summary>
    [Fact]
    public void The_screen_shows_the_size_of_the_packed_archive()
    {
        string activity = RepoFiles.Read(Activity);

        Assert.Contains("if (_fullLog && _parts.Count > 0) _archive = DiagnosticsBundle.PackFullLog(_parts);", activity);

        string total = RepoFiles.MethodBody(activity, "private string Total()");

        Assert.Contains("AppStrings.DiagnosticsFullTotal", total);
        Assert.Contains("new FileInfo(_archive) is { Exists: true } file ? file.Length : 0", total);

        // Обычный комплект считает по частям, как и считал: архива до нажатия там нет.
        Assert.Contains("DiagnosticsBundlePlan.TotalBytes(_parts)", total);
    }

    /// <summary>Слова экрана и кнопки — в ресурсах, а не литералами по месту.</summary>
    [Fact]
    public void Every_word_of_the_full_log_lives_in_the_resources()
    {
        string strings = RepoFiles.Read("WheelTalk.Droid/Resources/Strings/AppStrings.resx");

        foreach (string key in
                 (string[])["SettingFullLog", "SettingFullLogHint", "DiagnosticsFullTitle",
                     "DiagnosticsFullPart", "DiagnosticsFullTotal"])
        {
            Assert.Contains($"<data name=\"{key}\"", strings);
        }

        Assert.Contains("DiagnosticsBundle.FullLogFile => AppStrings.DiagnosticsFullPart,", RepoFiles.Read(Activity));
    }
}
