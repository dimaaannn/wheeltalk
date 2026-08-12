using System.Text.RegularExpressions;
using System.Xml.Linq;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Ui;

/// <summary>
/// Замок «слова плиток на стенде — боевые» (по образцу шторочного <see cref="LabSheetParityTests"/>).
/// Стенд — то, чем смотрят на плитки глазами: снимки различимости, солнце, вытянутая рука. Слова у
/// него свои литералами (библиотека ресурсов не держит, а ресурсы приложения стенду не видны),
/// и разойтись они могут молча — при первой же правке текста в <c>AppStrings.resx</c>.
/// <para>
/// Дыру нашла ревизия текстов 12.08.2026: у шторки паритет заперт с 11.08, а у плиток — нет, хотя
/// расходиться им нечем иначе как забывчивостью. Теперь забывчивость роняет сборку.
/// </para>
/// </summary>
public class LabTileWordsParityTests
{
    private const string LabWords = "WheelTalk.Lab.Droid/Ui/LabMetricWords.cs";

    private const string Strings = "WheelTalk.Droid/Resources/Strings/AppStrings.resx";

    /// <summary>
    /// Каждое слово стенда — слово приложения под тем же ключом. Не «похожее», а <b>то же самое</b>:
    /// снимок со стенда должен показывать ровно то, что увидит райдер.
    /// </summary>
    [Fact]
    public void Every_word_of_the_stand_matches_the_app_under_the_same_key()
    {
        var app = AppWords();
        var stand = StandWords();

        Assert.NotEmpty(stand);

        foreach (var (key, word) in stand)
        {
            Assert.True(app.ContainsKey(key), $"Стенд знает ключ «{key}», которого нет в AppStrings.");
            Assert.True(app[key] == word,
                $"«{key}»: у стенда «{word}», у приложения «{app[key]}» — слова разошлись.");
        }
    }

    /// <summary>
    /// Подписи тесных мест — тем же правилом. Ключ у них тот же, что у полного слова, плюс «Short»
    /// (короткое имя четвертной плитки) либо «Sign» (знак величины в центре панели, где единица живёт
    /// в самой подписи); стенд обязан знать их наравне с приложением, иначе на четвертных и в центре
    /// стоит сырой ключ вместо слова.
    /// </summary>
    [Fact]
    public void The_stand_knows_every_tight_label_the_app_has()
    {
        var stand = StandWords();

        var missing = AppWords()
            .Where(pair => Tight.Any(suffix => pair.Key.EndsWith(suffix, StringComparison.Ordinal)))
            .Where(pair => !stand.ContainsKey(pair.Key))
            .Select(pair => pair.Key)
            .ToList();

        Assert.True(missing.Count == 0, "Тесных подписей у стенда нет: " + string.Join(", ", missing));
    }

    /// <summary>Чем кончается ключ подписи для тесного места: короткое имя и знак величины.</summary>
    private static readonly string[] Tight = ["Short", "Sign"];

    /// <summary>
    /// Каждое слово, которое просит редактор центра, стенд обязан знать. Редактор на стенде — тот же
    /// класс, что в приложении, но словарь у стенда свой, и ключ без слова стоит на экране сырым:
    /// «CentreEditTitle» заголовком окна — поймано прогоном 12.08.2026, жило с заведения редактора.
    /// </summary>
    [Fact]
    public void The_stand_knows_every_word_the_centre_editor_asks_for()
    {
        var stand = StandWords();

        var missing = Regex.Matches(
                RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/CentreEditor.cs"),
                @"words\(""(?<key>\w+)""\)")
            .Select(match => match.Groups["key"].Value)
            .Distinct()
            .Where(key => !stand.ContainsKey(key))
            .ToList();

        Assert.True(missing.Count == 0, "Слов редактора у стенда нет: " + string.Join(", ", missing));
    }

    private static Dictionary<string, string> AppWords() =>
        XDocument.Parse(RepoFiles.Read(Strings))
            .Root!.Elements("data")
            .ToDictionary(data => data.Attribute("name")!.Value, data => data.Element("value")!.Value);

    private static Dictionary<string, string> StandWords() =>
        Regex.Matches(RepoFiles.Read(LabWords), @"\[""(?<key>\w+)""\] = ""(?<word>[^""]*)""")
            .ToDictionary(match => match.Groups["key"].Value, match => match.Groups["word"].Value);
}
