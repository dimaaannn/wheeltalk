namespace WheelTalk.Core.Settings.Device;

/// <summary>
/// Одна настройка, как её сообщило колесо: сырой байт, разобранное значение и признак «эта
/// настройка у колеса есть».
/// <para>
/// Сырой байт хранится рядом с числом намеренно. Во-первых, признак «настройки нет» у Veteran —
/// это конкретное значение байта (<c>0x80</c>), и решать по нему можно только до преобразования:
/// у знакового поля <c>0x80</c> — законное −128 (план 34 §10, капкан К2). Во-вторых, в дампе
/// диагностики полезно видеть именно то, что пришло, а не то, во что мы это истолковали.
/// </para>
/// </summary>
public readonly record struct WheelSettingValue
{
    private WheelSettingValue(byte? raw, int value, bool supported)
    {
        Raw = raw;
        Value = value;
        Supported = supported;
    }

    /// <summary>Байт, как он пришёл. <c>null</c> — кадр кончился раньше этого поля.</summary>
    public byte? Raw { get; }

    /// <summary>Значение после разбора. При <see cref="Supported"/> = <c>false</c> смысла не имеет.</summary>
    public int Value { get; }

    /// <summary>Колесо сообщило значение этой настройки.</summary>
    public bool Supported { get; }

    /// <summary>Колесо сообщило значение.</summary>
    public static WheelSettingValue Reported(byte raw, int value) => new(raw, value, true);

    /// <summary>Значения нет: либо колесо прислало сентинел, либо кадр до поля не дотянулся.</summary>
    public static WheelSettingValue Missing(byte? raw = null) => new(raw, 0, false);
}
