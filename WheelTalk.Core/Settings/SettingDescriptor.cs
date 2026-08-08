namespace WheelTalk.Core.Settings;

/// <summary>
/// The four pages, as in the notes. Warnings are their own page rather than a group on another
/// because there are many of them and they matter more than the rest.
/// </summary>
public enum SettingsPage
{
    Wheel,
    Application,
    Display,
    Warnings,
}

/// <summary>What a row looks like and how it is edited.</summary>
public enum SettingKind
{
    /// <summary>On or off.</summary>
    Toggle,

    /// <summary>A number in a range. Several of ours follow WheelLog's "zero turns it off" convention.</summary>
    Number,

    /// <summary>One of a fixed set of stored values. The value is the key, never the translated label.</summary>
    Choice,

    Text,

    /// <summary>
    /// Не значение, а кнопка: строка что-то делает и ничего не хранит. Заведено ради «передать
    /// отладочную информацию» — действию место рядом с настройками, к которым оно относится, а не
    /// на отдельном экране ради одной кнопки.
    /// </summary>
    Action,

    /// <summary>
    /// Ползунок прямо в строке: значение применяется на ходу, пока его ведут, и никуда не пишется.
    /// Заведён ради прослушивания сигнала тревоги — выбранный звук нельзя оценить иначе как услышав,
    /// а число в диалоге его не заменяет.
    /// <para>
    /// Такая строка всегда <see cref="SettingDescriptor.Transient"/> и <b>гаснет, когда со страницы
    /// уходят или её перестраивают</b>: сигнал, оставшийся звучать в кармане, — худшее, что может
    /// сделать экран настроек. Поэтому и <see cref="SettingDescriptor.Current"/> у неё возвращает
    /// ноль: ползунок всегда начинается с тишины.
    /// </para>
    /// </summary>
    Slider,
}

/// <summary>
/// One setting, described rather than drawn. There are about forty of them, and forty repetitions
/// of label-plus-switch would be forty places to look for a typo — so a page renders a list of
/// these instead.
/// <para>
/// The description carries where the value lives (<see cref="Apply"/> and <see cref="Current"/>
/// work on the live options object, the one decoders and the alert engine read), what it looks
/// like, and when it is shown at all. The condition belongs here and not in the page: a setting
/// that only makes sense for one protocol is a fact about the setting.
/// </para>
/// </summary>
public sealed class SettingDescriptor
{
    /// <summary>Stable and configuration-shaped, e.g. <c>WheelConfig:GotwayVoltage</c>. Stored as-is.</summary>
    public required string Key { get; init; }

    public required SettingKind Kind { get; init; }

    /// <summary>
    /// Which of the four pages this belongs on. Not derived from the key: the wheel page carries
    /// both <c>WheelConfig:*</c> and the address, and a page is an editorial decision anyway.
    /// </summary>
    public required SettingsPage Page { get; init; }

    /// <summary>Which collapsible group within the page — a resource key, like the label.</summary>
    public required string SectionKey { get; init; }

    /// <summary>
    /// Настройка для частного случая, а не для всех. Такие уезжают вниз страницы, под общую
    /// черту: скорость раскрута имеет смысл только колесу без аппаратного ШИМ, и стоять она
    /// должна там, где её ищут — отдельно, — а не вперемешку с тем, что настраивают все.
    /// </summary>
    public bool Advanced { get; init; }

    /// <summary>
    /// Resource keys, not text. The stored value must never depend on the language — that is what
    /// broke the original's saved tile layout, by its own admission.
    /// </summary>
    public required string LabelKey { get; init; }

    public string? HintKey { get; init; }

    /// <summary>Puts a stored value into the live options object. Text in, because that is what a layer holds.</summary>
    public required Action<string> Apply { get; init; }

    /// <summary>Reads it back out of the live object, in the same text form.</summary>
    public required Func<string> Current { get; init; }

    /// <summary>
    /// Что сделать <b>после правки человеком</b> — и только после неё. Зовётся из
    /// <see cref="SettingsBinder.Set"/>, то есть с одного-единственного места: когда значение
    /// изменил человек.
    /// <para>
    /// Отдельно от <see cref="Apply"/>, потому что это разные события, сколько бы ни казалось
    /// обратное. <see cref="Apply"/> — восстановление: он зовётся на старте приложения, при смене
    /// слоя, при смене колеса и на любую правку **соседней** настройки. Действие, повешенное туда,
    /// срабатывает, когда человек ничего не делал, — и утаскивает за собой всё, до чего дотянется:
    /// пароль InMotion так поднимал <c>WheelSession</c> в момент старта, до того как встанут
    /// обработчики падений.
    /// </para>
    /// </summary>
    public Action? AfterEdit { get; init; }

    /// <summary>
    /// The wheel reports this and the decoder overwrites it on the first frame — hardware PWM, the
    /// Alexovik firmware flag, the headlight. Shown, never stored: saved as a wheel override it
    /// would come back on its own at the next connection and look like an edit nobody made.
    /// </summary>
    public bool ReportedByWheel { get; init; }

    /// <summary>
    /// Настройка живёт до перезапуска и в слои не пишется. Для того, что человек включает на один
    /// раз и не должен обнаружить включённым завтра: экран, который не гаснет, назавтра — это
    /// разряженный телефон, а не забота.
    /// </summary>
    public bool Transient { get; init; }

    /// <summary>
    /// Настройка, у которой не бывает «этого колеса»: звук, вибрация и вспышка — свойство телефона
    /// и райдера, паузы повтора — приложения, ширина тревожной рамки — экрана. Правка такой
    /// настройки при выбранном колесе завела бы переопределение, а вместе с ним рамку и меню,
    /// объясняющие разницу, которой между двумя колёсами быть не может. Пишется в общий слой
    /// независимо от того, какое колесо выбрано.
    /// </summary>
    public bool GlobalOnly { get; init; }

    /// <summary>
    /// Whether the row is shown right now. Used for the cascades the original has and we want:
    /// alarms off hides everything under them, hardware PWM hides the three numbers the duty cycle
    /// would otherwise be computed from.
    /// </summary>
    public Func<bool>? IsVisible { get; init; }

    public double Minimum { get; init; }

    public double Maximum { get; init; } = 100;

    public double Step { get; init; } = 1;

    /// <summary>
    /// Resource key of the unit shown next to the number — km/h, V, %, seconds. Bounds, step and
    /// the stored text are all in these units and not in whatever the wheel packs them into: one
    /// conversion, in <see cref="Apply"/> and <see cref="Current"/>, or three that drift.
    /// </summary>
    public string? UnitKey { get; init; }

    public int Decimals { get; init; }

    /// <summary>Stored values for <see cref="SettingKind.Choice"/>, in the order they are offered.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>Resource keys for those values, one per choice, in the same order.</summary>
    public IReadOnlyList<string> ChoiceLabelKeys { get; init; } = [];

    public bool Visible => IsVisible is null || IsVisible();
}
