namespace WheelTalk.Core.Settings.Device;

/// <summary>
/// Настройки колеса, как оно рассказало о себе одним кадром: ключ → значение и время получения.
/// Неизменяем — снимок едет в телеметрию ссылкой и читается с чужих потоков.
/// <para>
/// Снимок целиком заменяется следующим кадром, а не дополняется: колесо присылает страницу
/// настроек полностью и само (Veteran — раз в 4 секунды), и склейка разных моментов показала бы
/// состояние, которого у колеса не было.
/// </para>
/// </summary>
public sealed class WheelSettingsSnapshot
{
    private readonly Dictionary<string, WheelSettingValue> _values;

    public WheelSettingsSnapshot(DateTimeOffset receivedAt, IEnumerable<KeyValuePair<string, WheelSettingValue>> values)
    {
        ReceivedAt = receivedAt;
        _values = new Dictionary<string, WheelSettingValue>(values, StringComparer.Ordinal);
    }

    /// <summary>Когда пришёл кадр, из которого собран снимок.</summary>
    public DateTimeOffset ReceivedAt { get; }

    /// <summary>Всё, что было в кадре, — включая поля с <see cref="WheelSettingValue.Supported"/> =
    /// <c>false</c>: «настройки нет» — это тоже ответ колеса, и он нужен показу.</summary>
    public IReadOnlyDictionary<string, WheelSettingValue> Values => _values;

    /// <summary>
    /// Значение по ключу. Незнакомый ключ отвечает тем же, чем поле, о котором колесо промолчало, —
    /// <see cref="WheelSettingValue.Missing"/>: у спрашивающего один ответ на «показывать нечего»,
    /// а не два.
    /// </summary>
    public WheelSettingValue this[string key] =>
        _values.TryGetValue(key, out var value) ? value : WheelSettingValue.Missing();
}
