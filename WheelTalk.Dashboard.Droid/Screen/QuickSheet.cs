using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Reusable "quick sheet" mechanics — quick-commands-design.md, the shared half of §6.1: call by
/// tap/swipe-up from an edge zone, ≤150 ms slide animation, autohide (after a command with a brief
/// delayed highlight of its fate — §5; on an outside tap; after 5 s idle), the pin, the grabber.
/// A menu is a declarative <see cref="QuickSheetCommand"/> list handed in through
/// <see cref="SetCommands"/> — a second menu is a second <see cref="QuickSheet"/> instance with its
/// own list and its own call point (§6.1: "у каждого меню своя точка вызова"), not a copy of this
/// class.
/// <para>
/// Lives as a full-bleed overlay: hidden, it is <see cref="ViewStates.Gone"/> and touches nothing
/// (§6: "пока шторка скрыта, она не рисуется и кадр панели не трогает"); shown, its own background
/// is the "tap outside closes it" scrim and its content pane — bottom-aligned, wrap-content height —
/// is the sheet itself.
/// </para>
/// <para>
/// Слова — забота вызывающего, как у <c>DashboardView.LinkText</c>: у приложения они переводимые,
/// у стенда свои, поэтому подпись булавки приходит через <see cref="PinLabel"/>, а не из ресурсов.
/// </para>
/// </summary>
public sealed class QuickSheet : FrameLayout
{
    private static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(5);

    /// <summary>"Задержка на долю секунды" — design doc §5's honesty window: long enough to read the highlight, short enough not to feel stuck (§ "на ходу длинные анимации раздражают").</summary>
    private static readonly TimeSpan PostCommandHideDelay = TimeSpan.FromMilliseconds(450);

    private const int AnimationMs = 150;
    private static readonly Color SuccessColor = Color.ParseColor("#1B5E20");
    private static readonly Color FailureColor = Color.ParseColor("#B00020");
    private static readonly Color OnColor = Color.ParseColor("#FFA000");

    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly Action _hideNow;
    private readonly LinearLayout _content;
    /// <summary>
    /// Полоса кнопок: одна строка, пока команды помещаются, иначе — перенос по измеренной ширине
    /// (<see cref="Place"/>). Семь кнопок не влезали в 360 dp, и последняя («Настройки») просто
    /// уезжала за край — найдено 30.07.2026, когда добавилась кнопка «Не гасить». Прокрутку не
    /// берём: искать команду пальцем на ходу хуже, чем увидеть все сразу.
    /// </summary>
    private readonly LinearLayout _rows;

    private LinearLayout _row = null!;

    /// <summary>Минимальная сторона кнопки — она же цель касания (quick-commands-design.md §3).</summary>
    private const int ButtonWidthDp = 56;

    private IReadOnlyList<QuickSheetCommand> _commands = [];
    private bool _pinned;
    private bool _visible;

    public QuickSheet(Context context) : base(context)
    {
        _hideNow = Hide;
        Visibility = ViewStates.Gone;

        // Тап по всему, что не сама шторка, — «мимо» (§5): закрывает всегда, булавка защищает
        // только автоскрытие, а не ручное закрытие.
        Clickable = true;
        Click += (_, _) => Hide();

        _rows = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        _rows.SetPadding(context.Dp(8), 0, context.Dp(8), context.Dp(12));

        _content = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical, Clickable = true };
        _content.SetBackgroundColor(context.PageBackground());
        _content.AddView(BuildGrabber(context), new LinearLayout.LayoutParams(context.Dp(32), context.Dp(4))
        {
            Gravity = GravityFlags.CenterHorizontal,
            TopMargin = context.Dp(8),
            BottomMargin = context.Dp(8),
        });
        _content.AddView(_rows, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        AddView(_content, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { Gravity = GravityFlags.Bottom });
    }

    public bool IsOpen => _visible;

