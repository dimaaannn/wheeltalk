using WheelTalk.Core.Alerts;

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

    /// <summary>
    /// Полоса тревоги системным окном поверх ЧУЖИХ приложений — не оригинала, решение владельца
    /// 11.08.2026 (<see cref="WheelTalk.Droid.Alerts.SystemAlertOverlay"/>). Выключено по умолчанию:
    /// это единственный канал здесь, спрашивающий у системы разрешение «поверх других приложений», и
    /// давать его без спроса неправильно — включает райдер сам, разрешение запрашивается тогда же.
    /// </summary>
    public bool OverlayOtherApps { get; set; }

    /// <summary>
    /// Каким сигналом звучит тревога. Здесь, а рядом с каналами, потому что это свойство телефона и
    /// райдера, а не колеса: динамик, карман и шлем одни и те же, на чём бы человек ни ехал.
    /// Заводской — первый выбор владельца на слух (план 26).
    /// </summary>
    public AlarmWave Wave { get; set; } = AlarmWave.TwoToneStack;
}
