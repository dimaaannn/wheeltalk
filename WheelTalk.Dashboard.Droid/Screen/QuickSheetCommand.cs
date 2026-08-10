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
    /// <summary>
    /// Значок над подписью — номер vector drawable, а не буква эмодзи (<see cref="QuickIcons"/>).
    /// Шрифтовому знаку мы не могли задать ни цвет, ни толщину, а один и тот же «📊» доставался
    /// двум разным делам (план 25 §1); свой контур подчиняется кнопке и уникален по построению.
    /// </summary>
    public required int Icon { get; init; }

    /// <summary>
    /// К какому разделу шторки команда относится: колесо · поездка · телефон (план 32 §1, этап 4).
    /// Раздел — это строка кнопок с боковым корешком, и порядок разделов задаёт порядок команд в
    /// списке: соседи по разделу стоят подряд.
    /// <para>
    /// Шторка не знает, что эти слова значат: ключ ей нужен только чтобы понять, где раздел
    /// сменился, а подпись корешка она спрашивает у хозяина меню
    /// (<see cref="QuickSheet.SectionLabel"/>) — слова про колесо и телефон библиотеке не
    /// принадлежат. Пусто у всех — раздел один, безымянный, и ряд выглядит как до разделов.
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
