using System.Globalization;
using WheelTalk.Core.Settings;
using WheelTalk.Droid.Resources.Strings;

namespace WheelTalk.Droid.Settings;

/// <summary>
/// Formatting shared by the root screen (category summaries) and the category screen (row values,
/// the number-editing dialog): parsing/printing the text a <see cref="SettingDescriptor"/> stores,
/// and how much of a row's value to say out loud. Kept in one place because the number dialog, the
/// row's own readout and the root summary all format the very same value and must never drift.
/// </summary>
internal static class SettingsFormat
{
    public static bool ParseBool(string text) => bool.TryParse(text, out bool value) && value;

    public static double ParseNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;

    /// <summary>The stored text for a number, in the units the descriptor is shown in.</summary>
    public static string Store(SettingDescriptor descriptor, double value) =>
        value.ToString("F" + descriptor.Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>Rounds to the descriptor's step before clamping, so a value right at the edge does not get pushed back out by rounding.</summary>
    public static double Snap(SettingDescriptor descriptor, double value) =>
        descriptor.Step <= 0 ? value : Math.Round(value / descriptor.Step) * descriptor.Step;

    /// <summary>Number plus unit, for the row's readout and for the dialog's range line.</summary>
    public static string Display(SettingDescriptor descriptor, double value)
    {
        string number = Store(descriptor, value);
        return descriptor.UnitKey is { } unit ? $"{number} {TranslateExtension.Get(unit)}" : number;
    }

    public static int IndexOfChoice(SettingDescriptor descriptor, string value)
    {
        var choices = descriptor.Choices;
        for (int i = 0; i < choices.Count; i++)
        {
            if (choices[i] == value) return i;
        }

        return 0;
    }

    public static string ChoiceLabel(SettingDescriptor descriptor, string value)
    {
        int index = IndexOfChoice(descriptor, value);
        return index < descriptor.ChoiceLabelKeys.Count ? TranslateExtension.Get(descriptor.ChoiceLabelKeys[index]) : value;
    }

    /// <summary>
    /// Что сказано в строке «сообщено колесом» — значение по виду настройки: переключатель словом,
    /// число числом (с единицей, если она у описания есть), выбор — названием варианта, а не кодом.
    /// <para>
    /// Отдельно от <see cref="ValueText"/> намеренно, и разница в том, чего здесь НЕТ. Нет прижатия
    /// к границам ручки: у Veteran наклон приходит числом 200 при <c>Maximum</c> по умолчанию 100, и
    /// клампом экран сказал бы 100 — то есть соврал. Нет и «выкл.» вместо нуля
    /// (<see cref="SettingDescriptor.ZeroDisables"/>): это правило правимых ручек. Экран повторяет
    /// слово колеса как есть и не толкует его (слово владельца 15.08.2026, план 34).
    /// </para>
    /// </summary>
    public static string ReportedText(SettingDescriptor descriptor, string value) =>
        descriptor.Kind switch
        {
            SettingKind.Toggle => ParseBool(value) ? AppStrings.Yes : AppStrings.No,
            SettingKind.Number => Display(descriptor, ParseNumber(value)),
            SettingKind.Choice => ChoiceLabel(descriptor, value),
            _ => value,
        };

    /// <summary>What a row says its value is, regardless of kind — used by the row itself and by the root summary.</summary>
    public static string ValueText(SettingDescriptor descriptor, ResolvedSetting resolved)
    {
        string raw = resolved.Value ?? descriptor.Current();
        return descriptor.Kind switch
        {
            SettingKind.Toggle => ParseBool(raw) ? AppStrings.Yes : AppStrings.No,
            SettingKind.Number => Display(descriptor, Math.Clamp(ParseNumber(raw), descriptor.Minimum, descriptor.Maximum)),
            SettingKind.Choice => ChoiceLabel(descriptor, raw),
            _ => raw,
        };
    }

    /// <summary>
    /// Root-screen summary for one category: a couple of headline values (the plan's "Колесо — Sherman
    /// L, знак тока прямой" example), plus how many of the category's settings are overridden for the
    /// wheel currently selected — or, with no wheel selected, how many exist at all. Picking the first
    /// two eligible descriptors rather than hand-picked ones per category keeps this generic: the
    /// catalogue already orders each page by what matters most.
    /// <para>
    /// Читается по <b>боевой</b> области — сводка говорит, чем приложение живёт, а не что открыто
    /// на странице настроек (план 29 §29.3).
    /// </para>
    /// </summary>
    /// <param name="wheelModel">
    /// Как колесо себя назвало («Sherman L») — из последнего кадра телеметрии. Нужен одной
    /// <see cref="SettingsPage.WheelDevice"/>: у остальных страниц сводка о наших слоях, и модель
    /// колеса к ней отношения не имеет. Пусто — колесо ещё не назвалось.
    /// </param>
    public static string Summarize(SettingsBinder binder, SettingsPage page, string wheelModel)
    {
        // Страница, где всё сказано колесом, общей сводкой была бы нема: и заголовок, и хвост
        // отсеивают ReportedByWheel — своих значений у такой строки не бывает, считать нечего
        // (план 34 §4, шаг 2.4). Она говорит другое: чьи это настройки и сколько их подтвердило
        // само колесо.
        if (page == SettingsPage.WheelDevice) return SummarizeWheelDevice(binder, wheelModel);

        string scope = binder.LiveScope;
        var descriptors = binder.Page(page, scope).SelectMany(section => section).ToList();

        string head = string.Join(" · ", descriptors
            .Where(d => !d.Advanced && !d.ReportedByWheel && d.Kind is SettingKind.Toggle or SettingKind.Number or SettingKind.Choice)
            .Take(2)
            .Select(d => $"{TranslateExtension.Get(d.LabelKey)}: {ValueText(d, binder.Read(d, scope))}"));

        int overridden = descriptors.Count(d => !d.ReportedByWheel && binder.Read(d, scope).IsOverridden);
        string tail = overridden > 0
            ? string.Format(CultureInfo.CurrentCulture, AppStrings.SettingsSummaryOverrides, overridden)
            : Plural.Of(descriptors.Count,
                AppStrings.SettingsSummaryCount1, AppStrings.SettingsSummaryCount2, AppStrings.SettingsSummaryCount5);

        return head.Length > 0 ? $"{head} · {tail}" : tail;
    }

    /// <summary>
    /// «Sherman L · 15 значений». Считаются <b>показанные</b> строки, а не все описанные: настройки,
    /// которой у этого колеса нет, оно и не присылает (сентинел прячет строку), и обещать её числом
    /// в сводке значило бы соврать раньше, чем человек откроет страницу.
    /// <para>
    /// Ноль строк — сводка говорит «настройки недоступны» (слово владельца 16.08.2026), и это
    /// один ответ на два случая: связи нет вовсе и связь есть, но колесо не сообщило ни одной
    /// настройки — все поля пришли сентинелом. Прежде тут стояло «Подключитесь к колесу», и во
    /// втором случае это было прямой ложью: человек подключён, телеметрия идёт, а карточка советует
    /// подключиться. Случай редкий, но врал бы он именно тем, что уводит поиск причины не туда.
    /// </para>
    /// </summary>
    private static string SummarizeWheelDevice(SettingsBinder binder, string wheelModel)
    {
        int shown = binder.Page(SettingsPage.WheelDevice, binder.LiveScope).Sum(section => section.Count());
        if (shown == 0) return AppStrings.SettingsWheelDeviceUnavailable;

        string values = Plural.Of(shown,
            AppStrings.SettingsValuesCount1, AppStrings.SettingsValuesCount2, AppStrings.SettingsValuesCount5);

        return wheelModel.Length > 0 ? $"{wheelModel} · {values}" : values;
    }
}
