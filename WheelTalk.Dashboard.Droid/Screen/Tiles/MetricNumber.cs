using Android.Graphics;
using Android.Text;
using Android.Text.Style;
using Java.Lang;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Metrics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Число величины словами экрана: прочерк вместо нуля у молчащей (план 23 §3.1) и единица вплотную
/// к числу, а не у дальнего края плитки (решение владельца 04.08.2026).
/// <para>
/// Единица живёт в той же строке, что и число, потому что <b>читаются они вместе</b>: «77 В» — это
/// одно показание, а не число и подпись к нему. Отдельным <c>TextView</c> её прижать к числу
/// нельзя — число тянется автоподбором кегля на всю ширину, и единица уезжает вправо.
/// </para>
/// </summary>
internal static class MetricNumber
{
    public const string NoValue = "—";

    /// <summary>Текст показания без единицы — им сравнивают, менялось ли значение.</summary>
    public static string Text(MetricDescriptor metric, TelemetrySnapshot? snapshot, string format)
    {
        double? value = snapshot is null ? null : metric.Read(snapshot);

        return value is { } number ? number.ToString(format) : NoValue;
    }

    /// <summary>
    /// Показание с единицей: единица мельче и приглушена, но стоит вплотную. Кегль ей задаётся долей
    /// от кегля числа (<see cref="TilesLayout.UnitScale"/>), а не своим размером, — иначе при
    /// автоподборе она то тонула бы в числе, то спорила с ним.
    /// </summary>
    public static ICharSequence Compose(string text, string unit, Color unitColor)
    {
        if (unit.Length == 0) return new Java.Lang.String(text);

        // Тонкий пробел, а не обычный: «77 В» с обычным читается как два слова, без пробела вовсе —
        // сливается в «77В».
        var line = new SpannableString(text + " " + unit);
        int from = text.Length;
        int to = line.Length();

        line.SetSpan(new RelativeSizeSpan(TilesLayout.UnitScale), from, to, SpanTypes.InclusiveExclusive);
        line.SetSpan(new ForegroundColorSpan(unitColor), from, to, SpanTypes.InclusiveExclusive);

        return line;
    }
}
