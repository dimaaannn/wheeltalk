using System.Text.RegularExpressions;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Ui;

/// <summary>
/// Замок постоянства шторки (план 25 §2, шаг 5; раскладка по разделам — план 32 §1, этап 4).
/// Позиции команд объявлены отправной точкой правила «позиции фиксированы навсегда»
/// (quick-commands-design.md §3): рука запоминает <b>место</b>, а не слово, и перестановка кнопок
/// обесценивает эту память у всех, кто уже привык. Значит перестановка должна ронять сборку, а не
/// всплывать жалобой через месяц.
/// <para>
/// Читается по исходникам: состав шторки собирает <c>MainActivity</c>, android-проект тестам не
/// виден, а держать раскладку нужно именно боевую. Правило простое — то, что здесь написано, и
/// есть уговор; менять его можно, но только вместе с этим тестом и осознанно.
/// </para>
/// <para>
/// Значок с плана 32 — не буква эмодзи, а имя из <c>QuickIcons</c>: уникальность держится по этим
/// именам, и она же проверяется до самого файла контура — переименованный drawable иначе всплыл бы
/// пустой кнопкой на телефоне.
/// </para>
/// </summary>
public class QuickSheetLayoutTests
{
    private const string MainActivity = "WheelTalk.Droid/Main/MainActivity.cs";

    private const string QuickIcons = "WheelTalk.Dashboard.Droid/Screen/QuickIcons.cs";

    private const string Drawables = "WheelTalk.Dashboard.Droid/Resources/drawable";

    /// <summary>
    /// Оперативные команды и восьмая, отладочная. Раздел задаёт строку шторки: колесо · поездка ·
    /// телефон (план 32 §1, этап 4), и реплей стоит <b>внутри</b> поездки, а не в конце списка —
    /// иначе под телефонными командами завелась бы вторая строка «Поездка».
    /// </summary>
    private static readonly (string Icon, string Group)[] Commands =
    [
        ("Light", "WheelNow"),
        ("Beep", "WheelNow"),
        ("Power", "WheelNow"),

        ("Record", "Ride"),
        ("Reset", "Ride"),

        // Реплей — только у отладочного транспорта, на колесе его не бывает.
        ("Play", "Ride"),

        ("Sun", "Phone"),
        ("Lock", "Phone"),
    ];

    /// <summary>Переходы — свой раздел, свой порядок (план 32 §1, этап 4).</summary>
    private static readonly string[] Links = ["Data", "Rides", "Settings"];

    [Fact]
    public void The_operative_commands_keep_their_order_and_their_sections()
    {
        var actual = IconsWithGroups(RepoFiles.MethodBody(
            RepoFiles.Read(MainActivity), "private IReadOnlyList<QuickSheetCommand> BuildWheelCommands()"));

        Assert.Equal(Commands, actual);
    }

    /// <summary>
    /// Раздел — это идущие подряд соседи: шторка ставит корешок там, где ключ сменился, и команда,
    /// приписанная в конец списка со старым ключом, молча заведёт вторую строку того же раздела.
    /// </summary>
    [Fact]
    public void No_section_of_the_sheet_comes_twice()
    {
        var sections = IconsWithGroups(RepoFiles.MethodBody(
                RepoFiles.Read(MainActivity), "private IReadOnlyList<QuickSheetCommand> BuildWheelCommands()"))
            .Select(entry => entry.Group)
            .ToList();

        var runs = sections.Where((group, index) => index == 0 || sections[index - 1] != group).ToList();

        Assert.Equal(runs.Distinct(), runs);
    }

    /// <summary>
    /// Переходы ушли из ряда команд в свой раздел и обязаны там остаться: оперативная команда — та,
    /// после которой человек продолжает ехать, и это половина лечения жалобы «какая за что
    /// непонятно».
    /// </summary>
    [Fact]
    public void The_transitions_live_in_their_own_section_and_keep_their_order()
    {
        var actual = IconsWithGroups(RepoFiles.MethodBody(
                RepoFiles.Read(MainActivity), "private IReadOnlyList<QuickSheetLink> BuildScreenLinks()"))
            .Select(entry => entry.Icon)
            .ToArray();

        Assert.Equal(Links, actual);
    }

