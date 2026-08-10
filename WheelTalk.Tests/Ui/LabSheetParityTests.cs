using System.Text.RegularExpressions;
using System.Xml.Linq;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Ui;

/// <summary>
/// Замок «стенд не отстаёт от боевого» (решение владельца 10.08.2026). Стенд — то, чем смотрят на
/// шторку глазами: снимки различимости, солнце, вытянутая рука. Значит показывать он обязан
/// <b>полный состав боевого</b> — те же значки, тот же порядок, те же разделы и те же слова, а
/// чего не умеет, то неактивным, но на своём месте.
/// <para>
/// Повод у замка свой: 10.08.2026 на стенде «пропали» кнопки, которых там никогда и не было, и
/// отличить забывчивость от замысла было нечем. Теперь отстающий стенд роняет сборку.
/// </para>
/// <para>
/// Слова стенда — литералы: библиотека шторки слов не держит, а ресурсы приложения стенду не
/// видны. Поэтому здесь они сверяются с <c>AppStrings</c> — и по ключу там, где ключ известен
/// (разделы, булавка), и по значению для всех остальных.
/// </para>
/// </summary>
public class LabSheetParityTests
{
    private const string MainActivity = "WheelTalk.Droid/Main/MainActivity.cs";

    private const string LabActivity = "WheelTalk.Lab.Droid/LabActivity.cs";

    private const string Registry = "WheelTalk.Droid/Main/MainScreenRegistry.cs";

    [Fact]
    public void The_stand_shows_the_same_commands_in_the_same_sections()
    {
        var battle = IconsWithGroups(RepoFiles.MethodBody(
            RepoFiles.Read(MainActivity), "private IReadOnlyList<QuickSheetCommand> BuildWheelCommands()"));
        var stand = IconsWithGroups(RepoFiles.MethodBody(
            RepoFiles.Read(LabActivity), "private IReadOnlyList<QuickSheetCommand> BuildFakeCommands()"));

        // Пустые списки совпали бы друг с другом молча — а это значит лишь, что разбор промахнулся.
        Assert.NotEmpty(battle);
        Assert.Equal(battle, stand);
    }

    /// <summary>
    /// Переходы стенда — все три боевых. «Данные» и «Поездки» вести ему некуда, и оба стоят
    /// неактивными: это и есть та разница, которую владелец разрешил, — гаснет кнопка, а не место.
    /// </summary>
    [Fact]
    public void The_stand_shows_all_transitions_and_greys_out_what_it_cannot_open()
    {
        string body = RepoFiles.MethodBody(
            RepoFiles.Read(LabActivity), "private IReadOnlyList<QuickSheetLink> BuildFakeLinks()");

        var battle = IconsWithGroups(RepoFiles.MethodBody(
            RepoFiles.Read(MainActivity), "private IReadOnlyList<QuickSheetLink> BuildScreenLinks()"));

        Assert.NotEmpty(battle);
        Assert.Equal(battle.Select(entry => entry.Icon), IconsWithGroups(body).Select(entry => entry.Icon));
        Assert.Equal(2, Regex.Matches(body, @"IsEnabled = \(\) => false").Count);
    }

    [Fact]
    public void The_stand_shows_the_same_screen_tabs()
    {
        var battle = Matched(RepoFiles.Read(Registry), @"new\(\w+Id, QuickIcons\.(?<icon>\w+)");
        var stand = IconsWithGroups(RepoFiles.MethodBody(
                RepoFiles.Read(LabActivity), "private IReadOnlyList<QuickSheetScreen> BuildScreens()"))
            .Select(entry => entry.Icon);

        Assert.NotEmpty(battle);
        Assert.Equal(battle, stand);
    }

    /// <summary>
    /// Имена разделов и подпись булавки — те же слова, что видит райдер. Ключ берётся из боевого,
    /// значение — из <c>AppStrings</c>: переименование раздела в приложении обязано ронять стенд, а
    /// не расходиться с ним молча (так и разошлось «Перейти» с «Ссылками»).
    /// </summary>
    [Fact]
    public void The_stand_names_the_sections_with_the_words_of_the_app()
    {
        string battle = RepoFiles.Read(MainActivity);
        string stand = RepoFiles.Read(LabActivity);

        string[] slots =
        [
            @"PinLabel = \(\) => ",
            @"ScreensSectionLabel = \(\) => ",
            @"LinksSectionLabel = \(\) => ",
            "WheelNow => ",
            "Ride => ",
            "Phone => ",
        ];

        foreach (string slot in slots)
        {
            string key = Single(battle, slot + @"AppStrings\.(?<it>\w+)", slot);
            string word = Single(stand, slot + @"""(?<it>[^""]+)""", slot);

            Assert.Equal(AppWords()[key], word);
        }
    }

    /// <summary>
    /// Каждое слово шторки стенда — слово приложения. Ключа у подписи кнопки в стенде нет (она
    /// собирается из состояния), поэтому сверка идёт по значению: «Сброс max» и «Закреплён»
    /// прожили так до 10.08.2026, пока в боевом стояли «Сброс пиков» и «Экран закреплён».
    /// </summary>
    [Fact]
    public void Every_word_the_stand_shows_in_the_sheet_is_a_word_of_the_app()
    {
        string stand = RepoFiles.Read(LabActivity);
        var known = AppWords().Values.ToHashSet();

        string[] parts =
        [
            "private IReadOnlyList<QuickSheetCommand> BuildFakeCommands()",
            "private IReadOnlyList<QuickSheetLink> BuildFakeLinks()",
            "private IReadOnlyList<QuickSheetScreen> BuildScreens()",
        ];

        foreach (string part in parts)
        {
            var words = Regex.Matches(RepoFiles.MethodBody(stand, part), @"""(?<it>[^""]+)""");
            Assert.NotEmpty(words);

            foreach (Match word in words)
            {
                string it = word.Groups["it"].Value;
                Assert.True(known.Contains(it), $"Слово стенда «{it}» в приложении не встречается — разошлись.");
            }
        }
    }

    private static Dictionary<string, string> AppWords() =>
        XDocument.Parse(RepoFiles.Read("WheelTalk.Droid/Resources/Strings/AppStrings.resx"))
            .Root!.Elements("data")
            .ToDictionary(data => data.Attribute("name")!.Value, data => data.Element("value")!.Value);

    /// <summary>Единственное совпадение — иначе это не то место, и сверка врёт.</summary>
    private static string Single(string source, string pattern, string what)
    {
        var found = Regex.Matches(source, pattern);
        Assert.True(found.Count == 1, $"«{what}» нашлось {found.Count} раз — разбор исходника промахнулся.");
        return found[0].Groups["it"].Value;
    }

    private static (string Icon, string Group)[] IconsWithGroups(string body) =>
        [.. Regex.Matches(body, @"Icon = QuickIcons\.(?<icon>\w+),(?:\s*Group = (?<group>\w+),)?")
            .Select(match => (match.Groups["icon"].Value, match.Groups["group"].Value))];

    private static IEnumerable<string> Matched(string body, string pattern) =>
        Regex.Matches(body, pattern).Select(match => match.Groups["icon"].Value);
}
