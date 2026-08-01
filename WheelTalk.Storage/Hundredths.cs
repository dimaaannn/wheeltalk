using System.Globalization;

namespace WheelTalk.Storage;

/// <summary>
/// Turning a computed <c>double</c> into the integer hundredths the tables hold.
/// <para>
/// Rounding, not truncation, and rounding the way <c>ToString("F2")</c> rounds — away from zero at
/// a midpoint, where <see cref="Math.Round(double)"/> alone would go to even. The export path runs
/// the stored number back through <c>F2</c>, so any other rule would show up as a ride whose CSV
/// no longer matches the one written straight from the wheel: <c>0.125</c> is <c>0.13</c> to the
/// formatter and would have been stored as <c>12</c>.
/// </para>
/// </summary>
internal static class Hundredths
{
    public static long Of(double value) => (long)Math.Round(value * 100.0, MidpointRounding.AwayFromZero);

    /// <summary>Thousandths — cell voltages only, where the difference being watched is 4.167 against 4.190.</summary>
    public static long Thousandths(double value) => (long)Math.Round(value * 1000.0, MidpointRounding.AwayFromZero);

    /// <summary>UTC, ISO-8601 with a Z. The one format that sorts as text, which is what the index needs.</summary>
    public static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    public static DateTimeOffset ParseStamp(string text) =>
        DateTimeOffset.ParseExact(text, "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
