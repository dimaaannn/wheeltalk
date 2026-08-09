using WheelTalk.Core.Settings;

namespace WheelTalk.Tests.Settings;

/// <summary>
/// Два правила каталога (план 30 §8). Оба про то, чего не видно глазом: ссылка в никуда просто не
/// рисуется, а признак «дополнительная» в смешанной секции теряется молча — так три строки годами
/// стояли обычными, хотя объявлены дополнительными.
/// <para>
/// Сам каталог живёт в android-проекте, и отсюда его не поднять; поэтому правила вынесены в ядро
/// чистой функцией, а <c>SettingsCatalogue.Build</c> прогоняет её на себе при запуске (в Debug) и
/// падает громко. Здесь проверяется сама функция — на списках, где нарушение заведено нарочно.
/// </para>
/// </summary>
public class SettingsCatalogueRulesTests
{
    [Fact]
    public void A_catalogue_that_follows_the_rules_has_nothing_to_say()
    {
        var descriptors = new[]
        {
            Row("Display:VoltageScale", SettingsPage.Display, "SectionVoltageTape",
                seeAlso: ["WheelConfig:CellsInSeries"]),
            Row("WheelConfig:CellsInSeries", SettingsPage.Wheel, "SectionBattery",
                seeAlso: ["Display:VoltageScale"]),
            Row("Display:Tilt", SettingsPage.Display, "SectionLook", advanced: true),
            Row("Display:Palette", SettingsPage.Display, "SectionLook", advanced: true),
        };

        Assert.Empty(SettingsCatalogueRules.Problems(descriptors));
    }

    /// <summary>
    /// Ссылка ключом и живёт: ключ, которого в каталоге нет, — это переименование, сделанное
    /// наполовину, и на экране оно выглядит как отсутствие ссылки.
    /// </summary>
    [Fact]
    public void A_link_to_a_key_nobody_has_is_a_problem()
    {
        var descriptors = new[]
        {
            Row("Display:VoltageScale", SettingsPage.Display, "SectionVoltageTape",
                seeAlso: ["WheelConfig:CellsInSeriesOld"]),
        };

        var problems = SettingsCatalogueRules.Problems(descriptors);

        Assert.Single(problems);
        Assert.Contains("WheelConfig:CellsInSeriesOld", problems[0]);
    }

    [Fact]
    public void A_link_to_itself_is_a_problem_too()
    {
        var descriptors = new[]
        {
            Row("Display:Palette", SettingsPage.Display, "SectionLook", seeAlso: ["Display:Palette"]),
        };

        Assert.Single(SettingsCatalogueRules.Problems(descriptors));
    }

    /// <summary>
    /// Ровно то, на чём обожглись (план 30, Д8): страница сортирует строки по признаку, а группирует
    /// по секции — и смешанная секция целиком встаёт туда, где стоит её первая строка.
    /// </summary>
    [Fact]
    public void A_section_that_mixes_plain_rows_with_advanced_ones_is_a_problem()
    {
        var descriptors = new[]
        {
            Row("Display:Palette", SettingsPage.Display, "SectionLook"),
            Row("Display:Tilt", SettingsPage.Display, "SectionLook", advanced: true),
        };

        var problems = SettingsCatalogueRules.Problems(descriptors);

        Assert.Single(problems);
        Assert.Contains("SectionLook", problems[0]);
    }

    /// <summary>Секция принадлежит странице: одноимённые секции двух страниц — две разные секции.</summary>
    [Fact]
    public void The_same_section_name_on_two_pages_is_two_sections()
    {
        var descriptors = new[]
        {
            Row("Display:Palette", SettingsPage.Display, "SectionLook"),
            Row("Screen:KeepOn", SettingsPage.Application, "SectionLook", advanced: true),
        };

        Assert.Empty(SettingsCatalogueRules.Problems(descriptors));
    }

    private static SettingDescriptor Row(
        string key,
        SettingsPage page,
        string section,
        bool advanced = false,
        IReadOnlyList<string>? seeAlso = null) =>
        new()
        {
            Key = key,
            Kind = SettingKind.Toggle,
            Page = page,
            SectionKey = section,
            LabelKey = key,
            Advanced = advanced,
            SeeAlso = seeAlso ?? [],
            Current = () => "False",
            Apply = _ => { },
        };
}
