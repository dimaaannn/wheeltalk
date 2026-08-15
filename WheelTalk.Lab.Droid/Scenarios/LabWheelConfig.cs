using WheelTalk.Core.Ports;

namespace WheelTalk.Lab.Droid.Scenarios;

/// <summary>
/// Те же значения, что лежат в <c>appsettings.json</c> приложения. Стенд должен показывать ровно
/// те числа, которые покажет приложение на том же дампе, а числа зависят от этих параметров —
/// декодер по ним считает и напряжение, и проценты заряда, и ШИМ.
/// <para>
/// Перенесено из <c>WheelTalk.Lab/Scenarios/LabWheelConfig.cs</c> без изменений.
/// </para>
/// </summary>
public sealed class LabWheelConfig : IWheelConfig
{
    public string GotwayNegative { get; set; } = "0";
    public bool UseBetterPercents { get; set; }
    public bool HwPwm { get; set; }
    public bool CustomPercents { get; set; }

    /// <summary>Ряд, заданный человеком. У стенда его задаёт ручка — см. <c>LabSettings.CellsInSeries</c>.</summary>
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
    public string InMotionPassword { get; set; } = "000000";
    public int InMotionPollPeriodMs { get; set; } = 250;
}