    /// <summary>
    /// Ни один значок в шторке не повторяется — с этого начался весь план: «📊» стоял разом на
    /// команде «Данные» и на корешке «Панель», и один знак на два разных дела и есть готовое
    /// объяснение жалобы. Считаются все четыре источника: команды, переходы, корешки экранов и
    /// булавка, которую шторка приписывает сама.
    /// </summary>
    [Fact]
    public void No_two_things_in_the_sheet_wear_the_same_icon()
    {
        var icons = SheetIcons();

        var doubled = icons.GroupBy(icon => icon).Where(group => group.Count() > 1).Select(group => group.Key).ToList();

        Assert.True(doubled.Count == 0, "Один знак на два дела в шторке: " + string.Join(", ", doubled));
        Assert.True(icons.Count >= 14, $"Знаков нашлось всего {icons.Count} — разбор исходника промахнулся мимо списков.");
    }

    /// <summary>
    /// У каждого имени из шторки есть свой контур, и два имени на один файл — та же двойня, только
    /// незаметная по коду. Проверяется до файла: переименованный drawable компиляцию не роняет
    /// (имя резолвится через <c>Resource.Drawable</c> уже собранного проекта), а всплывает пустой
    /// кнопкой на телефоне.
    /// </summary>
    [Fact]
    public void Every_icon_of_the_sheet_has_its_own_drawable()
    {
        var contours = Contours();

        foreach (string icon in SheetIcons().Distinct())
        {
            Assert.True(contours.ContainsKey(icon), $"В QuickIcons нет значка «{icon}».");

            string file = Path.Combine(RepoFiles.Root, Drawables.Replace('/', Path.DirectorySeparatorChar), contours[icon] + ".xml");
            Assert.True(File.Exists(file), $"У значка «{icon}» нет контура: {contours[icon]}.xml");
        }

        var shared = contours.GroupBy(entry => entry.Value).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        Assert.True(shared.Count == 0, "Один контур на два имени: " + string.Join(", ", shared));
    }

    /// <summary>Все значки шторки: команды, переходы, корешки экранов и булавка.</summary>
    private static List<string> SheetIcons()
    {
        string main = RepoFiles.Read(MainActivity);

        var icons = new List<string>();
        icons.AddRange(IconsWithGroups(RepoFiles.MethodBody(
            main, "private IReadOnlyList<QuickSheetCommand> BuildWheelCommands()")).Select(entry => entry.Icon));
        icons.AddRange(IconsWithGroups(RepoFiles.MethodBody(
            main, "private IReadOnlyList<QuickSheetLink> BuildScreenLinks()")).Select(entry => entry.Icon));
        // Корешки экранов — из реестра (план 17 §3): значок стоит вторым в записи, следом за id.
        icons.AddRange(Matched(
            RepoFiles.Read("WheelTalk.Droid/Main/MainScreenRegistry.cs"),
            @"new\(\w+Id, QuickIcons\.(?<icon>\w+)"));

        // Булавку шторка приписывает сама, и знак у неё свой — в общий счёт он входит наравне.
        icons.AddRange(Matched(
            RepoFiles.MethodBody(
                RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/QuickSheet.cs"),
                "private QuickSheetCommand PinCommand()"),
            @"Icon = QuickIcons\.(?<icon>\w+)"));

        return icons;
    }

    /// <summary>Имя значка → имя его vector drawable, по <c>QuickIcons</c>.</summary>
    private static Dictionary<string, string> Contours() =>
        Regex.Matches(RepoFiles.Read(QuickIcons), @"int (?<name>\w+) => Resource\.Drawable\.(?<file>\w+);")
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["file"].Value);

    /// <summary>Значок и раздел — парой и по порядку: в описании они стоят рядом, и разлучить их значит потерять смысл обоих.</summary>
    private static (string Icon, string Group)[] IconsWithGroups(string body) =>
        [.. Regex.Matches(body, @"Icon = QuickIcons\.(?<icon>\w+),(?:\s*Group = (?<group>\w+),)?")
            .Select(match => (match.Groups["icon"].Value, match.Groups["group"].Value))];

    private static IEnumerable<string> Matched(string body, string pattern) =>
        Regex.Matches(body, pattern).Select(match => match.Groups["icon"].Value);
}
