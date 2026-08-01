using System.Globalization;
using WheelTalk.Core.Settings;

namespace WheelTalk.Droid.Settings.Catalogue;

/// <summary>
/// Every setting the app has, described once. This is the only place that knows a key belongs to a
/// particular property of a particular live object, which is what lets four pages be one page.
/// <para>
/// Numbers are described and stored in the units they are <em>shown</em> in, not the ones the
/// original packs them into: 50.0 km/h rather than 500, 3.30 V rather than 330. The conversion sits
/// in the two delegates and nowhere else, so a slider, a stored layer and a wheel's own idea of the
/// value cannot drift apart in three different places.
/// </para>
/// <para>
/// Defaults are not repeated here — they arrive in the options objects from the packaged
/// appsettings.json, and <see cref="SettingsBinder.FactoryDefaults"/> reads them straight back off
/// those objects. A default written twice is a default that will eventually be written differently.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.App/Configuration/SettingsCatalogue.cs</c> без изменений логики —
/// только пространство имён и ссылка на <c>WheelTalk.Dashboard</c> → <c>WheelTalk.Dashboard.Droid</c>.
/// Дескрипторы четырёх страниц разложены по <see cref="WheelPage"/>, <see cref="AppPage"/>,
/// <see cref="AlertsPage"/>, <see cref="DisplayPage"/> (план 14, А2.1) — этот файл лишь собирает их
/// в один список и держит форматтеры, общие для нескольких страниц.
/// </para>
/// </summary>
public static class SettingsCatalogue
{
    public static IReadOnlyList<SettingDescriptor> Build(CatalogueContext context)
    {
        return
        [
            .. WheelPage.Build(context.Wheel, context.Selected, context.Identity, context.Protocol),
            .. AppPage.Build(context.Connection, context.Power, context.Share),
            .. AlertsPage.Build(context.Alerts, context.Channels),
            .. DisplayPage.Build(context.Dashboard, context.Screen),
        ];
    }

    internal static bool ParseBool(string text) => bool.TryParse(text, out bool value) && value;

    internal static double ParseNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;

    internal static string Fixed(double value, int decimals) =>
        value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    internal static string Whole(int value) => value.ToString(CultureInfo.InvariantCulture);
}
