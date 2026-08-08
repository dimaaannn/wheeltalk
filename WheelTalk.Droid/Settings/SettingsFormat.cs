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
    /// </summary>
    public static string Summarize(SettingsBinder binder, SettingsPage page)
    {
        var descriptors = binder.Page(page).SelectMany(section => section).ToList();

        string head = string.Join(" · ", descriptors
            .Where(d => !d.Advanced && !d.ReportedByWheel && d.Kind is SettingKind.Toggle or SettingKind.Number or SettingKind.Choice)
            .Take(2)
            .Select(d => $"{TranslateExtension.Get(d.LabelKey)}: {ValueText(d, binder.Read(d))}"));

        int overridden = descriptors.Count(d => !d.ReportedByWheel && binder.Read(d).IsOverridden);
        string tail = overridden > 0
            ? string.Format(CultureInfo.CurrentCulture, AppStrings.SettingsSummaryOverrides, overridden)
            : Plural.Of(descriptors.Count,
                AppStrings.SettingsSummaryCount1, AppStrings.SettingsSummaryCount2, AppStrings.SettingsSummaryCount5);

        return head.Length > 0 ? $"{head} · {tail}" : tail;
    }
}
