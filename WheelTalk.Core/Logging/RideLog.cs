using System.Globalization;
using WheelTalk.Core.Contracts;

namespace WheelTalk.Core.Logging;

/// <summary>
/// The ride history format of the original WheelLog (<c>LoggingService.updateFile</c>). Third-party
/// services parse these files, so the columns, their order and their precision are copied exactly:
/// <c>Locale.US</c> there is <see cref="CultureInfo.InvariantCulture"/> here, and the header text
/// is what <c>ParserLogToWheelData</c> matches against <c>LogHeaderEnum</c>.
/// <para>
/// The GPS block (<c>latitude</c>…<c>gps_distance</c>) is absent, exactly as in the original when
/// location logging is off — the six columns disappear from both the header and the rows.
/// </para>
/// <para>
/// Lines are returned without a terminator. WheelLog writes CRLF (<c>FileUtil.writeLine</c>), so
/// whoever owns the file has to say so — on Android a <c>StreamWriter</c> defaults to LF.
/// </para>
/// </summary>
public static class RideLog
{
    public const string Header =
        "date,time,speed,voltage,phase_current,current,power,torque,pwm,battery_level," +
        "distance,totaldistance,system_temp,temp2,tilt,roll,mode,alert";

    /// <summary>
    /// One row per snapshot, in header order.
    /// <para>
    /// <c>torque</c>, <c>roll</c> and <c>mode</c> are written exactly as the original writes them
    /// for our wheels, which is not the same as leaving them out. Torque is set only by the
    /// Inmotion V2 adapter and roll only by the two Inmotion families, so on a Veteran or a
    /// Gotway <c>WheelData</c> keeps its zeroes and <c>LoggingService</c> prints them through
    /// <c>%.2f</c> — a genuine WheelLog log off a Sherman L carries <c>0.00</c> in both columns.
    /// Mode is a free-form string no adapter of ours sets, and <c>WheelData.reset()</c> leaves it
    /// empty. Matching that beats inventing a gap: a foreign parser calling a float conversion on
    /// these columns has never met an empty one.
    /// </para>
    /// </summary>
    public static string FormatLine(DateTimeOffset time, TelemetrySnapshot s)
    {
        string[] fields =
        [
            time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            Fixed(s.SpeedKmh),
            Fixed(s.VoltageV),
            Fixed(s.PhaseCurrentA),
            Fixed(s.CurrentA),
            Fixed(s.PowerW),
            "0.00",                             // torque — Inmotion V2 only, zero on our wheels
            Fixed(s.Pwm),
            Whole(s.Battery),
            Whole(s.WheelDistance),
            Whole(s.TotalDistance),
            Whole(s.TemperatureC),
            Whole(s.Temperature2C),
            Fixed(s.Angle),                     // tilt
            "0.00",                             // roll — Inmotion only, zero on our wheels
            "",                                 // mode — no adapter of ours sets it
            s.Alert,
        ];
        return string.Join(',', fields);
    }

    private static string Fixed(double value) => value.ToString("F2", CultureInfo.InvariantCulture);
    private static string Whole(long value) => value.ToString(CultureInfo.InvariantCulture);
}
