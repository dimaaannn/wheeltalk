using System.Globalization;
using WheelTalk.Core.Settings;

namespace WheelTalk.Droid.Settings.Catalogue;

/// <summary>
/// Every setting the app has, described once. This is the only place that knows a key belongs to a
/// particular property of a particular live object, which is what lets five pages be one page.
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
/// Дескрипторы пяти страниц разложены по <see cref="WheelPage"/>, <see cref="AppPage"/>,
/// <see cref="AlertsPage"/>, <see cref="DisplayPage"/> (план 14, А2.1) — этот файл лишь собирает их
/// в один список и держит форматтеры, общие для нескольких страниц.
/// </para>
/// </summary>
public static class SettingsCatalogue
{
    public static IReadOnlyList<SettingDescriptor> Build(CatalogueContext context)
    {
        var descriptors = Describe(context);

#if DEBUG
        // Оба правила ловят то, чего не видно глазом: ссылку в никуда и потерянный признак
        // «дополнительная» (план 30 §8). Проверка живёт здесь, потому что каталог собирается один
        // раз при запуске, и это единственное место, где он целиком на руках. В Release её нет:
        // райдеру от падения на старте пользы никакой, а ссылка в никуда просто не нарисуется.
        if (SettingsCatalogueRules.Problems(descriptors) is { Count: > 0 } problems)
        {
            throw new InvalidOperationException(
                "Каталог настроек нарушает свои правила:" + Environment.NewLine
                + string.Join(Environment.NewLine, problems));
        }
#endif

        return descriptors;
    }

    private static IReadOnlyList<SettingDescriptor> Describe(CatalogueContext context)
    {
        return
        [
            .. WheelPage.Build(context.Wheel, context.Selected, context.Identity, context.Protocol,
                context.RestartAuthentication),
            .. AppPage.Build(context.Connection, context.Power, context.Screen, context.Storage,
                context.Diagnostics, context.Share, context.ShareFullLog),
            .. AlertsPage.Build(context.Alerts, context.Channels),
            .. DisplayPage.Build(context.Dashboard, context.Alerts, context.Panels),

            // Пятая страница собирается последней, потому что она и в списке последняя: не тема, а
            // отметка зрелости (план 28). Строки в ней — те же самые описания, что стояли выше, с
            // одним изменённым полем Page; ключи, признаки и условия переехали нетронутыми.
            .. ExperimentalPage.Build(context.Wheel, context.Dashboard, context.Channels,
                context.PreviewAlarm, context.LastFrame, context.SaveCells),
        ];
    }

    internal static bool ParseBool(string text) => bool.TryParse(text, out bool value) && value;

    internal static double ParseNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;

    internal static string Fixed(double value, int decimals) =>
        value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    internal static string Whole(int value) => value.ToString(CultureInfo.InvariantCulture);
}
