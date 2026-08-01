namespace WheelTalk.Core.Alerts;

/// <summary>
/// What the wheel is doing that the rider should know about, as a state rather than as events:
/// whatever acts on it — sound, screen, torch — is on for exactly as long as the state says so,
/// which means a missed notification cannot leave a signal stuck on.
/// </summary>
/// <param name="PwmIntensity">
/// 0 below the warning threshold, rising linearly to 1 at the critical one and staying there above
/// it. Deliberately a number and not a level: how it sounds or looks is the presentation's call.
/// </param>
/// <param name="SpeedExceeded">The soft, binary one — speed above its threshold.</param>
public sealed record AlertState(double PwmIntensity, bool SpeedExceeded)
{
    public static readonly AlertState Quiet = new(0, false);

    public bool PwmAlarming => PwmIntensity > 0;

    public bool Any => PwmAlarming || SpeedExceeded;
}