    /// <summary>Подпись булавки — единственное слово, которое шторка показывает сама.</summary>
    public Func<string> PinLabel { get; set; } = () => "Pin";

    /// <summary>
    /// The menu's composition — set once for the life of the screen. Positions are fixed by list
    /// order (design doc §3: "позиции фиксированы навсегда"); the pin is not in this list, it is
    /// mechanics the sheet always appends last on its own.
    /// </summary>
    public void SetCommands(IReadOnlyList<QuickSheetCommand> commands)
    {
        _commands = commands;
        RebuildRow();
    }

    public void Show()
    {
        if (_visible) return;
        _visible = true;
        RebuildRow();
        Visibility = ViewStates.Visible;
        AnimateIn();
        if (!_pinned) ScheduleTimeout();
    }

    public void Hide()
    {
        if (!_visible) return;
        _visible = false;
        _handler.RemoveCallbacks(_hideNow);
        AnimateOut();
    }

    public void Toggle()
    {
        if (_visible) Hide(); else Show();
    }

    /// <summary>Конец поездки снимает булавку — design doc §3: «действует до … конца поездки».</summary>
    public void Unpin()
    {
        if (!_pinned) return;
        _pinned = false;
        if (_visible) RebuildRow();
    }

    /// <summary>
    /// A bare <see cref="View"/> (not a <see cref="ViewGroup"/>) does not size itself from content —
    /// unlike widgets such as <see cref="TextView"/>, it has no <c>onMeasure</c> override, so the
    /// platform default (<c>View.getDefaultSize</c>) resolves <c>WrapContent</c> under an
    /// <c>AT_MOST</c> constraint to the *entire* available bound, not to any intrinsic size — the
    /// grabber briefly ballooned to fill the whole sheet this way, squeezing the button row to zero
    /// height. Every call site must therefore give this view its exact 32×4 dp size directly in the
    /// <see cref="ViewGroup.LayoutParams"/> it adds it with, never <c>WrapContent</c>.
    /// </summary>
    private static View BuildGrabber(Context context)
    {
        var grabber = new View(context);
        var background = new GradientDrawable();
        background.SetShape(ShapeType.Rectangle);
        background.SetCornerRadius(context.Dp(2));
        background.SetColor(Color.ParseColor("#66888888"));
        grabber.Background = background;
        return grabber;
    }

    private void RebuildRow()
    {
        _rows.RemoveAllViews();
        StartRow();

        int available = Context!.Resources!.DisplayMetrics!.WidthPixels - Context.Dp(16);
        int used = 0;

        for (int i = 0; i < _commands.Count; i++)
        {
            int index = i;
            var command = _commands[i];
            var button = BuildButton(command);
            if (command.IsEnabled?.Invoke() ?? true)
            {
                button.Click += (_, _) => OnCommandTapped(index, command);
            }

            Place(button, available, ref used);
        }

        // Булавка идёт тем же путём, что и команды, а не приписывается в конец: отдельный путь и
        // был тем, из-за чего её уносило за край, когда команды заполняли строку ровно.
        Place(BuildPinButton(), available, ref used);
    }

    /// <summary>
    /// Кладёт кнопку в текущую строку или начинает новую, если она туда уже не влезает.
    /// <para>
    /// Ширину спрашиваем у самой кнопки, а не считаем по <see cref="ButtonWidthDp"/>: 56 dp — это
    /// её минимум, а настоящая ширина растёт с подписью, и «Закрепить панель» вдвое шире «Бипа».
    /// Расчёт по минимуму врал в большую сторону — «столько влезет» оказывалось на кнопку больше,
    /// чем помещается, и последняя уезжала за край. Тем же он ломался бы от длинного перевода и от
    /// увеличенного шрифта в системных настройках.
    /// </para>
    /// </summary>
    private void Place(View button, int available, ref int used)
    {
        int unspecified = MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified);
        button.Measure(unspecified, unspecified);

