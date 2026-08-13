using System.Text.RegularExpressions;
using System.Xml.Linq;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Ui;

/// <summary>
/// Палитра документных экранов (план 33): настройки, «Поездки», «Данные», поиск колёс, «Что уйдёт».
/// Они следуют системной теме, и цвет у них — <b>роль</b> из ресурсов, а не литерал в коде.
/// <para>
/// <b>Случившаяся поломка (снимок владельца 13.08.2026).</b> Настройки в светлой системной теме
/// выходили о двух хозяевах: фон и заголовок красила тема, а карточки, обводки, разделители и
/// подсказки — тринадцать тёмных литералов в коде. В тёмной теме этого не видно, оттого и жило.
/// Это тот же грех «один рычаг — два хозяина» (план 29), только про цвет.
/// </para>
/// <para>
/// Здесь три обещания, и каждое ломается тихо, если его не стеречь: роль объявлена <b>в обоих</b>
/// наборах (иначе на одной из тем сборка встанет либо цвет уедет в чужой), дневной набор
/// <b>читается</b> (числа контраста считаются, а не прикидываются на глаз — тот же порядок, что
/// задан плану 25), а ночной набор <b>равен прежним литералам побайтово</b> — владелец ездит на
/// тёмной, и починка светлой не имеет права тронуть его экран.
/// </para>
/// </summary>
public class DocumentPaletteTests
{
    private const string Day = "WheelTalk.Droid/Resources/values/colors.xml";

    private const string Night = "WheelTalk.Droid/Resources/values-night/colors.xml";

    /// <summary>Роли палитры. Всё, что начинается с <c>doc_</c>, — она; остальные цвета ресурсов чужие.</summary>
    private const string RolePrefix = "doc_";

    /// <summary>
    /// Чем меряется роль. Текст — 4,5:1 (WCAG 1.4.3), орган управления и его граница — 3:1
    /// (WCAG 1.4.11), украшение — ничем: заливка карточки и подсветка строки ничего не сообщают
    /// сами по себе, их работу делает граница и соседство.
    /// </summary>
    private enum Kind
    {
        Text,
        Component,
        Decor,
    }

    /// <summary>
    /// Роль, чем она мерится и на чём стоит. Фон у почти всех — карточка: строки настроек живут
    /// внутри неё, и она темнее страницы, то есть худший из двух случаев.
    /// </summary>
    private static readonly (string Role, Kind Kind, string Background)[] Roles =
    [
        ("doc_surface", Kind.Decor, ""),
        ("doc_card", Kind.Decor, ""),
        ("doc_card_border", Kind.Decor, ""),
        ("doc_row_divider", Kind.Decor, ""),
        ("doc_override_fill", Kind.Decor, ""),
        ("doc_highlight", Kind.Decor, ""),
        ("doc_share_border", Kind.Decor, ""),
        ("doc_divider", Kind.Decor, ""),

        ("doc_border", Kind.Component, "doc_card"),
        ("doc_dependant_bar", Kind.Component, "doc_card"),

        ("doc_text_primary", Kind.Text, "doc_card"),
        ("doc_text_title", Kind.Text, "doc_card"),
        ("doc_text_control", Kind.Text, "doc_card"),
        ("doc_text_muted", Kind.Text, "doc_card"),
        ("doc_text_secondary", Kind.Text, "doc_card"),
        ("doc_hint", Kind.Text, "doc_card"),
        ("doc_hint_dim", Kind.Text, "doc_card"),
        ("doc_chevron", Kind.Text, "doc_card"),
        ("doc_accent", Kind.Text, "doc_card"),
        ("doc_override", Kind.Text, "doc_card"),
        ("doc_link", Kind.Text, "doc_card"),
        ("doc_warning", Kind.Text, "doc_card"),
        ("doc_picked", Kind.Text, "doc_card"),
        ("doc_cell_low", Kind.Text, "doc_card"),
        ("doc_cell_high", Kind.Text, "doc_card"),

        // Единственная роль, которая стоит не на странице: текст поверх акцентной заливки.
        ("doc_on_accent", Kind.Text, "doc_accent"),
    ];

