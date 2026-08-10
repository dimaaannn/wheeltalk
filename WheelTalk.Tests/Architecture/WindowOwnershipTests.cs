using System.Text.RegularExpressions;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Architecture;

/// <summary>
/// Замок «у окна поверх экрана есть хозяин» — на все android-проекты разом, а не на одно место.
/// Диалог висит не на ветви вью, а на <b>окне активности</b>: показанный и брошенный, он переживает
/// её смерть — активность уничтожается вместе со своим окном, Android пишет <c>WindowLeaked</c>, а
/// диалог держит живой всю разметку за собой. Смерть приходит буднично: поворот телефона у экранов
/// без <c>ConfigurationChanges</c>, смена светлой темы на тёмную, экран поиска, закрывающий себя сам
/// по концу подключения.
/// <para>
/// Повод — дамп владельца 10.08.2026: единственный стек в его хвосте был <c>WindowLeaked</c> от
/// открытого полноэкранного графика. Плитки вылечены хозяином в библиотеке, активности — <c>OwnedWindow</c>;
/// этот замок стережёт <b>следующее</b> окно, которого ещё нет.
/// </para>
/// <para>
/// Читается по исходникам: android-проекты тестам не видны, поднять активность в тесте нечем, а
/// правило простое и глазами проверяемое — кто открыл окно, тот его и закрывает.
/// </para>
/// </summary>
public class WindowOwnershipTests
{
    private static readonly string[] Projects =
        ["WheelTalk.Droid", "WheelTalk.Dashboard.Droid", "WheelTalk.Lab.Droid"];

    /// <summary>Окно строится и показывается — значит либо его показывает хозяин, либо оно отдаётся хозяину наружу.</summary>
    private const string HandsItOut = "public static Dialog Show(";

    [Fact]
    public void Every_window_is_shown_by_an_owner_or_handed_to_one()
    {
        foreach (var (path, source) in Sources())
        {
            if (source.Contains(HandsItOut, StringComparison.Ordinal)) continue;

            foreach (Match window in Regex.Matches(source, @"new (?:AlertDialog\.Builder|Dialog)\("))
            {
                string before = source[..window.Index];

                Assert.True(
                    Regex.IsMatch(before, @"\w+\.Show\($"),
                    $"Окно без хозяина: {Name(path)}, знак {window.Index}. Показывай через OwnedWindow "
                        + "либо отдавай наружу — тому, кто закроет его по концу своей жизни.");
            }
        }
    }

    /// <summary>
    /// Хозяин закрывает своё окно, умирая. Держать окно и не закрывать его — та же утечка, только с
    /// лишним полем: <c>OnDestroy</c> и есть тот конец жизни, о котором активность узнаёт.
    /// </summary>
    [Fact]
    public void Every_owner_closes_its_window_when_it_dies()
    {
        var owners = Sources().Where(file => Regex.IsMatch(file.Source, @"OwnedWindow \w+ = new")).ToList();

        Assert.NotEmpty(owners);

        foreach (var (path, source) in owners)
        {
            Assert.Contains("protected override void OnDestroy()", source);
            Assert.Contains(".Close()", RepoFiles.MethodBody(source, "protected override void OnDestroy()"));
        }
    }

    private static string Name(string path) => path[(RepoFiles.Root.Length + 1)..];

    private static IEnumerable<(string Path, string Source)> Sources() =>
        Projects
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(RepoFiles.Root, project), "*.cs", SearchOption.AllDirectories))
            .Where(NotGenerated)
            .Select(file => (file, File.ReadAllText(file)));

    /// <summary>Только рукописное: <c>obj</c> и <c>bin</c> полны сгенерированного, и правило не о нём.</summary>
    private static bool NotGenerated(string path)
    {
        string separator = Path.DirectorySeparatorChar.ToString();
        return !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
            && !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal);
    }
}
