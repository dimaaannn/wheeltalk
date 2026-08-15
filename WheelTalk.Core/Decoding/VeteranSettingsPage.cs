using WheelTalk.Core.Settings.Device;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Страница 8 кадра Veteran — настройки колеса. Не порт: у оригинала на этом месте стоит расписка
/// «new packet, not yet recognized», разбирает эту страницу только родное приложение
/// производителя (<c>BtManager.java:415-548</c> → <c>ControlSettingData</c>).
/// <para>
/// Колесо шлёт страницу само, раз в 4 секунды, и ничего не спрашивает: разбор — чистая функция от
/// кадра, без состояния и без часов (время получения передаёт вызывающий, чтобы снимок оставался
/// воспроизводимым в тестах).
/// </para>
/// <para>
/// Номер страницы здесь не проверяется: по нему кадр уже разослан ветками
/// <c>VeteranDecoder.DecodeSmartBms</c>, и вторая такая же проверка ничего бы не поймала.
/// </para>
/// </summary>
public static class VeteranSettingsPage
{
    /// <summary>Номер страницы в байте 46 кадра — тот же отсчёт, что у <c>VeteranDecoder.Decode</c>.</summary>
    public const int PageNumber = 8;

    /// <summary>«Такой настройки у этого колеса нет» (<c>ControlSettingData.java:5</c>). Родное
    /// приложение по одному этому байту прячет строку, по каждому полю отдельно.
    /// <para>
    /// Открыт наружу, потому что тем же байтом говорит «нет» и режим езды из кадра телеметрии
    /// (байт 31): Sherman L шлёт там <c>0x80</c> во всех 597 кадрах записи 28.07.2026, а жёсткость
    /// педалей сообщает этой страницей. Один смысл — одно число, и второго такого в коде быть не
    /// должно.
    /// </para></summary>
    public const byte NoSuchSetting = 0x80;

    /// <summary>
    /// Раскладка страницы (план 34 §1.4), по возрастанию индекса. Знаковое поле ровно одно —
    /// поправка напряжения: у неё законны и −15, и 15, у остальных пятнадцати диапазоны
    /// неотрицательные.
    /// </summary>
    private static readonly (int Index, string Key, bool Signed)[] Layout =
    [
        (50, WheelSettingKeys.PedalHardness, false),
        (52, WheelSettingKeys.StopSpeed, false),
        (53, WheelSettingKeys.StopPowerRate, false),
        (55, WheelSettingKeys.ScreenBacklightRate, false),
        (56, WheelSettingKeys.Gyro, false),
        (57, WheelSettingKeys.TransportMode, false),
        (58, WheelSettingKeys.Unit, false),
        (59, WheelSettingKeys.Vol, true),
        (60, WheelSettingKeys.LowVolMode, false),
        (61, WheelSettingKeys.HighSpeedMode, false),
        (63, WheelSettingKeys.KeyTone, false),
        (64, WheelSettingKeys.MaxChargeVol, false),
        (65, WheelSettingKeys.MaxChargeVolBase, false),
        (66, WheelSettingKeys.UpOrDownSpeedHelper, false),
        (68, WheelSettingKeys.UpSpeedCul, false),
        (69, WheelSettingKeys.BrakePressureAlarm, false),
    ];

    /// <summary>
    /// Разбирает кадр страницы 8 в снимок. <c>null</c> — кадр не дотянулся даже до первого поля,
    /// то есть сказать о настройках нечего вовсе; снимок из одних пустых мест был бы хуже, чем
    /// прежний снимок, оставшийся на месте.
    /// </summary>
    public static WheelSettingsSnapshot? Parse(byte[] frame, DateTimeOffset receivedAt)
    {
        if (frame.Length <= Layout[0].Index) return null;

        var values = new List<KeyValuePair<string, WheelSettingValue>>(Layout.Length);
        foreach ((int index, string key, bool signed) in Layout)
        {
            values.Add(new KeyValuePair<string, WheelSettingValue>(key, Read(frame, index, signed)));
        }

        return new WheelSettingsSnapshot(receivedAt, values);
    }

    /// <summary>
    /// Одно поле. Прошивка вправе прислать кадр короче раскладки — тогда о поле просто не сказано
    /// (план 34 §10, капкан К5), и это не ошибка кадра.
    /// <para>
    /// Сентинел сверяется <b>с сырым байтом, до преобразования</b>: у знакового поля <c>0x80</c> —
    /// законное −128, и проверка после преобразования спутала бы одно с другим (капкан К2).
    /// </para>
    /// </summary>
    private static WheelSettingValue Read(byte[] frame, int index, bool signed)
    {
        if (index >= frame.Length) return WheelSettingValue.Missing();

        byte raw = frame[index];
        if (raw == NoSuchSetting) return WheelSettingValue.Missing(raw);

        return WheelSettingValue.Reported(raw, signed ? (sbyte)raw : (int)raw);
    }
}
