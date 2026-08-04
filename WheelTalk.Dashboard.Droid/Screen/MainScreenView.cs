using Android.Content;
using Android.Views;
using Android.Widget;
using WheelTalk.Dashboard.Droid.Layouts;
using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Рамка хозяина главного экрана: полоса тревоги колеса сверху, сам экран (<see cref="Current"/> —
/// панель <see cref="TwinTapesDashboard"/> либо плитки, см. <see cref="Show"/>), полосы тревоги
/// поверх него (<see cref="Bars"/>) и шторка быстрых команд поверх всего. Живёт в
/// библиотеке, а не в приложении, чтобы стенд показывал ровно то, что видит райдер, — тем же
/// классом, а не похожей копией (тот же ход, которым плашка связи и точка записи раньше переехали
/// внутрь панели). Приложению остаётся проводка: данные, команды шторки, инсеты и жесты.
/// <para>
/// Полоса тревоги и шторка — общие для всех экранов и принадлежат рамке, а не экрану
/// (план 17 §5: «свою шторку у варианта» заводить запрещено; полоса тревоги показывает и тревогу
/// колеса, и служебное «ещё раз — выход», которое не про показ данных вовсе).
/// </para>
/// <para>
/// Потолок ширины 480 dp (adaptive-layout.md §2) стоит здесь, на корне, а не в
/// <c>TwinTapesDashboard.onMeasure</c>: полоса тревоги и шторка — не канва, им тоже нельзя
/// размазываться по планшету. Фон за пределами контента (поля потолка ширины, системные бары после
/// edge-to-edge) — фон панели, а не системный светлый (adaptive-layout.md §4).
/// </para>
/// </summary>
public sealed class MainScreenView : FrameLayout
{
    private readonly LinearLayout _content;
    private readonly FrameLayout _stage;

    public MainScreenView(Context context, DashboardOptions options) : base(context)
    {
        Panel = new TwinTapesDashboard(context, options);
        Current = Panel;
        Alert = new AlertStrip(context);
        Sheet = new QuickSheet(context);
        Bars = new AlertBarsView(context, options);

        // Сцена: сменный экран и полосы тревоги поверх него. Полосы принадлежат рамке, а не экрану
        // (слово владельца 05.08.2026 — «полосы не принадлежат экрану и не дублируются на каждом»):
        // панель и плитки сменяются под ними, а полосы стоят. Границы сцены — это границы бывших
        // панельных полос: ниже строки слов (она над ними, как была), выше шторки (Sheet добавлен
        // позже и остаётся сверху) — вид панели не изменился ни мерой, ни слоем.
        _stage = new FrameLayout(context);
        _stage.AddView(Current.View, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _stage.AddView(Bars, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        var content = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        content.SetBackgroundColor(options.Palette.Background);
        content.AddView(Alert, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        content.AddView(_stage, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        SetBackgroundColor(options.Palette.Background);
        _content = content;
        AddView(content, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)
        {
            Gravity = GravityFlags.CenterHorizontal,
        });

        AddView(Sheet, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
    }

    /// <summary>Показанный сейчас экран — тот, которому водитель кадра носит состояние.</summary>
    public IMainScreen Current { get; private set; }

    /// <summary>
    /// Панель — экран по умолчанию, и рамка держит его сама: он есть всегда, даже пока показан
    /// другой. Так стенд мерит её кадр (<c>LastDrawMs</c>), не спрашивая, что сейчас на экране.
    /// </summary>
    public DashboardView Panel { get; }

    public AlertStrip Alert { get; }

    public QuickSheet Sheet { get; }

    /// <summary>
    /// Полосы тревоги — самостоятельный элемент поверх сменного экрана (панели и плиток одинаково).
    /// Мера у них общая со всеми экранами приложения (<see cref="AlertBarsView.HeightShare"/>): одна
    /// тревога не должна выглядеть двумя разными. Что полоса в полный голос закроет шкалы лент —
    /// принято сознательно: полный голос это ШИМ у предела, до которого в поездке почти не доходят.
    /// Источник тревоги
    /// (<see cref="AlertBarsView.Alert"/>) ставит хозяин рамки и будит элемент со своего кадра —
    /// сами данные тревоги рамка не знает, как не знает их и экран.
    /// </summary>
    public AlertBarsView Bars { get; }

    /// <summary>
    /// Сменить содержимое рамки (план 23 §2.1: «второй Activity не заводить, меняется только
    /// содержимое»). Полоса тревоги и шторка остаются на месте — они принадлежат рамке, а не
    /// экрану.
    /// <para>
    /// Хозяин обязан после этого переставить <see cref="MainScreenDriver.Attach"/> на новый экран:
    /// цикл кадра ставит себя в очередь <c>PostOnAnimation</c> той <c>View</c>, которую обслуживает,
    /// а снятая с рамки в этой очереди уже не стоит.
    /// </para>
    /// </summary>
    public void Show(IMainScreen screen)
    {
        if (ReferenceEquals(screen, Current)) return;

        _stage.RemoveView(Current.View);
        Current = screen;

        // Экран — нулевым ребёнком сцены: полосы тревоги общие и остаются поверх при любой смене.
        _stage.AddView(screen.View, 0, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
    }

    /// <summary>
    /// adaptive-layout.md §2: «≥ 400 dp — суммарная ширина контента ≤ 480 dp, дальше — поля».
    /// Считается на каждом измерении, а не один раз при сборке: Activity объявляет
    /// <c>ConfigChanges.Orientation|ScreenSize</c> и поворот переживает не пересоздаваясь, так что
    /// посчитанная однажды ширина в ландшафте и в разделённом экране была бы неверной
    /// (план 13 §3.2 — портрет решено не фиксировать).
    /// </summary>
    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        int available = MeasureSpec.GetSize(widthMeasureSpec);
        int maxWidthPx = Context!.Dp(480);
        int wanted = available > maxWidthPx ? maxWidthPx : ViewGroup.LayoutParams.MatchParent;

        if (_content.LayoutParameters is { } layout && layout.Width != wanted)
        {
            layout.Width = wanted;
            _content.LayoutParameters = layout;
        }

        base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
    }
}
