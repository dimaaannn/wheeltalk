using WheelTalk.Core.Ports;

namespace WheelTalk.Configuration;

/// <summary>
/// POCO <see cref="IWheelConfig"/> bound from the "WheelTalk:WheelConfig" section of
/// appsettings.json (see <see cref="WheelTalkOptions"/>). Property setters allow the
/// (B) "reported" writes decoders make at runtime (HwPwm, LightEnabled, IsAlexovikFW),
/// so the DI container must hand out one shared mutable instance, never a copy.
/// </summary>
public sealed class AppWheelConfig : IWheelConfig
{
    public string GotwayNegative { get; set; } = "0";
    public bool UseBetterPercents { get; set; }
    public bool HwPwm { get; set; }
    public bool CustomPercents { get; set; }
    public int CellsInSeries { get; set; }
    public int CellVoltageTiltback { get; set; } = 330;
    public int RotationSpeed { get; set; } = 500;
    public int RotationVoltage { get; set; } = 840;
    public int PowerFactor { get; set; } = 90;
    public bool LightEnabled { get; set; }

    public bool UseRatio { get; set; }
    public bool AutoVoltage { get; set; } = true;
    public string GotwayVoltage { get; set; } = "1";
    public bool IsAlexovikFW { get; set; }

    // Six zero digits rather than the original's empty-string default (AppConfig.passwordForWheel
    // only zero-pads to six digits when *saved*, so an app that never opened the setting UI has an
    // empty string here) — CANMessage.getPassword indexes password[0..5] unconditionally, and an
    // empty string would throw. Six zeros is a wheel that never got its owner's real PIN, not a
    // crash.
    public string InMotionPassword { get; set; } = "000000";
    public int InMotionPollPeriodMs { get; set; } = 250;
}
