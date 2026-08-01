namespace WheelTalk.Core.Contracts;

/// <summary>
/// Unified telemetry output of <c>Decoder</c>. Raw fields mirror the fixed-point
/// representation used internally by the Android <c>WheelData</c> (1:1 port) —
/// derived "human" properties replicate its <c>get*Double()</c> accessors.
/// </summary>
public sealed record TelemetrySnapshot
{
    /// <summary>Speed, raw fixed-point 1/100 km/h (mirrors WheelData.mSpeed).</summary>
    public int SpeedRaw { get; init; }
    public double SpeedKmh => SpeedRaw / 100.0;

    /// <summary>Voltage, raw fixed-point 1/100 V (mirrors WheelData.mVoltage).</summary>
    public int VoltageRaw { get; init; }
    public double VoltageV => VoltageRaw / 100.0;

    /// <summary>Current, raw fixed-point 1/100 A (mirrors WheelData.mCurrent, derived from PWM * phase current).</summary>
    public int CurrentRaw { get; init; }
    public double CurrentA => CurrentRaw / 100.0;

    /// <summary>Phase (motor) current, raw fixed-point 1/100 A (mirrors WheelData.mPhaseCurrent).</summary>
    public int PhaseCurrentRaw { get; init; }
    public double PhaseCurrentA => PhaseCurrentRaw / 100.0;

    /// <summary>Power, raw fixed-point 1/100 W (mirrors WheelData.mPower, derived from current * voltage).</summary>
    public int PowerRaw { get; init; }
    public double PowerW => PowerRaw / 100.0;

    /// <summary>PWM duty cycle, percent (0..100) — mirrors WheelData.getCalculatedPwm().</summary>
    public double Pwm { get; init; }

    /// <summary>Max PWM duty cycle observed, percent (0..100) — mirrors WheelData.getMaxPwm().</summary>
    public double MaxPwm { get; init; }

    /// <summary>Battery level, percent (0..100).</summary>
    public int Battery { get; init; }

    /// <summary>Temperature raw value as stored by WheelData.mTemperature (Veteran writes it unscaled).</summary>
    public int TemperatureRaw { get; init; }
    /// <summary>Degrees Celsius — mirrors WheelData.getTemperature() (integer division by 100).</summary>
    public int TemperatureC => TemperatureRaw / 100;

    /// <summary>Secondary (motor) temperature raw value, 1/100 degrees — mirrors WheelData.mTemperature2 (Gotway/Begode frame 0x07).</summary>
    public int Temperature2Raw { get; init; }
    public int Temperature2C => Temperature2Raw / 100;

    /// <summary>Top speed observed, raw fixed-point 1/100 km/h (mirrors WheelData.mTopSpeed).</summary>
    public int TopSpeedRaw { get; init; }
    public double TopSpeedKmh => TopSpeedRaw / 100.0;

    /// <summary>Trip distance, meters (mirrors WheelData.mDistance).</summary>
    public long WheelDistance { get; init; }
    public double WheelDistanceKm => WheelDistance / 1000.0;

    /// <summary>Odometer total, meters (mirrors WheelData.mTotalDistance).</summary>
    public long TotalDistance { get; init; }
    public double TotalDistanceKm => TotalDistance / 1000.0;

    /// <summary>Distance since this session started, meters (mTotalDistance - mStartTotalDistance).</summary>
    public long DistanceFromStart { get; init; }
    public double DistanceFromStartKm => DistanceFromStart / 1000.0;

    /// <summary>Pitch angle, degrees (mirrors WheelData.mAngle — already a double at decode time).</summary>
    public double Angle { get; init; }

    /// <summary>Charging status raw code (mirrors WheelData.mChargingStatus).</summary>
    public int ChargingStatus { get; init; }

    /// <summary>Auto-off / sleep timer, seconds (mirrors WheelData.mSleepTimer).</summary>
    public int SleepTimerSec { get; init; }

    /// <summary>Wheel-reported hard alarm (Gotway/Begode frame 0x04, bit 0) — mirrors WheelData.getWheelAlarm().</summary>
    public bool WheelAlarm { get; init; }

    /// <summary>Latest decoded alert/news line (Gotway/Begode frame 0x04) — mirrors WheelData.getAlert().</summary>
    public string Alert { get; init; } = "";

    /// <summary>Firmware version string, e.g. "006.0.00".</summary>
    public string Version { get; init; } = "";

    /// <summary>Model name derived from protocol version (e.g. "Sherman L").</summary>
    public string Model { get; init; } = "";

    public WheelType WheelType { get; init; } = WheelType.Veteran;

    public SmartBms Bms1 { get; init; } = new();
    public SmartBms Bms2 { get; init; } = new();

    // KingSong-only fields — Veteran/Gotway never write WheelState's backing properties, so these
    // stay at their defaults ("", 0) for those protocols.

    /// <summary>Bluetooth-advertised name, e.g. "KS-S18-0205" (mirrors WheelData.mName) — KingSong only.</summary>
    public string Name { get; init; } = "";

    /// <summary>Wheel-reported ride mode as a decimal string (mirrors WheelData.mModeStr) — KingSong only.</summary>
    public string ModeStr { get; init; } = "";

    /// <summary>Fan status code (mirrors WheelData.mFanStatus) — KingSong only.</summary>
    public int FanStatus { get; init; }

    /// <summary>Motor-controller CPU load, percent (mirrors WheelData.mCpuLoad) — KingSong only.</summary>
    public int CpuLoad { get; init; }

    /// <summary>Wheel-configured speed limit, km/h (mirrors WheelData.mSpeedLimit) — KingSong only.</summary>
    public double SpeedLimit { get; init; }

    /// <summary>Serial number (mirrors WheelData.mSerialNumber) — KingSong only.</summary>
    public string Serial { get; init; } = "";

    /// <summary>Raw output value (KingSong's cpu-load frame, hwPwm×100) — mirrors WheelData.mOutput.</summary>
    public int OutputRaw { get; init; }
    /// <summary>Integer percent — mirrors WheelData.getOutput() (integer division by 100).</summary>
    public int Output => OutputRaw / 100;

    // InMotion-only fields — Veteran/Gotway/KingSong never write WheelState's backing properties,
    // so these stay at their defaults (0) for those protocols.

    /// <summary>Lean/roll angle, degrees — mirrors WheelData.mRoll — InMotion only.</summary>
    public double Roll { get; init; }

    /// <summary>IMU temperature, raw signed byte value, unscaled (unlike TemperatureRaw/Temperature2Raw,
    /// which are 1/100 degrees) — mirrors WheelData.mImuTemp — InMotion only.</summary>
    public int ImuTemp { get; init; }

    // InMotion V2-only fields — mirror WheelData.mTorque/mMotorPower/mCpuTemp/mCurrentLimit.

    /// <summary>Motor torque, N·m — mirrors WheelData.mTorque.</summary>
    public double Torque { get; init; }

    /// <summary>Motor power, W — mirrors WheelData.mMotorPower.</summary>
    public double MotorPower { get; init; }

    /// <summary>Motor-controller CPU temperature, degrees C, unscaled — mirrors WheelData.mCpuTemp.</summary>
    public int CpuTemp { get; init; }

    /// <summary>Wheel-reported dynamic current limit, A — mirrors WheelData.mCurrentLimit.</summary>
    public double CurrentLimit { get; init; }
}