        int width = button.MeasuredWidth;
        int gap = _row.ChildCount > 0 ? Context!.Dp(8) : 0;

        if (_row.ChildCount > 0 && used + gap + width > available)
        {
            StartRow();
            used = 0;
            gap = 0;
        }

        used += gap + width;
        AddSpaced(button);
    }

    private View BuildButton(QuickSheetCommand command)
    {
        bool enabled = command.IsEnabled?.Invoke() ?? true;
        bool on = command.IsOn?.Invoke() ?? false;

        var context = Context!;
        var button = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        button.SetGravity(GravityFlags.Center);
        button.SetMinimumWidth(context.Dp(ButtonWidthDp));
        button.SetMinimumHeight(context.Dp(ButtonWidthDp));
        int pad = context.Dp(4);
        button.SetPadding(pad, pad, pad, pad);
        button.Alpha = enabled ? 1f : 0.4f;

        var background = new GradientDrawable();
        background.SetShape(ShapeType.Rectangle);
        background.SetCornerRadius(context.Dp(10));
        background.SetColor(on ? OnColor : NormalColor(context));
        button.Background = background;

        var icon = new TextView(context) { Text = command.Icon, Gravity = GravityFlags.Center };
        icon.SetTextSize(ComplexUnitType.Sp, 20);
        icon.SetTextColor(Color.White);
        button.AddView(icon);

        var label = new TextView(context) { Text = command.Label(), Gravity = GravityFlags.Center };
        label.SetTextSize(ComplexUnitType.Sp, 10);
        label.SetTextColor(Color.White);
        button.AddView(label);

        return button;
    }

    private View BuildPinButton()
    {
        var button = BuildButton(new QuickSheetCommand
        {
            Icon = "📌",
            Label = PinLabel,
            IsOn = () => _pinned,
            Action = () => Task.CompletedTask,
        });
        button.Click += (_, _) => OnPinTapped();
        return button;
    }

    private static Color NormalColor(Context context) =>
        context.IsDarkTheme() ? Color.ParseColor("#AC99EA") : Color.ParseColor("#512BD4");

    /// <summary>
    /// Wrap-content width, not weight-based 0dp+weight: a horizontal <see cref="LinearLayout"/> with
    /// weighted zero-width children measured its own wrap-content height as zero here — a known
    /// Android quirk (the weighted second pass does not feed back into the row's own height). Fixed
    /// widths sidestep it and cost nothing — the row is centered, not stretched to fill.
    /// </summary>
    /// <summary>
    /// Кнопка по её номеру среди команд — не по номеру внутри строки: после переноса они разошлись,
    /// и подсветка «команда не ушла» светила бы соседа.
    /// </summary>
    private View? ButtonAt(int index)
    {
        for (int r = 0; r < _rows.ChildCount; r++)
        {
            if (_rows.GetChildAt(r) is not LinearLayout row) continue;
            if (index < row.ChildCount) return row.GetChildAt(index);
            index -= row.ChildCount;
        }

        return null;
    }

    private void StartRow()
    {
        _row = new LinearLayout(Context!) { Orientation = Android.Widget.Orientation.Horizontal };
        _row.SetGravity(GravityFlags.CenterHorizontal);
        _rows.AddView(_row, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = _rows.ChildCount > 0 ? Context!.Dp(8) : 0,
        });
    }

    private void AddSpaced(View view)
    {
        var p = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        if (_row.ChildCount > 0) p.LeftMargin = Context!.Dp(8);
        _row.AddView(view, p);
    }

    /// <summary>
    /// Runs the command, then holds its fate on screen before deciding what happens next — the
    /// honesty window design doc §5 asks for: the button that just fired is the one that gets
    /// highlighted, so it is rebuilt into the row before the highlight (not after — a highlight
    /// applied to a view about to be replaced would never be seen) and looked up by its stable index.
    /// </summary>
    private async void OnCommandTapped(int index, QuickSheetCommand command)
    {
        _handler.RemoveCallbacks(_hideNow);

        bool success;
        try
        {
            await command.Action();
            success = true;
        }
        catch
        {
            success = false;
        }

        RebuildRow();
        Highlight(ButtonAt(index), success);

        if (_pinned)
        {
            _handler.PostDelayed(RebuildRow, (long)PostCommandHideDelay.TotalMilliseconds);
            return;
        }

        _handler.PostDelayed(_hideNow, (long)PostCommandHideDelay.TotalMilliseconds);
    }

    private void OnPinTapped()
    {
        _handler.RemoveCallbacks(_hideNow);
        _pinned = !_pinned;
        RebuildRow();
        if (!_pinned) ScheduleTimeout();
    }

    private static void Highlight(View? view, bool success)
    {
        if (view is not { Background: GradientDrawable background }) return;
        background.SetColor(success ? SuccessColor : FailureColor);
    }

    private void ScheduleTimeout()
    {
        _handler.RemoveCallbacks(_hideNow);
        _handler.PostDelayed(_hideNow, (long)InactivityTimeout.TotalMilliseconds);
    }

    private ViewTreeObserver.IOnGlobalLayoutListener? _pendingReveal;

    /// <summary>
    /// Slides the sheet up from below its own height. The height is only known after layout, so the
    /// starting offset is set from a post-layout callback rather than guessed.
    /// <para>
    /// Cancels whatever <see cref="_content"/>'s animator was in the middle of first: a rapid
    /// close-then-reopen (auto-hide firing right as the call zone is tapped again, or a pinned
    /// command's delayed <see cref="RebuildRow"/> landing mid-animation) otherwise leaves
    /// <see cref="AnimateOut"/>'s queued end action free to fire later and yank the sheet back to
    /// <see cref="ViewStates.Gone"/> after this call already set it <see cref="ViewStates.Visible"/> —
    /// the shutter closing or reopening on its own that plan 11's field pass found.
    /// </para>
    /// </summary>
    private void AnimateIn()
    {
        CancelPendingReveal();
        _content.Animate()?.Cancel();
        _content.TranslationY = 0;
        var observer = _content.ViewTreeObserver;
        ViewTreeObserver.IOnGlobalLayoutListener? listener = null;
        listener = new GlobalLayoutOnce(() =>
        {
            observer?.RemoveOnGlobalLayoutListener(listener);
            _pendingReveal = null;
            _content.TranslationY = _content.Height;
            _content.Animate()!.TranslationY(0)!.SetDuration(AnimationMs)!.Start();
        });
        _pendingReveal = listener;
        observer?.AddOnGlobalLayoutListener(listener);
    }

    /// <summary>Drops a reveal listener queued by an earlier <see cref="AnimateIn"/> that a fast Hide() overtook before it fired — otherwise it fires later, against whatever <see cref="_content"/> is doing by then.</summary>
    private void CancelPendingReveal()
    {
        if (_pendingReveal is null) return;
        _content.ViewTreeObserver?.RemoveOnGlobalLayoutListener(_pendingReveal);
        _pendingReveal = null;
    }

    /// <summary>One-shot global-layout listener — runs after the layout pass that follows Show(), unlike <c>View.Post</c> which can fire before it.</summary>
    private sealed class GlobalLayoutOnce(Action onLayout) : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        public void OnGlobalLayout() => onLayout();
    }

    private void AnimateOut()
    {
        CancelPendingReveal();
        _content.Animate()?.Cancel();
        _content.Animate()!.TranslationY(_content.Height)!.SetDuration(AnimationMs)!
            .WithEndAction(new Java.Lang.Runnable(() =>
            {
                // Defensive re-check: cancelling the animator above still lets a same-frame Show()
                // queue its own animation before this end action gets to run. Only the outcome that
                // is still current gets applied.
                if (_visible) return;
                Visibility = ViewStates.Gone;
                _content.TranslationY = 0;
            }))!.Start();
    }
}
