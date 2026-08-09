namespace WheelTalk.Core.Settings;

/// <summary>
/// Два правила, которым каталог обязан отвечать, — и оба такие, что нарушение видно не глазом, а
/// разбором (план 30 §8). Проверяются здесь, а не в разметке: каталог собирается один раз при
/// запуске, и оба вопроса — про сам список, а не про то, как он нарисован.
/// <list type="number">
///   <item><b>Ссылка ведёт куда-то.</b> <see cref="SettingDescriptor.SeeAlso"/> назван ключом; ключ,
///   которого в каталоге нет, — это ссылка, которая на экране просто не появится, и заметить её
///   пропажу нечем.</item>
///   <item><b>Секция однородна по <see cref="SettingDescriptor.Advanced"/>.</b> Страница сортирует
///   строки по этому признаку, а потом группирует по секции: секция целиком уезжает туда, где
///   стоит её первая строка. В смешанной секции признак у остальных строк <b>молча теряется</b> —
///   ровно так три «дополнительные» строки годами рисовались обычными (план 30, Д8).</item>
/// </list>
/// </summary>
public static class SettingsCatalogueRules
{
    /// <summary>Найденные нарушения, по строке на каждое. Пустой список — каталог в порядке.</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<SettingDescriptor> descriptors)
    {
        var problems = new List<string>();
        var keys = descriptors.Select(descriptor => descriptor.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var descriptor in descriptors)
        {
            foreach (string target in descriptor.SeeAlso)
            {
                if (!keys.Contains(target))
                {
                    problems.Add($"«{descriptor.Key}» ссылается на «{target}», которого в каталоге нет.");
                }

                if (target == descriptor.Key)
                {
                    problems.Add($"«{descriptor.Key}» ссылается сама на себя.");
                }
            }
        }

        // Секция принадлежит странице: одно и то же имя секции на двух страницах — две разные
        // секции, и однородность у каждой своя.
        foreach (var section in descriptors.GroupBy(descriptor => (descriptor.Page, descriptor.SectionKey)))
        {
            if (section.Select(descriptor => descriptor.Advanced).Distinct().Count() > 1)
            {
                problems.Add(
                    $"Секция «{section.Key.SectionKey}» страницы {section.Key.Page} смешивает обычные строки с "
                    + "дополнительными — признак Advanced в ней потеряется.");
            }
        }

        return problems;
    }
}
