using System.Text.RegularExpressions;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Ui;

/// <summary>
/// Замок постоянства шторки (план 25 §2, шаг 5). Раскладка, принятая 09.08.2026, объявлена
/// отправной точкой правила «позиции фиксированы навсегда» (quick-commands-design.md §3): рука
/// запоминает <b>место</b>, а не слово, и перестановка кнопок обесценивает эту память у всех, кто
/// уже привык. Значит перестановка должна ронять сборку, а не всплывать жалобой через месяц.
/// <para>
/// Читается по исходникам: состав шторки собирает <c>MainActivity</c>, android-проект тестам не
/// виден, а держать раскладку нужно именно боевую. Правило простое — то, что здесь написано, и
/// есть уговор; менять его можно, но только вместе с этим тестом и осознанно.
/// </para>
/// </summary>
public class QuickSheetLayoutTests
{
    private const string MainActivity = "WheelTalk.Droid/Main/MainActivity.cs";

    /// <summary>
    /// Семь оперативных и восьмая, отладочная. Порода задаёт стайки: колесо-сейчас · запись ·
    /// связь · телефон (план 25 §2, шаг 3) — разделители шторка ставит по смене стайки.
    /// </summary>
    private static readonly (string Icon, string Group)[] Commands =
    [
        ("💡", "WheelNow"),
        ("📢", "WheelNow"),
        ("🔴", "Recording"),
        ("🔄", "Recording"),
        ("🔌", "Link"),
        ("☀", "Phone"),
        ("🔒", "Phone"),

        // Реплей — только у отладочного транспорта, на колесе его не бывает; в ряду он последний и
        // своей стайкой.
        ("▶", "Replay"),
    ];

    /// <summary>Переходы — своя полоса, свой порядок (план 25 §2, шаг 2).</summary>
    private static readonly string[] Links = ["📈", "📁", "⚙"];

    [Fact]
    public void The_row_of_operative_commands_keeps_its_order_and_its_groups()
    {
        var actual = IconsWithGroups(RepoFiles.MethodBody(
            RepoFiles.Read(MainActivity), "private IReadOnlyList<QuickSheetCommand> BuildWheelCommands()"));

        Assert.Equal(Commands, actual);
    }

    /// <summary>
    /// Переходы ушли из ряда команд в полосу корешков и обязаны там остаться: ряд оперативных —
    /// семь, и это половина лечения жалобы «какая за что непонятно».
    /// </summary>
    [Fact]
    public void The_transitions_live_in_their_own_strip_and_keep_their_order()
    {
        var actual = IconsWithGroups(RepoFiles.MethodBody(
                RepoFiles.Read(MainActivity), "private IReadOnlyList<QuickSheetLink> BuildScreenLinks()"))
            .Select(entry => entry.Icon)
            .ToArray();

        Assert.Equal(Links, actual);
    }

    /// <summary>
    /// Ни один знак в шторке не повторяется — с этого начался весь план: «📊» стоял разом на
    /// команде «Данные» и на корешке «Панель», и один знак на два разных дела и есть готовое
    /// объяснение жалобы. Считаются все четыре источника: команды, переходы, корешки экранов и
    /// булавка, которую шторка приписывает сама.
    /// </summary>
    [Fact]
    public void No_two_things_in_the_sheet_wear_the_same_glyph()
    {
        string main = RepoFiles.Read(MainActivity);

        var icons = new List<string>();
        icons.AddRange(IconsWithGroups(RepoFiles.MethodBody(
            main, "private IReadOnlyList<QuickSheetCommand> BuildWheelCommands()")).Select(e => e.Icon));
        icons.AddRange(IconsWithGroups(RepoFiles.MethodBody(
            main, "private IReadOnlyList<QuickSheetLink> BuildScreenLinks()")).Select(e => e.Icon));
        // Корешки экранов — из реестра (план 17 §3): значок стоит вторым в записи, следом за id.
        icons.AddRange(Matched(
            RepoFiles.Read("WheelTalk.Droid/Main/MainScreenRegistry.cs"),
            @"new\(\w+Id, ""(?<icon>[^""]+)"""));

        // Булавку шторка приписывает сама, и знак у неё свой — в общий счёт он входит наравне.
        icons.AddRange(Matched(
            RepoFiles.MethodBody(
                RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/QuickSheet.cs"), "private View BuildPinButton()"),
            @"Icon = ""(?<icon>[^""]+)"""));

        var doubled = icons.GroupBy(icon => icon).Where(group => group.Count() > 1).Select(group => group.Key).ToList();

        Assert.True(doubled.Count == 0, "Один знак на два дела в шторке: " + string.Join(", ", doubled));
        Assert.True(icons.Count >= 13, $"Знаков нашлось всего {icons.Count} — разбор исходника промахнулся мимо списков.");
    }

    /// <summary>Значок и стайка — парой и по порядку: в описании они стоят рядом, и разлучить их значит потерять смысл обоих.</summary>
    private static (string Icon, string Group)[] IconsWithGroups(string body) =>
        [.. Regex.Matches(body, """Icon = "(?<icon>[^"]+)",(?:\s*Group = (?<group>\w+),)?""")
            .Select(match => (match.Groups["icon"].Value, match.Groups["group"].Value))];

    private static IEnumerable<string> Matched(string body, string pattern) =>
        Regex.Matches(body, pattern).Select(match => match.Groups["icon"].Value);
}
