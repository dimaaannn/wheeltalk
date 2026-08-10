using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
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
    /// Разделы команд, строка на раздел, слева от строки — боковой корешок с его именем
    /// (план 32 §1, этап 3). Переноса больше нет: состав строки задаёт раздел, а не измеренная
    /// ширина чужих подписей, и кнопки тянутся весом на всю ширину. Последней строкой сюда же
    /// встают переходы — они не команды, но и не корешки, и раздел у них свой.
    /// </summary>
    private readonly LinearLayout _rows;

    /// <summary>
    /// Полоса над командами: корешки экранов (план 23 §2.2) своим разделом и черта под ними.
    /// Не команды: правило «позиции команд фиксированы навсегда» их не касается.
    /// </summary>
    private readonly LinearLayout _screenRow;

    /// <summary>
    /// Кнопки команд по порядку списка, булавка — последней. Подсветка судьбы ищет нажатую по её
    /// номеру среди команд, а обходом дерева его больше не найти: в строке теперь стоят корешок
    /// раздела и обойма, и «первая <see cref="LinearLayout"/> в строке» — уже не кнопка.
    /// </summary>
    private readonly List<View> _buttons = [];

    /// <summary>
    /// Высота строки команд (план 32 §1, этап 3). Задаётся строке <b>явно</b>, и это не украшение:
    /// горизонтальный <see cref="LinearLayout"/> с детьми нулевой ширины и весом мерил свою высоту
    /// нулём — из-за той ловушки прежний ряд и жил на <c>WrapContent</c> с фиксированными ширинами.
    /// Высота задана — веса безопасны.
    /// </summary>
    private const int CommandHeightDp = 70;

    /// <summary>Корешок экрана: во всю ширину, выше наименьшей цели касания (48 dp).</summary>
    private const int TabHeightDp = 48;

    /// <summary>
    /// Переход ниже команды: уйти с экрана — не оперативное дело, и выглядеть командой оно не
    /// должно. 48 dp вместо 44 макета — решение владельца 10.08.2026: имя раздела идёт единым
    /// кеглем со всеми, и в 44 dp «ПЕРЕЙТИ» вставало впритык.
    /// </summary>
    private const int LinkHeightDp = 48;

    /// <summary>Полоса бокового корешка раздела. По высоте раздел не стоит ничего — в этом весь его смысл.</summary>
    private const int SpineWidthDp = 22;

    private const int GapDp = 8;

    /// <summary>
    /// Кегли шторки. Подпись команды 12 sp вместо прежних 10 (план 25 §2, шаг 4: 10 sp — вдвое ниже
    /// минимума ISO 15008 даже для взгляда стоя), корешок экрана 16 sp, переход 13 sp, имя
    /// раздела 10 sp — оно подсказка, а не то, что читают на ходу.
    /// </summary>
    private const float LabelSp = 12;

    private const float TabLabelSp = 16;

    private const float LinkLabelSp = 13;

    private const float SpineSp = 10;

    private const int IconDp = 24;

    private const int TabIconDp = 22;

    private const int LinkIconDp = 19;

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

        _screenRow = new LinearLayout(context)
        {
            Orientation = Android.Widget.Orientation.Vertical,
            Visibility = ViewStates.Gone,
        };

        _content = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical, Clickable = true };
        _content.Background = BuildSheetBackground(context);
        _content.SetPadding(context.Dp(14), context.Dp(10), context.Dp(14), context.Dp(16));
        _content.AddView(BuildGrabber(context), new LinearLayout.LayoutParams(context.Dp(36), context.Dp(4))
        {
            Gravity = GravityFlags.CenterHorizontal,
            BottomMargin = context.Dp(12),
        });
        _content.AddView(_screenRow, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        _content.AddView(_rows, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        AddView(_content, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { Gravity = GravityFlags.Bottom });
    }

    public bool IsOpen => _visible;

    /// <summary>Подпись булавки — слово, которое шторка показывает сама.</summary>
    public Func<string> PinLabel { get; set; } = () => "Pin";

    /// <summary>
    /// В каком разделе стоит булавка. По смыслу она про телефон в эту поездку, но слова про телефон
    /// библиотеке не принадлежат — раздел ей называет хозяин меню тем же ключом, что и командам.
    /// Пусто — своим, безымянным разделом.
    /// </summary>
    public string PinGroup { get; set; } = "";

    /// <summary>Имя раздела по ключу команды (<see cref="QuickSheetCommand.Group"/>).</summary>
    public Func<string, string> SectionLabel { get; set; } = group => group;

    /// <summary>Имя раздела корешков экранов.</summary>
    public Func<string> ScreensSectionLabel { get; set; } = () => "Screen";

    /// <summary>Имя раздела переходов.</summary>
    public Func<string> LinksSectionLabel { get; set; } = () => "Go";

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
    /// Переходы на другие экраны приложения — последний раздел шторки, «Перейти» (план 32 §1,
    /// этап 3). Пусто — раздела нет вовсе.
    /// </summary>
    public void SetLinks(IReadOnlyList<QuickSheetLink> links)
    {
        _links = links;
        RebuildRow();
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
        background.SetColor(QuickSheetPalette.Grabber);
        grabber.Background = background;
        return grabber;
    }

    /// <summary>
    /// Подложка шторки: скруглённый сверху на 20 dp прямоугольник и волосяная граница по верхнему
    /// краю (план 32 §1, этап 3). До этого подложкой был общий <c>PageBackground()</c> — тот же
    /// тёмный, что и фон панели, и границы шторки почти не было видно.
    /// <para>
    /// Двумя слоями, а не обводкой: обводка <see cref="GradientDrawable"/> идёт по всем четырём
    /// сторонам, а нужна одна — та, что отделяет шторку от панели.
    /// </para>
    /// </summary>
    private static Drawable BuildSheetBackground(Context context)
    {
        float radius = context.Dp(20);
        float[] corners = [radius, radius, radius, radius, 0, 0, 0, 0];

        var border = new GradientDrawable();
        border.SetShape(ShapeType.Rectangle);
        border.SetCornerRadii(corners);
        border.SetColor(QuickSheetPalette.TopBorder);

        var face = new GradientDrawable();
        face.SetShape(ShapeType.Rectangle);
        face.SetCornerRadii(corners);
        face.SetColor(QuickSheetPalette.Background);

        var sheet = new LayerDrawable([border, face]);
        sheet.SetLayerInset(1, 0, context.Dp(1), 0, 0);
        return sheet;
    }

    /// <summary>
    /// Строка раздела: слева боковой корешок с именем, справа обойма, куда встают кнопки весом.
    /// Отдаёт обойму — саму строку хозяину знать незачем.
    /// <para>
    /// Высота строки задаётся <b>здесь и точно</b>: на ней держится вся раскладка с весами
    /// (см. <see cref="CommandHeightDp"/>).
    /// </para>
    /// </summary>
    private LinearLayout AddSection(LinearLayout host, string title, int heightDp)
    {
        var context = Context!;

        var row = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Horizontal };
        row.AddView(new SectionSpine(context, title), new LinearLayout.LayoutParams(
            context.Dp(SpineWidthDp), ViewGroup.LayoutParams.MatchParent));

        var slots = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Horizontal };
        row.AddView(slots, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f)
        {
            LeftMargin = context.Dp(GapDp),
        });

        host.AddView(row, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, context.Dp(heightDp))
        {
            TopMargin = host.ChildCount > 0 ? context.Dp(GapDp) : 0,
        });

        return slots;
    }

    /// <summary>
    /// Кнопка встаёт в обойму весом: в строке из двух они просто шире, из трёх — уже, а пустого
    /// места не остаётся ни в одной (план 32 §1, этап 3). Место кнопки при этом постоянно — его
    /// задаёт раздел, а не измеренная ширина соседей.
    /// </summary>
    private void AddToSection(LinearLayout slots, View view)
    {
        slots.AddView(view, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f)
        {
            LeftMargin = slots.ChildCount > 0 ? Context!.Dp(GapDp) : 0,
        });
    }

    /// <summary>
    /// Rebuilds the screen strip from scratch on every call — cheap enough at a handful of tabs, and
    /// it keeps the highlight honest: a tap reads <see cref="QuickSheetScreen.IsSelected"/> fresh
    /// right after invoking <see cref="QuickSheetScreen.Select"/>, so the sheet never decides on its
    /// own which screen is current, it only reflects what the caller just set.
    /// </summary>
    private void RebuildScreens()
    {
        _screenRow.RemoveAllViews();
        _screenRow.Visibility = _screens.Count == 0 ? ViewStates.Gone : ViewStates.Visible;
        if (_screens.Count == 0) return;

        var slots = AddSection(_screenRow, ScreensSectionLabel(), TabHeightDp);

        foreach (var screen in _screens)
        {
            var tab = BuildScreenTab(screen);
            tab.Click += (_, _) =>
            {
                screen.Select();
                RebuildScreens();
            };

            AddToSection(slots, tab);
        }

        // Черта под корешками: корешок меняет содержимое рамки, команда делает что-то с колесом или
        // телефоном — это разные вещи, а не пятый раздел команд (план 32 §1, этап 3).
        _screenRow.AddView(BuildDivider(), new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Context!.Dp(1))
        {
            TopMargin = Context!.Dp(12),
            BottomMargin = Context!.Dp(12),
        });
    }

    /// <summary>
    /// Черта между корешками экранов и командами. Своей величины у голого <see cref="View"/> нет —
    /// высоту ему задаёт тот, кто добавляет (см. <see cref="BuildGrabber"/> про то, чем кончается
    /// <c>WrapContent</c> на таком).
    /// </summary>
    private View BuildDivider()
    {
        var divider = new View(Context!);
        divider.SetBackgroundColor(QuickSheetPalette.Plate);
        return divider;
    }

    /// <summary>
    /// Переход: значок и подпись <b>в строку</b>, узкой кнопкой с обводкой и без заливки — уйти с
    /// экрана оперативной командой не является, и выглядеть командой не должно (план 32 §1,
    /// этап 3). Выбранным переход не бывает.
    /// </summary>
    private View BuildScreenLink(QuickSheetLink link)
    {
        var context = Context!;

        var button = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Horizontal };
        button.SetGravity(GravityFlags.Center);
        button.Clickable = true;

        var background = new GradientDrawable();
        background.SetShape(ShapeType.Rectangle);
        background.SetCornerRadius(context.Dp(11));
        background.SetColor(Color.Transparent);
        background.SetStroke(context.Dp(1), QuickSheetPalette.LinkBorder);
        button.Background = background;

        button.AddView(BuildIcon(link.Icon, QuickSheetPalette.Ink, LinkIconDp));

        var label = new TextView(context) { Text = link.Label, Gravity = GravityFlags.Center };
        label.SetTextSize(ComplexUnitType.Sp, LinkLabelSp);
        label.SetTextColor(QuickSheetPalette.Ink);
        label.SetSingleLine(true);
        label.Ellipsize = TextUtils.TruncateAt.End;
        label.SetPadding(context.Dp(7), 0, 0, 0);
        button.AddView(label);

        return button;
    }

    /// <summary>
    /// Корешок экрана: во всю ширину своей доли ряда, 48 dp высотой, значок и слово в строку.
    /// Ширина во всю долю — заодно и лечение узкой цели касания: тап между значком и словом раньше
    /// проваливался мимо кнопки (найдено 09.08.2026).
    /// </summary>
    private View BuildScreenTab(QuickSheetScreen screen)
    {
        var context = Context!;
        bool selected = screen.IsSelected();
        var accent = QuickSheetPalette.Accent;

        var tab = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Horizontal };
        tab.SetGravity(GravityFlags.Center);
        tab.Clickable = true;

        var background = new GradientDrawable();
        background.SetShape(ShapeType.Rectangle);
        background.SetCornerRadius(context.Dp(12));
        background.SetColor(selected ? accent : Color.Transparent);
        if (!selected) background.SetStroke(context.Dp(1), QuickSheetPalette.TabBorder);
        tab.Background = background;

        // Выбранный корешок залит акцентом — слово на нём берёт тот цвет, что читается на этой
        // заливке (в тёмной теме акцент светлый, и белым по нему выходило 2,5:1). Невыбранный
        // акцентом больше не красится: тем же цветом красились и все кнопки команд, отсюда и
        // «похожего цвета» (план 25 §1).
        var ink = selected ? Readable(accent) : QuickSheetPalette.Ink;

        tab.AddView(BuildIcon(screen.Icon, ink, TabIconDp));

        var label = new TextView(context) { Text = screen.Label, Gravity = GravityFlags.Center };
        label.SetTextSize(ComplexUnitType.Sp, TabLabelSp);
        label.SetTextColor(ink);
        label.SetSingleLine(true);
        label.Ellipsize = TextUtils.TruncateAt.End;
        if (selected) label.SetTypeface(null, TypefaceStyle.Bold);
        label.SetPadding(context.Dp(9), 0, 0, 0);
        tab.AddView(label);

        return tab;
    }

    /// <summary>
    /// Разделы команд сверху вниз: строка на раздел, соседи по разделу стоят подряд. Булавка идёт
    /// тем же путём, что и команды, а не приписывается в конец: отдельный путь и был тем, из-за
    /// чего её уносило за край. Последним — раздел переходов.
    /// </summary>
    private void RebuildRow()
    {
        _rows.RemoveAllViews();
        _buttons.Clear();

        var entries = new List<QuickSheetCommand>(_commands) { PinCommand() };

        for (int index = 0; index < entries.Count;)
        {
            string group = entries[index].Group;
            var slots = AddSection(_rows, SectionLabel(group), CommandHeightDp);

            while (index < entries.Count && entries[index].Group == group)
            {
                AddToSection(slots, BuildCommandButton(entries[index], index));
                index++;
            }
        }

        if (_links.Count == 0) return;

        var linkSlots = AddSection(_rows, LinksSectionLabel(), LinkHeightDp);

        foreach (var link in _links)
        {
            var button = BuildScreenLink(link);
            button.Click += (_, _) =>
            {
                Hide();
                link.Open();
            };

            AddToSection(linkSlots, button);
        }
    }

    /// <summary>
    /// Кнопка команды вместе с её поведением; номер в списке запоминается тут же — по нему
    /// подсветка судьбы находит нажатую (<see cref="ButtonAt"/>). Номер за концом списка команд —
    /// булавка: она про саму шторку, и дело у неё своё.
    /// </summary>
    private View BuildCommandButton(QuickSheetCommand command, int index)
    {
        var button = BuildButton(command);
        _buttons.Add(button);

        if (index == _commands.Count)
        {
            button.Click += (_, _) => OnPinTapped();
            return button;
        }

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

        return button;
    }

    private View BuildButton(QuickSheetCommand command)
    {
        bool enabled = command.IsEnabled?.Invoke() ?? true;
        bool on = command.IsOn?.Invoke() ?? false;

        var context = Context!;
        var button = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        button.SetGravity(GravityFlags.Center);
        int pad = context.Dp(4);
        button.SetPadding(pad, pad, pad, pad);
        button.Alpha = enabled ? 1f : 0.4f;
        button.Clickable = true;

        // Спокойная плашка вместо акцентной (план 32 §1, этап 3): акцентом красились и невключённая
        // команда, и корешок выбранного экрана — вот и «все кнопки похожего цвета» (план 25 §1).
        // Цвет слова считается по заливке, а не берётся белым навсегда: белым по янтарю «включено»
        // выходило 2,0:1 — вдвое ниже требуемых 4,5:1 (WCAG 1.4.3).
        var fill = on ? OnColor(context) : QuickSheetPalette.Plate;
        var ink = Readable(fill);

        var background = new GradientDrawable();
        background.SetShape(ShapeType.Rectangle);
        background.SetCornerRadius(context.Dp(12));
        background.SetColor(fill);
        button.Background = background;

        button.AddView(BuildIcon(command.Icon, ink, IconDp));

        var label = new TextView(context) { Text = command.Label(), Gravity = GravityFlags.Center };
        label.SetTextSize(ComplexUnitType.Sp, LabelSp);
        label.SetTextColor(ink);
        label.SetMaxLines(2);
        label.Ellipsize = TextUtils.TruncateAt.End;
        if (on) label.SetTypeface(null, TypefaceStyle.Bold);
        button.AddView(label, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = context.Dp(5),
        });

        return button;
    }

    /// <summary>
    /// Значок — свой контур (<see cref="QuickIcons"/>) в цвете кнопки, тинтом: держать один и тот
    /// же знак в трёх цветах ради белого, чёрного и приглушённого было бы втрое больше файлов.
    /// </summary>
    private ImageView BuildIcon(int icon, Color tint, int sizeDp)
    {
        var context = Context!;
        var view = new ImageView(context);
        view.SetImageResource(icon);
        view.ImageTintList = ColorStateList.ValueOf(tint);
        view.LayoutParameters = new LinearLayout.LayoutParams(context.Dp(sizeDp), context.Dp(sizeDp));
        return view;
    }

    /// <summary>Булавка как команда: место в ряду ей достаётся тем же путём, что и остальным.</summary>
    private QuickSheetCommand PinCommand() => new()
    {
        Icon = QuickIcons.Pin,
        Group = PinGroup,
        Label = PinLabel,
        IsOn = () => _pinned,
        Action = () => Task.CompletedTask,
    };

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
    /// Кнопка по её номеру среди команд — не по номеру внутри строки: разделы разложили команды по
    /// строкам, и подсветка «команда не ушла» светила бы соседа.
    /// </summary>
    private View? ButtonAt(int index) => index >= 0 && index < _buttons.Count ? _buttons[index] : null;

    /// <summary>
    /// Боковой корешок раздела: имя снизу вверх в полосе шириной <see cref="SpineWidthDp"/> dp.
    /// Заголовок над строкой стоил бы 18 dp высоты пять раз и оставлял бы дыры в неполных строках —
    /// корешок по высоте не стоит ничего (план 32 §1, этап 3).
    /// <para>
    /// Рисуется поворотом канвы, а не повёрнутым <see cref="TextView"/>: тот меряется до поворота,
    /// как горизонтальный, и полоса получила бы ширину слова вместо 22 dp — капкан плана 32 §2.
    /// </para>
    /// </summary>
    private sealed class SectionSpine : View
    {
        private readonly string _title;
        private readonly Paint _paint;

        public SectionSpine(Context context, string title) : base(context)
        {
            _title = title.ToUpperInvariant();
            _paint = new Paint(PaintFlags.AntiAlias)
            {
                Color = QuickSheetPalette.Spine,
                TextAlign = Paint.Align.Center,
                TextSize = TypedValue.ApplyDimension(
                    ComplexUnitType.Sp, SpineSp, context.Resources!.DisplayMetrics!),
                // Разрядка макета — 1,6 px при кегле 10, то есть 0,16 em: Paint мерит её в em.
                LetterSpacing = 0.16f,
            };
            _paint.SetTypeface(Typeface.DefaultBold);
        }

        protected override void OnDraw(Canvas canvas)
        {
            // Кегль у всех разделов один и подгонке под длину слова не подлежит (решение владельца
            // 10.08.2026): разнокалиберные корешки читаются как разные по важности.
            float centerX = Width / 2f;
            float centerY = Height / 2f;

            canvas.Save();
            canvas.Rotate(-90, centerX, centerY);
            canvas.DrawText(_title, centerX, centerY - (_paint.Ascent() + _paint.Descent()) / 2f, _paint);
            canvas.Restore();
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
    /// Судьба команды — заливкой кнопки (design doc §5). Слово и значок перекрашиваются вместе с
    /// ней тем же расчётом, что и при сборке: иначе чёрная подпись, выбранная под янтарь
    /// «включено», оставалась бы на тёмно-зелёном. Значок теперь свой контур, и красится он тинтом,
    /// а не цветом текста.
    /// </summary>
    private static void Highlight(View? view, bool success)
    {
        if (view is not LinearLayout { Background: GradientDrawable background } button) return;

        var fill = success ? SuccessColor : FailureColor;
        background.SetColor(fill);

        var ink = Readable(fill);
        for (int i = 0; i < button.ChildCount; i++)
        {
            switch (button.GetChildAt(i))
            {
                case TextView text: text.SetTextColor(ink); break;
                case ImageView icon: icon.ImageTintList = ColorStateList.ValueOf(ink); break;
            }
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
