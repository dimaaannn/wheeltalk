namespace WheelTalk.Core.Settings.Device;

/// <summary>
/// Ключи настроек, которые колесо сообщает о себе само. Строки, а не перечисление: ключ —
/// это имя поля чужого протокола, и он должен пережить встречу с настройкой, которой у нас ещё
/// нет, — новая марка добавит ключ, не тронув наш тип.
/// <para>
/// Имена — производителя (<c>ControlSettingData.java</c>; раскладка и диапазоны сведены в
/// <c>docs/android-plan-34-wheel-settings.md</c> §1.4): так строка снимка сверяется с чужим
/// исходником без перевода. Подписей для человека здесь нет — показ живёт в Droid.
/// </para>
/// </summary>
public static class WheelSettingKeys
{
    /// <summary>Жёсткость педалей, 0..100.</summary>
    public const string PedalHardness = "pedalHardness";

    /// <summary>Скорость отклонения назад (tiltback), 10..120; 200 — выключено.</summary>
    public const string StopSpeed = "stopSpeed";

    /// <summary>Порог ШИМ, при котором колесо начинает откидывать, 30..100.</summary>
    public const string StopPowerRate = "stopPowerRate";

    /// <summary>Яркость экрана колеса, 0..100.</summary>
    public const string ScreenBacklightRate = "screenBacklightRate";

    /// <summary>Состояние калибровки гироскопа, 0/1/2.</summary>
    public const string Gyro = "gyro";

    /// <summary>Режим перевозки, 0/1.</summary>
    public const string TransportMode = "transportMode";

    /// <summary>Единицы измерения: 0 — километры, 1 — мили.</summary>
    public const string Unit = "unit";

    /// <summary>Поправка напряжения, −15..15. <b>Единственное знаковое поле страницы.</b></summary>
    public const string Vol = "vol";

    /// <summary>Режим низкого напряжения, 0/1.</summary>
    public const string LowVolMode = "lowVolMode";

    /// <summary>Скоростной режим, 0/1.</summary>
    public const string HighSpeedMode = "highSpeedMode";

    /// <summary>Громкость звука клавиш, 0..100.</summary>
    public const string KeyTone = "keyTone";

    /// <summary>Напряжение конца заряда, 0..120.</summary>
    public const string MaxChargeVol = "maxChargeVol";

    /// <summary>Опора расчёта напряжения заряда, по умолчанию 145.</summary>
    public const string MaxChargeVolBase = "maxChargeVolBase";

    /// <summary>Помощь при подъёме и спуске, 0..100.</summary>
    public const string UpOrDownSpeedHelper = "upOrDownSpeedHelper";

    /// <summary>Ускорение подъёма, 0..100.</summary>
    public const string UpSpeedCul = "upSpeedCul";

    /// <summary>Тревога по давлению тормоза, 80..125.</summary>
    public const string BrakePressureAlarm = "brakePressureAlarm";
}