    /// <summary>
    /// Ночь — <b>тот самый литерал</b>, которым это место красилось до перекладки на роли, и место,
    /// откуда он взят. Таблица и есть обещание «тёмная тема не изменилась ни на байт»: перекрасить
    /// ночь теперь можно только вместе с этой строкой, то есть осознанно.
    /// </summary>
    private static readonly (string Role, string Was, string From)[] Inherited =
    [
        ("doc_surface", "#1F1F1F", "ScreenKit.PageBackground, тёмная ветка"),
        ("doc_card", "#282828", "SettingsCategoryActivity.SectionFill / SettingsActivity.CardFill"),
        ("doc_card_border", "#3A3A3A", "SectionBorder / CardBorder"),
        ("doc_row_divider", "#333333", "SettingsCategoryActivity.RowDivider"),
        ("doc_border", "#4A4A4A", "BorderColor; пунктир карточки «Тестовые функции»"),
        ("doc_dependant_bar", "#3F3F3F", "SettingsCategoryActivity.DependantBar"),
        ("doc_text_primary", "#FFFFFF", "UiKit.PlainText, тёмная ветка"),
        ("doc_text_title", "#D8D8D8", "заголовок строки настройки"),
        ("doc_text_control", "#DDDDDD", "знак кнопки листа правки"),
        ("doc_text_muted", "#CFCFCF", "невыбранный чип, «Отмена»"),
        ("doc_text_secondary", "#9A9A9A", "SectionTitleColor / DimText / подпись листа"),
        ("doc_hint", "#8A8A8A", "HintColor; сводка «Тестовых функций»"),
        ("doc_hint_dim", "#7A7A7A", "подсказка корня настроек"),
        ("doc_chevron", "#6E6E6E", "SettingsActivity.Chevron"),
        ("doc_accent", "#AC99EA", "AccentColor; UiKit.CreateButton, тёмная ветка"),
        ("doc_on_accent", "#1F1F1F", "текст кнопки «Готово» на акценте"),
        ("doc_override", "#FF8F00", "OverrideColor / OwnColor"),
        ("doc_override_fill", "#29FF8F00", "SettingsActivity.OwnBadgeFill"),
        ("doc_highlight", "#33FF8F00", "SettingsCategoryActivity.HighlightColor"),
        ("doc_link", "#4FA3E3", "LinkColor"),
        ("doc_warning", "#E53935", "WarningColor"),
        ("doc_picked", "#009E73", "SettingsActivity.WheelPicked; «нашлось» в поиске"),
        ("doc_cell_low", "#FF4500", "Color.OrangeRed, крайняя нижняя ячейка «Данных»"),
        ("doc_cell_high", "#3CB371", "Color.MediumSeaGreen, крайняя верхняя ячейка"),
        ("doc_share_border", "#40808080", "DiagnosticsShareActivity, обводка карточки"),
        ("doc_divider", "#888888", "UiKit.Divider, Color.Gray"),
    ];

