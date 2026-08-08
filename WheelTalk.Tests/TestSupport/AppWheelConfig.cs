using WheelTalk.Core.Ports;

namespace WheelTalk.Tests.TestSupport;

/// <summary>
/// POCO <see cref="IWheelConfig"/> для тестов — со значениями по умолчанию из оригинала, теми же,
/// что стоят в заводских `appsettings.json`.
/// <para>
/// Своя копия у каждого хоста — так же, как у приложения
/// (`WheelTalk.Droid/Configuration/AppWheelConfig.cs`) и у консольной песочницы: контракт
/// <see cref="IWheelConfig"/> живёт в ядре, а откуда берутся значения — дело хоста. Раньше тесты
/// брали копию консоли через ссылку на её проект, и из-за этого собирались под
/// `net10.0-windows`; своя копия эту ссылку сняла.
/// </para>
/// <para>
/// Сеттеры нужны: декодеры пишут сюда то, что сообщило колесо (HwPwm, LightEnabled,
/// IsAlexovikFW), поэтому экземпляр раздаётся один и тот же, а не копией.
/// </para>
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
}
