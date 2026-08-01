namespace WheelTalk.Core.Ports;

/// <summary>
/// Typed access to behavior parameters (side "A", read by decoders) and reported/derived
/// wheel settings (side "B", written by decoders) — replaces Android's string-keyed AppConfig.
/// Shared across protocols (Veteran, Gotway/Begode); each decoder reads only what it needs.
/// Defaults are bound from appsettings.json ("WheelConfig" section).
/// </summary>
public interface IWheelConfig
{
    // (A) behavior — read by VeteranDecoder / GotwayDecoder
    /// <summary>"0" = abs(speed/current), "1"/"-1" = sign multiplier (AppConfig.getGotwayNegative()).</summary>
    string GotwayNegative { get; }
    bool UseBetterPercents { get; }
    bool HwPwm { get; set; }

    // derived-calculation parameters (WheelState.SetBatteryLevel / CalculatePwm)
    bool CustomPercents { get; }
    /// <summary>Cell voltage tiltback, 1/100 V (e.g. 330 = 3.30 V).</summary>
    int CellVoltageTiltback { get; }
    /// <summary>Rotation speed reference, 1/10 km/h.</summary>
    int RotationSpeed { get; }
    /// <summary>Rotation voltage reference, 1/10 V.</summary>
    int RotationVoltage { get; }
    /// <summary>Power factor, 1/100.</summary>
    int PowerFactor { get; }

    // (A) Gotway/Begode-specific — read by GotwayDecoder
    /// <summary>Scales distance/speed by 0.875 for older 12" wheels (AppConfig.useRatio).</summary>
    bool UseRatio { get; }
    /// <summary>Trust the wheel-reported battery voltage (frame 0x01) over the scaled frame-0x00 value.</summary>
    bool AutoVoltage { get; }
    /// <summary>Cell-count code "0".."6" (16/20/24/32/32/40/36 cells) — selects the voltage scaler and pack size.</summary>
    string GotwayVoltage { get; }

    // (A) InMotion-specific — read by InMotionDecoder
    /// <summary>6-digit wheel-layer PIN (AppConfig.passwordForWheel) — sent on connect, six times, before the wheel answers with telemetry.</summary>
    string InMotionPassword { get; }

    // (B) reported/derived — written by decoders
    bool LightEnabled { get; set; }
    /// <summary>Set true once the connected Begode firmware is identified as Alexovik's SmirnoV custom firmware ("BF" handshake).</summary>
    bool IsAlexovikFW { get; set; }
}
