namespace WheelTalk.Core.Contracts;

/// <summary>
/// Settings reported back by the wheel/decoder into <c>IWheelConfig</c> (side "B" —
/// mirrors Android AppConfig.setHwPwm / setLightEnabled calls made from within adapters).
/// </summary>
public sealed record WheelSettings
{
    /// <summary>Forced true once protocol version >= 2 is observed (VeteranAdapter.getVer()).</summary>
    public bool HwPwm { get; init; }

    /// <summary>Toggled by switchFlashlight().</summary>
    public bool LightEnabled { get; init; }
}
