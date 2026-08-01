namespace WheelTalk.Core.Contracts;

/// <summary>
/// Generic command contract (input) — discriminated union standing in for the 41
/// <c>open fun</c> methods of Android <c>BaseAdapter</c>. Only the variants actually
/// implemented by the Veteran slice (§5.5 of the port plan) are handled by
/// <c>VeteranDecoder</c>; everything else is a no-op for now.
/// </summary>
public abstract record WheelCommand
{
    public sealed record Beep : WheelCommand;
    public sealed record SetLight(bool Enabled) : WheelCommand;
    public sealed record SwitchFlashlight : WheelCommand;
    public sealed record SetPedalsMode(int Mode) : WheelCommand;
    public sealed record ResetTrip : WheelCommand;
    public sealed record Calibrate : WheelCommand;
}
