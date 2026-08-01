using WheelTalk.Core.Contracts;
using WheelTalk.Core.Logging;

namespace WheelTalk.Tests.Logging;

/// <summary>
/// Third-party services parse this file, so the format is pinned character by character against
/// the original writer (<c>LoggingService.kt:169</c> for the header, <c>:285</c> for the row).
/// The reference is that code, not <c>app/src/test/resources/log_test1.csv</c> in the Android
/// repo: that file is an input for the reverse parser and was clearly hand-made — it carries
/// bare <c>0</c> where <c>%.2f</c> would have written <c>0.00</c>.
/// </summary>
public class RideLogTests
{
    private static readonly DateTimeOffset Moment =
        new(2026, 7, 27, 22, 5, 3, 40, TimeSpan.FromHours(3));

    private static readonly TelemetrySnapshot Snapshot = new()
    {
        SpeedRaw = 380,
        VoltageRaw = 15012,
        PhaseCurrentRaw = -125,
        CurrentRaw = 250,
        PowerRaw = 37530,
        Pwm = 12.5,
        Battery = 87,
        WheelDistance = 1234,
        TotalDistance = 987654,
        TemperatureRaw = 3400,
        Temperature2Raw = 2900,
        Angle = 1.5,
    };

    [Fact]
    public void Header_matches_the_original_without_the_gps_block()
    {
        Assert.Equal(
            "date,time,speed,voltage,phase_current,current,power,torque,pwm,battery_level," +
            "distance,totaldistance,system_temp,temp2,tilt,roll,mode,alert",
            RideLog.Header);
    }

    /// <summary>
    /// The <c>torque</c> and <c>roll</c> zeroes are not padding: the original sets those fields
    /// only from the Inmotion adapters, so a real WheelLog log off a Veteran or a Gotway has
    /// <c>0.00</c> in both, printed by the same <c>%.2f</c>. <c>mode</c> is the one genuinely
    /// empty column — no adapter of ours writes it, and <c>WheelData.reset()</c> clears it.
    /// </summary>
    [Fact]
    public void Row_matches_the_original_formatting()
    {
        Assert.Equal(
            "2026-07-27,22:05:03.040,3.80,150.12,-1.25,2.50,375.30,0.00,12.50,87,1234,987654,34,29,1.50,0.00,,",
            RideLog.FormatLine(Moment, Snapshot));
    }

    /// <summary>
    /// The trailing empty columns are the easy place to drop or gain a comma, and a row that no
    /// longer lines up with the header is exactly what breaks a foreign parser.
    /// </summary>
    [Fact]
    public void Row_has_as_many_columns_as_the_header()
    {
        Assert.Equal(
            RideLog.Header.Split(',').Length,
            RideLog.FormatLine(Moment, Snapshot).Split(',').Length);
    }

    /// <summary>
    /// The pinned row above carries no alert; this covers the other case. The text goes in raw,
    /// unquoted, as in the original — the reverse parser there splits on commas and would not
    /// understand quoting anyway.
    /// </summary>
    [Fact]
    public void Alert_text_lands_in_the_last_column()
    {
        var snapshot = Snapshot with { Alert = "PWM 90%" };

        Assert.EndsWith(",PWM 90%", RideLog.FormatLine(Moment, snapshot));
    }
}
