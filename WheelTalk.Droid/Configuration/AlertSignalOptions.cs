namespace WheelTalk.Droid.Configuration;

/// <summary>
/// Which channels an alert is allowed to use. Separate from <c>AlertOptions</c> on purpose: that
/// one holds thresholds, which are a property of the wheel, and these are a property of the phone
/// and the rider — someone riding with earphones wants the flash, someone in traffic wants neither.
/// <para>
/// Nothing here decides whether there is an alarm. Switching a channel off silences it and stops
/// whatever it was doing; it does not make the wheel any safer.
/// </para>
/// </summary>
public sealed class AlertSignalOptions
{
    public const string SectionName = "AlertSignals";

    public bool Sound { get; set; } = true;

    public bool Vibration { get; set; } = true;

    /// <summary>The camera flash, blinking in time with the beeps. Not a channel the original has.</summary>
    public bool Torch { get; set; } = true;
}