    /// <summary>Роль объявлена в обоих наборах, и лишних ни в одном: пропуск — цвет чужой темы на экране.</summary>
    [Fact]
    public void Every_role_is_declared_in_both_sets()
    {
        var day = Colours(Day);
        var night = Colours(Night);

        Assert.NotEmpty(day);

        foreach (var (role, _, _) in Roles)
        {
            Assert.True(day.ContainsKey(role), $"Роли «{role}» нет в дневном наборе.");
            Assert.True(night.ContainsKey(role), $"Роли «{role}» нет в ночном наборе.");
        }

        Assert.Equal(
            Roles.Select(role => role.Role).OrderBy(name => name, StringComparer.Ordinal),
            day.Keys.Where(IsRole).OrderBy(name => name, StringComparer.Ordinal));

        Assert.Equal(
            day.Keys.Where(IsRole).OrderBy(name => name, StringComparer.Ordinal),
            night.Keys.Where(IsRole).OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// Дневной набор читается: текст — 4,5:1, орган управления — 3:1. Числа считаются здесь, а не
    /// подбираются на глаз: светлый набор заводился впервые, и «кажется, видно» — не мера.
    /// </summary>
    [Fact]
    public void The_day_set_is_readable_by_the_numbers()
    {
        var day = Colours(Day);

        foreach (var (role, kind, background) in Roles)
        {
            if (kind == Kind.Decor) continue;

            double need = kind == Kind.Text ? 4.5 : 3.0;
            double got = Contrast(day[role], day[background]);

            Assert.True(got >= need,
                $"День: «{role}» на «{background}» даёт {got:F2}:1 при норме {need:F1}:1.");
        }
    }

    /// <summary>
    /// Ночной набор — прежние литералы, знак в знак. Не «похожие»: слить два почти одинаковых серых
    /// в одну роль значило бы перекрасить экран владельцу, который об этом не просил.
    /// </summary>
    [Fact]
    public void The_night_set_is_exactly_what_the_dark_screens_had()
    {
        var night = Colours(Night);

        foreach (var (role, was, from) in Inherited)
        {
            Assert.True(night.TryGetValue(role, out string? now), $"Роли «{role}» нет в ночном наборе.");
            Assert.True(string.Equals(was, now, StringComparison.OrdinalIgnoreCase),
                $"Ночь: «{role}» стал {now}, а был {was} ({from}) — тёмная тема переменилась.");
        }

        Assert.Equal(Roles.Length, Inherited.Length);
    }

    /// <summary>
    /// В документных экранах не осталось литералов цвета: цвет там — роль, и другого источника нет.
    /// Белый список — файлы <b>всегда тёмных</b> поверхностей: плеер, панель, «Цифры», шторка. Им
    /// системная тема безразлична, и палитра у них своя, кодом (решение владельца 13.08.2026).
    /// </summary>
    [Fact]
    public void No_colour_literals_are_left_in_the_document_screens()
    {
        var literal = new Regex(@"Color\.(ParseColor|White|Black|Gray|Red|Green|Blue|Orange\w*|Medium\w*)");

        var guilty = Directory
            .EnumerateFiles(Path.Combine(RepoFiles.Root, "WheelTalk.Droid"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => !AlwaysDark.Contains(Path.GetFileName(path)))
            .Where(path => literal.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetFileName(path))
            .ToList();

        Assert.True(guilty.Count == 0,
            "Литерал цвета в документном экране: " + string.Join(", ", guilty));
    }

    /// <summary>
    /// Всегда тёмные поверхности приложения. Плеер — та же панель приборов, и светлой её не бывает;
    /// свой список тут короток намеренно: попадание нового файла сюда должно быть решением, а не
    /// способом обойти замок.
    /// </summary>
    private static readonly HashSet<string> AlwaysDark = new(StringComparer.Ordinal)
    {
        "PlaybackActivity.cs",
    };

    private static bool IsRole(string name) => name.StartsWith(RolePrefix, StringComparison.Ordinal);

    private static Dictionary<string, string> Colours(string file) =>
        XDocument.Parse(RepoFiles.Read(file))
            .Root!.Elements("color")
            .ToDictionary(node => node.Attribute("name")!.Value, node => node.Value.Trim());

    /// <summary>
    /// Контраст по WCAG 2.1: <c>(L1 + 0,05) / (L2 + 0,05)</c>. Прозрачность крайних значений здесь
    /// не встречается — роли, которые меряются, все непрозрачны, — а у украшений её и мерить нечем:
    /// сквозь них видно то, на чём они лежат.
    /// </summary>
    private static double Contrast(string first, string second)
    {
        double one = Luminance(first);
        double two = Luminance(second);

        return (Math.Max(one, two) + 0.05) / (Math.Min(one, two) + 0.05);
    }

    private static double Luminance(string colour)
    {
        string hex = colour.TrimStart('#');
        if (hex.Length == 8) hex = hex[2..];

        double Channel(int at)
        {
            double part = Convert.ToInt32(hex.Substring(at, 2), 16) / 255.0;

            return part <= 0.04045 ? part / 12.92 : Math.Pow((part + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(0)) + (0.7152 * Channel(2)) + (0.0722 * Channel(4));
    }
}
