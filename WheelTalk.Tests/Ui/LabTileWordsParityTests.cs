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
    /// Короткие подписи четвертных плиток — тем же правилом. Ключ у них тот же, что у полного слова,
    /// плюс «Short»; стенд обязан знать их наравне с приложением, иначе на четвертных стоит сырой
    /// ключ вместо слова.
    /// </summary>
    [Fact]
    public void The_stand_knows_every_short_label_the_app_has()
    {
        var stand = StandWords();

        var missing = AppWords()
            .Where(pair => pair.Key.EndsWith("Short", StringComparison.Ordinal))
            .Where(pair => !stand.ContainsKey(pair.Key))
            .Select(pair => pair.Key)
            .ToList();

        Assert.True(missing.Count == 0, "Коротких подписей у стенда нет: " + string.Join(", ", missing));
    }

    private static Dictionary<string, string> AppWords() =>
        XDocument.Parse(RepoFiles.Read(Strings))
            .Root!.Elements("data")
            .ToDictionary(data => data.Attribute("name")!.Value, data => data.Element("value")!.Value);

    private static Dictionary<string, string> StandWords() =>
        Regex.Matches(RepoFiles.Read(LabWords), @"\[""(?<key>\w+)""\] = ""(?<word>[^""]*)""")
            .ToDictionary(match => match.Groups["key"].Value, match => match.Groups["word"].Value);
}
