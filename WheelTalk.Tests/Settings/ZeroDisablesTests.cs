using System.Text.RegularExpressions;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Settings;

/// <summary>
/// Замок постоянства признака <c>ZeroDisables</c> (план настроек, «выкл.» вместо нуля). Ровно шесть
/// ручек показывают ноль словом, а не цифрой, и список этот заведён руками — седьмая, приписанная
/// молча, не должна завестись сама.
/// <para>
/// Читается по исходникам: каталог настроек собирает android-проект, тестам не видный, а держать
/// нужно именно боевую разметку. Приём тот же, что в <c>QuickSheetLayoutTests</c> — <c>RepoFiles</c>
/// и разбор текста файла, только скобки здесь не всегда плоские (у одной ручки внутри объекта
/// настройки есть блок-лямбда), поэтому вместо нежадного регэкспа — счётчик глубины скобок.
/// </para>
/// </summary>
public class ZeroDisablesTests
{
    private static readonly string[] Pages =
    [
        "WheelTalk.Droid/Settings/Catalogue/AlertsPage.cs",
        "WheelTalk.Droid/Settings/Catalogue/AppPage.cs",
        "WheelTalk.Droid/Settings/Catalogue/DisplayPage.cs",
        "WheelTalk.Droid/Settings/Catalogue/ExperimentalPage.cs",
        "WheelTalk.Droid/Settings/Catalogue/WheelPage.cs",
    ];

    /// <summary>Шесть ручек, которым признак разрешён планом — и ни одной больше.</summary>
    private static readonly string[] Expected =
    [
        "Display:WarnVolts",
        "Display:DangerVolts",
        "Display:EmptyVolts",
        "Display:BlinkHz",
        "Display:HideTenthsAbove",
        "Display:HideExtrasAbove",
    ];

    [Fact]
    public void Exactly_the_six_planned_settings_carry_ZeroDisables()
    {
        var actual = Pages
            .SelectMany(page => Descriptors(RepoFiles.Read(page)))
            .Where(descriptor => descriptor.ZeroDisables)
            .Select(descriptor => descriptor.Key)
            .ToList();

        Assert.Equal(Expected.OrderBy(key => key, StringComparer.Ordinal), actual.OrderBy(key => key, StringComparer.Ordinal));
    }

    /// <summary>
    /// Каждый <c>new() { ... }</c> списка ручек — своей записью: ключ и признак <c>ZeroDisables</c>.
    /// Границы блока считаются глубиной скобок, а не нежадным регэкспом, потому что у «Рассчитать
    /// ряд» на «Тестовых функциях» внутри объекта настройки стоит блок-лямбда со своими скобками —
    /// нежадный поиск до первой закрывающей оборвал бы запись раньше времени.
    /// </summary>
    private static List<(string Key, bool ZeroDisables)> Descriptors(string source)
    {
        var results = new List<(string, bool)>();

        int i = 0;
        while (true)
        {
            int at = source.IndexOf("new()", i, StringComparison.Ordinal);
            if (at < 0) break;

            int open = source.IndexOf('{', at);
            int depth = 0;
            int end = -1;
            for (int j = open; j < source.Length; j++)
            {
                if (source[j] == '{') depth++;
                else if (source[j] == '}' && --depth == 0) { end = j; break; }
            }

            if (end < 0) throw new InvalidOperationException($"У «new()» на позиции {at} не сошлись скобки.");

            string body = source[open..end];
            i = end + 1;

            var key = Regex.Match(body, "Key\\s*=\\s*\"(?<key>[^\"]+)\"");
            if (!key.Success) continue; // ключ задан константой (ряд ячеек) — среди шести таких нет

            results.Add((key.Groups["key"].Value, Regex.IsMatch(body, @"ZeroDisables\s*=\s*true")));
        }

        return results;
    }
}
