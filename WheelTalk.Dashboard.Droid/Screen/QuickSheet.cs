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
    /// <summary>
    /// Сколько шторка ждёт, прежде чем закрыться сама. Было 5 с; 15 — решение владельца 08.08.2026
    /// (план 25 §0.1) по жалобам: пять секунд уходят на «что тут вообще есть», и шторка закрывалась
    /// раньше, чем человек успевал выбрать.
    /// <para>
    /// Довод против — «пятнадцать секунд закрытой панели на ходу это долго» — владельцем **снят**:
    /// шторка закрывается тапом по пустому месту, а для бесконечного случая есть «Не закрывать»
    /// (<c>_pinned</c>). Проверяется на выезде.
    /// </para>
    /// </summary>
    private static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(15);

    /// <summary>"Задержка на долю секунды" — design doc §5's honesty window: long enough to read the highlight, short enough not to feel stuck (§ "на ходу длинные анимации раздражают").</summary>
    private static readonly TimeSpan PostCommandHideDelay = TimeSpan.FromMilliseconds(450);

    private const int AnimationMs = 150;
    private static readonly Color SuccessColor = Color.ParseColor("#1B5E20");
    private static readonly Color FailureColor = Color.ParseColor("#B00020");
    /// <summary>
    /// Заливка «включено». Тёмная тема — прежний янтарь (8,1:1 к подложке #1f1f1f), светлая —
    /// притемнённый (3,9:1 к белой подложке): прежний давал 2,0:1, то есть кнопка «включено» на
    /// белом фоне не отличалась от фона по требованию WCAG 1.4.11 к нетекстовым элементам
    /// (план 25 §2, шаг 4). Слово на обоих — чёрное, 10,3:1 и 5,4:1.
    /// </summary>
    private static Color OnColor(Context context) =>
        context.IsDarkTheme() ? Color.ParseColor("#FFA000") : Color.ParseColor("#C46A00");

    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly Action _hideNow;
    private readonly LinearLayout _content;
    /// <summary>
    /// Ряд оперативных команд: одна строка, пока команды помещаются, иначе — перенос по измеренной
    /// ширине (<see cref="RowStack"/>). Семь кнопок не влезали в 360 dp, и последняя («Настройки»)
    /// просто уезжала за край — найдено 30.07.2026, когда добавилась кнопка «Не гасить». Прокрутку
    /// не берём: искать команду пальцем на ходу хуже, чем увидеть все сразу.
    /// </summary>
    private readonly LinearLayout _rows;

    /// <summary>
    /// Полоса над рядом команд: корешки экранов (план 23 §2.2) и, за разделителем, переходы на
    /// другие экраны (план 25 §2, шаг 2). Не команды: правило «позиции команд фиксированы навсегда»
    /// их не касается, и перенос у неё свой — тем же <see cref="RowStack"/>, но по своей мерке.
    /// </summary>
    private readonly LinearLayout _screenRow;

    /// <summary>Минимальная сторона кнопки — она же цель касания (quick-commands-design.md §3).</summary>
    private const int ButtonWidthDp = 56;

    /// <summary>
    /// Наименьшая цель касания — 48 dp (Android). Кнопкам команд её даёт <see cref="ButtonWidthDp"/>,
    /// а корешкам и переходам, у которых высота росла из одних паддингов, — вот эта величина: замер
    /// 09.08.2026 дал у них 31 dp, ниже нормы (план 25 §2, шаг 4).
    /// </summary>
    private const int TouchTargetDp = 48;

    /// <summary>
    /// Кегли шторки, ISO 15008 в пересчёте на телефон у руля (700 мм): угл. мин. ≈ 382 × sp / 700.
    /// Значок 26 sp — 14,2′, выше минимальных 12′; подпись 13 sp — 7,1′, ниже минимума и осознанно:
    /// подпись отвечает на «какая за что» с полуметра, а издалека работают значок и место кнопки.
    /// Было 20 и 10 sp — 10,9′ и 5,5′, ниже минимума оба (план 25 §2, шаг 4).
    /// </summary>
    private const float IconSp = 26;

    private const float LabelSp = 13;

    private IReadOnlyList<QuickSheetCommand> _commands = [];
    private IReadOnlyList<QuickSheetScreen> _screens = [];
    private IReadOnlyList<QuickSheetLink> _links = [];
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

        // Вертикальный, как и ряд команд: две вкладки да три перехода в 360 dp одной строкой не
        // помещаются, и без переноса последний переход уезжал бы за край — та же беда, что нашлась
        // у команд 30.07.2026.
        _screenRow = new LinearLayout(context)
        {
            Orientation = Android.Widget.Orientation.Vertical,
            Visibility = ViewStates.Gone,
        };

        _content = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical, Clickable = true };
        _content.SetBackgroundColor(context.PageBackground());
        _content.AddView(BuildGrabber(context), new LinearLayout.LayoutParams(context.Dp(32), context.Dp(4))
        {
            Gravity = GravityFlags.CenterHorizontal,
            TopMargin = context.Dp(8),
            BottomMargin = context.Dp(8),
        });
        _content.AddView(_screenRow, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
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

    /// <summary>
    /// The screen strip's composition — корешки экранов, план 23 §2.2. Empty by default, which
    /// hides the strip: a host with one screen shows none, exactly as before this strip existed.
    /// </summary>
    public void SetScreens(IReadOnlyList<QuickSheetScreen> screens)
    {
        _screens = screens;
        RebuildScreens();
    }

    /// <summary>
    /// Переходы на другие экраны приложения — та же полоса, что у корешков, но своя стайка за
    /// разделителем (план 25 §2, шаг 2: «переходы к переходам»). Пусто — стайки нет; так живёт
    /// стенд, которому уходить некуда.
    /// </summary>
    public void SetLinks(IReadOnlyList<QuickSheetLink> links)
    {
        _links = links;
        RebuildScreens();
    }

    public void Show()
    {
        if (_visible) return;
        _visible = true;
        RebuildScreens();
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

    /// <summary>
    /// Rebuilds the screen strip from scratch on every call — cheap enough at a handful of tabs, and
    /// it keeps the highlight honest: a tap reads <see cref="QuickSheetScreen.IsSelected"/> fresh
    /// right after invoking <see cref="QuickSheetScreen.Select"/>, so the sheet never decides on its
    /// own which screen is current, it only reflects what the caller just set.
    /// </summary>
    private void RebuildScreens()
    {
        _screenRow.Visibility = _screens.Count == 0 && _links.Count == 0 ? ViewStates.Gone : ViewStates.Visible;

        var stack = new RowStack(Context!, _screenRow);

        foreach (var screen in _screens)
        {
            var tab = BuildScreenTab(screen);
            tab.Click += (_, _) =>
            {
                screen.Select();
                RebuildScreens();
            };

            stack.Add(tab);
        }

        // Переходы — за разделителем и без заливки: они уводят с экрана совсем, а корешок только
        // меняет содержимое рамки (план 25 §2, шаг 2). Одинаковыми им выглядеть нельзя.
        if (_screens.Count > 0 && _links.Count > 0 && stack.RowStarted) stack.Add(BuildDivider());

        foreach (var link in _links)
        {
            var button = BuildScreenLink(link);
            button.Click += (_, _) =>
            {
                Hide();
                link.Open();
            };

            stack.Add(button);
        }
    }

    /// <summary>
    /// Тонкая черта между стайками — и в полосе переходов, и в ряду команд: одна разлука, один вид.
    /// <para>
    /// Размер задаётся <b>здесь и точно</b>, 1×36 dp: у голого <see cref="View"/> своей величины
    /// нет, и <c>WrapContent</c> разрешился бы во всё доступное место (см. <see cref="BuildGrabber"/>
    /// и <see cref="RowStack.Add"/>).
    /// </para>
    /// </summary>
    private View BuildDivider()
    {
        var context = Context!;
        var divider = new View(context);
        divider.SetBackgroundColor(Color.Argb(60, 128, 128, 128));
        divider.LayoutParameters = new ViewGroup.LayoutParams(context.Dp(1), context.Dp(TouchTargetDp - 12));
        return divider;
    }

    /// <summary>
    /// Переход: значок и слово в цвете акцента, без заливки и без выделения «выбрано» — выбранным
    /// переход не бывает. Высота — не меньше цели касания (48 dp), как у корешков.
    /// </summary>
    private View BuildScreenLink(QuickSheetLink link)
    {
        var context = Context!;
        var accent = NormalColor(context);

        var button = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Horizontal };
        button.SetGravity(GravityFlags.Center);
        button.SetMinimumHeight(context.Dp(TouchTargetDp));
        int padH = context.Dp(12);
        button.SetPadding(padH, 0, padH, 0);
        button.Clickable = true;

        var icon = new TextView(context) { Text = link.Icon, Gravity = GravityFlags.Center };
        icon.SetTextSize(ComplexUnitType.Sp, 18);
        icon.SetTextColor(accent);
        button.AddView(icon);

        var label = new TextView(context) { Text = link.Label, Gravity = GravityFlags.Center };
        label.SetTextSize(ComplexUnitType.Sp, LabelSp);
        label.SetTextColor(accent);
        label.SetPadding(context.Dp(4), 0, 0, 0);
        button.AddView(label);

        return button;
    }

    private View BuildScreenTab(QuickSheetScreen screen)
    {
        var context = Context!;
        bool selected = screen.IsSelected();
        var accent = NormalColor(context);

        var tab = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Horizontal };
        tab.SetGravity(GravityFlags.Center);
        tab.SetMinimumHeight(context.Dp(TouchTargetDp));
        int padH = context.Dp(12);
        tab.SetPadding(padH, 0, padH, 0);

        var background = new GradientDrawable();
        background.SetShape(ShapeType.Rectangle);
        background.SetCornerRadius(context.Dp(16));
        background.SetColor(selected ? accent : Color.Transparent);
        tab.Background = background;

        // Выбранный корешок залит акцентом — слово на нём берёт тот цвет, что читается на этой
        // заливке (в тёмной теме акцент светлый, и белым по нему выходило 2,5:1).
        var textColor = selected ? Readable(accent) : accent;

        var icon = new TextView(context) { Text = screen.Icon, Gravity = GravityFlags.Center };
        icon.SetTextSize(ComplexUnitType.Sp, 18);
        icon.SetTextColor(textColor);
        tab.AddView(icon);

        var label = new TextView(context) { Text = screen.Label, Gravity = GravityFlags.Center };
        label.SetTextSize(ComplexUnitType.Sp, LabelSp);
        label.SetTextColor(textColor);
        label.SetPadding(context.Dp(4), 0, 0, 0);
        tab.AddView(label);

        return tab;
    }

    private void RebuildRow()
    {
        var stack = new RowStack(Context!, _rows);
        string? group = null;

        for (int i = 0; i < _commands.Count; i++)
        {
            int index = i;
            var command = _commands[i];

            // Разделитель встаёт на смене стайки (план 25 §2, шаг 3) — но не первым в строке: у
            // переноса своя разлука, и черта в начале строки разделяла бы пустоту.
            if (group is not null && command.Group != group && stack.RowStarted) stack.Add(BuildDivider());

            group = command.Group;
            var button = BuildButton(command);
            if (command.IsEnabled?.Invoke() ?? true)
            {
                button.Click += (_, _) => OnCommandTapped(index, command.Action);

                // Consumed long click swallows the click that would otherwise follow it on the same
                // ACTION_UP (platform behaviour, no gesture bookkeeping needed here) — a long press
                // fires only LongPress, never Action too.
                if (command.LongPress is { } longPress)
                {
                    button.LongClick += (_, e) =>
                    {
                        OnCommandTapped(index, longPress);
                        e.Handled = true;
                    };
                }
            }

            stack.Add(button);
        }

        // Булавка идёт тем же путём, что и команды, а не приписывается в конец: отдельный путь и
        // был тем, из-за чего её уносило за край, когда команды заполняли строку ровно. И стайка у
        // неё своя: булавка — про саму шторку, а не про колесо или телефон.
        if (_commands.Count > 0 && stack.RowStarted) stack.Add(BuildDivider());
        stack.Add(BuildPinButton());
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

        // Цвет слова выбирается по заливке, а не берётся белым навсегда: у «включено» (янтарь) и у
        // акцента тёмной темы белым выходило 2,0:1 и 2,5:1 — вдвое ниже требуемых 4,5:1
        // (WCAG 1.4.3). Тот же расчёт, что у подсветки судьбы команды.
        var fill = on ? OnColor(context) : NormalColor(context);
        var textColor = Readable(fill);

        var background = new GradientDrawable();
        background.SetShape(ShapeType.Rectangle);
        background.SetCornerRadius(context.Dp(10));
        background.SetColor(fill);
        button.Background = background;

        var icon = new TextView(context) { Text = command.Icon, Gravity = GravityFlags.Center };
        icon.SetTextSize(ComplexUnitType.Sp, IconSp);
        icon.SetTextColor(textColor);
        button.AddView(icon);

        var label = new TextView(context) { Text = command.Label(), Gravity = GravityFlags.Center };
        label.SetTextSize(ComplexUnitType.Sp, LabelSp);
        label.SetTextColor(textColor);
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
    /// Чёрное или белое — что читается на этой заливке (WCAG 1.4.3, 4,5:1 для подписи). Считается, а
    /// не выбирается на глаз: белым по янтарю «включено» выходило 2,0:1, а по светлому акценту
    /// тёмной темы — 2,5:1, и обе кнопки честно выглядели «похожего цвета», как и жаловался
    /// владелец (план 25 §2, шаг 4). Чёрным на тех же заливках — 10,3:1 и 8,5:1.
    /// </summary>
    private static Color Readable(Color background) =>
        Contrast(Color.White, background) >= Contrast(Color.Black, background) ? Color.White : Color.Black;

    /// <summary>Отношение контраста по WCAG 2.x: (L₁ + 0,05) / (L₂ + 0,05).</summary>
    private static double Contrast(Color first, Color second)
    {
        double a = Luminance(first);
        double b = Luminance(second);
        return a > b ? (a + 0.05) / (b + 0.05) : (b + 0.05) / (a + 0.05);
    }

    /// <summary>Относительная яркость sRGB — WCAG 2.x, определение relative luminance.</summary>
    private static double Luminance(Color color) =>
        0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

    private static double Channel(byte value)
    {
        double c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

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

            for (int c = 0; c < row.ChildCount; c++)
            {
                // Разделители стоят в той же строке, но кнопками не являются: считать их значило бы
                // подсветить соседа вместо нажатой команды.
                if (row.GetChildAt(c) is not LinearLayout button) continue;
                if (index == 0) return button;
                index--;
            }
        }

        return null;
    }

    /// <summary>
    /// Укладчик строк: кладёт элементы слева направо, а когда очередной в строку уже не влезает —
    /// начинает новую. Один на обе полосы шторки (команды и корешки с переходами): у них одна и та
    /// же беда — 360 dp кончаются раньше, чем элементы, — и разные решения расходились бы.
    /// <para>
    /// Ширину спрашиваем у самого элемента, а не считаем по минимуму кнопки: 56 dp — это минимум, а
    /// настоящая ширина растёт с подписью, и «Закрепить» вдвое шире «Бипа». Расчёт по минимуму врал
    /// в большую сторону, и последняя кнопка уезжала за край; тем же он ломался бы от длинного
    /// перевода и от увеличенного шрифта в системных настройках.
    /// </para>
    /// </summary>
    private sealed class RowStack
    {
        private readonly Context _context;
        private readonly LinearLayout _container;
        private readonly int _available;
        private readonly int _gap;
        private LinearLayout _row = null!;
        private int _used;

        public RowStack(Context context, LinearLayout container)
        {
            _context = context;
            _container = container;
            _container.RemoveAllViews();
            _available = context.Resources!.DisplayMetrics!.WidthPixels - context.Dp(16);
            _gap = context.Dp(8);
            StartRow();
        }

        /// <summary>
        /// Кладёт элемент, <b>сохраняя заданный им самим размер</b>. Это не мелочь: голый
        /// <see cref="View"/> (разделитель) при <c>WrapContent</c> разрешается не в свою величину, а
        /// во <b>всё доступное</b> — <c>View.getDefaultSize</c> под <c>AT_MOST</c>. Та же ловушка,
        /// что однажды раздула черенок (см. <see cref="BuildGrabber"/>): затерев 1×36 dp
        /// разделителя на <c>WrapContent</c>, укладчик получал серую колонну во весь экран, а вместе
        /// с ней — строку во всю высоту, шторку на весь экран и уехавшие за край команды
        /// (найдено на телефоне 09.08.2026, 720×1440).
        /// </summary>
        public void Add(View view)
        {
            var own = view.LayoutParameters;
            int wantedWidth = own?.Width ?? ViewGroup.LayoutParams.WrapContent;
            int wantedHeight = own?.Height ?? ViewGroup.LayoutParams.WrapContent;

            // Ширина для подсчёта: заданная — как есть, иначе измеренная. Мерить элемент с явной
            // шириной нельзя: тот же голый View ответит нулём, и строка сочтёт разделитель бесплатным.
            int width;
            if (wantedWidth > 0)
            {
                width = wantedWidth;
            }
            else
            {
                int unspecified = MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified);
                view.Measure(unspecified, unspecified);
                width = view.MeasuredWidth;
            }

            int gap = _row.ChildCount > 0 ? _gap : 0;

            if (_row.ChildCount > 0 && _used + gap + width > _available)
            {
                StartRow();
                gap = 0;
            }

            _used += gap + width;

            var p = new LinearLayout.LayoutParams(wantedWidth, wantedHeight)
            {
                Gravity = GravityFlags.CenterVertical,
                LeftMargin = gap,
            };
            _row.AddView(view, p);
        }

        /// <summary>Строка не пуста — значит разделитель между стайками поставить есть куда.</summary>
        public bool RowStarted => _row.ChildCount > 0;

        private void StartRow()
        {
            _used = 0;
            _row = new LinearLayout(_context) { Orientation = Android.Widget.Orientation.Horizontal };
            _row.SetGravity(GravityFlags.CenterHorizontal);
            _container.AddView(_row, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = _container.ChildCount > 0 ? _gap : 0,
            });
        }
    }

    /// <summary>
    /// Runs a fired delegate — <see cref="QuickSheetCommand.Action"/> for a tap,
    /// <see cref="QuickSheetCommand.LongPress"/> for a long press — then holds its fate on screen
    /// before deciding what happens next: the honesty window design doc §5 asks for. The button
    /// that just fired is the one that gets highlighted, so it is rebuilt into the row before the
    /// highlight (not after — a highlight applied to a view about to be replaced would never be
    /// seen) and looked up by its stable index.
    /// </summary>
    private async void OnCommandTapped(int index, Func<Task> fired)
    {
        _handler.RemoveCallbacks(_hideNow);

        bool success;
        try
        {
            await fired();
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

    /// <summary>
    /// Судьба команды — заливкой кнопки (design doc §5). Слово перекрашивается вместе с ней тем же
    /// расчётом, что и при сборке: иначе чёрная подпись, выбранная под светлый акцент, оставалась
    /// бы на тёмно-зелёном.
    /// </summary>
    private static void Highlight(View? view, bool success)
    {
        if (view is not LinearLayout { Background: GradientDrawable background } button) return;

        var fill = success ? SuccessColor : FailureColor;
        background.SetColor(fill);

        var textColor = Readable(fill);
        for (int i = 0; i < button.ChildCount; i++)
        {
            if (button.GetChildAt(i) is TextView text) text.SetTextColor(textColor);
        }
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
