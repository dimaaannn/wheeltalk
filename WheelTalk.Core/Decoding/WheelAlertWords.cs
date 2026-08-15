namespace WheelTalk.Core.Decoding;

/// <summary>
/// Which words of <see cref="GotwayDecoder"/>'s alert line (frame 0x04, bits 1-7) are worth
/// interrupting the rider for, versus status noise the rider already expects. Single place for the
/// split (plan 23, alert-strip task) — <see cref="Contracts.TelemetrySnapshot.AlertForDisplay"/> is
/// the only caller, so moving a word between the two sets never touches display code. The full
/// <see cref="Contracts.TelemetrySnapshot.Alert"/> line is untouched by this filter: it still
/// reaches <c>WheelState</c> and the ride log whole.
/// </summary>
public static class WheelAlertWords
{
    /// <summary>
    /// TransportMode is a mode announcement, not an alarm — the only word left here.
    /// <para>
    /// <c>Speed1</c>/<c>Speed2</c> used to live in this set too, back when bits 1/2 were
    /// (mis)named for speed limiting. Plan 35 §9 (owner decision 15.08.2026) renamed them to the
    /// manufacturer's own words, <c>errMosfet</c>/<c>errGyroscope</c> — real hardware faults
    /// (begode-comparison.md §2.2), not the wheel doing its job during fast riding. They no
    /// longer belong in Noise: a MOSFET or gyroscope failure is exactly the kind of thing the
    /// strip exists to surface, same as <c>errHallSensors</c> already does.
    /// </para>
    /// <para>
    /// Чего здесь нет — тоже решение, а не умолчание. <c>LowVoltage</c>, <c>OverVoltage</c> и
    /// <c>OverTemperature</c> показываются **осознанно** (владелец, 03.08.2026): это состояния, в
    /// которых колесо снижает мощность или отключается, а не штатное ограничение скорости.
    /// Пропустить их опаснее, чем показать лишний раз. <c>errHallSensors</c> — отказ железа и
    /// тревога вне обсуждения.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "TransportMode",
    };

    /// <summary>Strips <see cref="Noise"/> words out of a decoded alert line, keeps the rest as-is.</summary>
    public static string FilterForDisplay(string alertLine)
    {
        if (alertLine.Length == 0) return alertLine;

        var words = alertLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = words.Where(word => !Noise.Contains(word));
        return string.Join(' ', kept);
    }
}
