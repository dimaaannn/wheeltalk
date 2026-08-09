namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// One button in a <see cref="QuickSheet"/> menu — the "состав" half of quick-commands-design.md
/// §6.1 («меню — декларативный список команд: подпись/иконка, чтение состояния, действие,
/// доступность»). <see cref="QuickSheet"/> owns everything about how the sheet behaves; a menu is
/// nothing but a list of these plus a call point, so a second menu is a second list, not a second
/// component.
/// </summary>
public sealed class QuickSheetCommand
{
    /// <summary>Big glyph drawn above the label — a single character/emoji, no drawable resources needed for one row of buttons.</summary>
    public required string Icon { get; init; }

    /// <summary>
    /// К какой смысловой стайке команда относится: колесо-сейчас, запись, связь, телефон
    /// (план 25 §2, шаг 3). Шторка не знает, что эти слова значат, — она лишь ставит разделитель
    /// там, где стайка сменилась: искать в группе из двух-трёх быстрее, чем в ряду из семи.
    /// <para>
    /// Пусто у всех — разделителей нет вовсе, и ряд выглядит как до группировки. Имена придумывает
    /// хозяин меню: слова про колесо и телефон библиотеке не принадлежат.
    /// </para>
    /// </summary>
    public string Group { get; init; } = "";

    /// <summary>
    /// Read on every render rather than captured once, so a button whose wording depends on state
    /// (light "Фара"/"Фара вкл", record "Запись"/"Стоп") always shows the current word without the
    /// sheet needing to know why it changed.
    /// </summary>
    public required Func<string> Label { get; init; }

    /// <summary>Null when the command has no on/off state to reflect (beep).</summary>
    public Func<bool>? IsOn { get; init; }

    /// <summary>Null means always available. False greys the button out and drops its click handler.</summary>
    public Func<bool>? IsEnabled { get; init; }

    /// <summary>
    /// Runs the command and reports its fate through the task: completing means delivered/applied,
    /// faulting means it was not — that fate is what the post-tap highlight shows (design doc §5).
    /// Wheel commands fault on real delivery failure (the write queue's confirmed outcome); local
    /// actions (record, reset peaks) are synchronous and always "succeed".
    /// </summary>
    public required Func<Task> Action { get; init; }

    /// <summary>
    /// A second, optional behaviour for the same button — plan 23 §5.8's "second way in" for the
    /// record command: a long press opens the recording screen while a short tap keeps doing what it
    /// always did. Null means the button has none; runs through the same highlight/autohide path as
    /// <see cref="Action"/>, it is only the gesture that differs.
    /// </summary>
    public Func<Task>? LongPress { get; init; }
}
