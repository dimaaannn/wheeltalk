using Android.Content;
using Android.Views;
using Android.Widget;
using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Рамка хозяина главного экрана: сам экран (<see cref="Current"/> — панель либо плитки, чей —
/// решает хозяин, см. <see cref="Show"/>), поверх него полосы тревоги
/// (<see cref="Bars"/>) и строка слов тревоги сверху (<see cref="Alert"/>), а поверх всего — шторка
/// быстрых команд. Всё, что поверх, — накладки: показ ни одной из них не меняет меру экрана. Живёт в
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
    private readonly FrameLayout _stage;

    /// <param name="initial">
    /// Экран, с которого рамка начинает. Приходит снаружи, и рамка ни одного экрана не строит сама
    /// (план 17 §3): пока она собирала панель, у панели было место, которого нет у остальных, —
    /// а вариантов панели с этого шага несколько, и выбирает их реестр, не рамка.
    /// </param>
    public MainScreenView(Context context, DashboardOptions options, IMainScreen initial) : base(context)
    {
        Current = initial;
        Alert = new AlertStrip(context);
        Sheet = new QuickSheet(context);
        Bars = new AlertBarsView(context, options);

        // Сцена: сменный экран, полосы тревоги и строка слов поверх него. Всё это принадлежит рамке,
        // а не экрану (слово владельца 05.08.2026 — «полосы не принадлежат экрану и не дублируются
        // на каждом»): панель и плитки сменяются под ними, а полосы стоят.
        //
        // Полоса тревоги — накладка, а не строка вертикальной разметки: членом разметки она отбирала
        // у сцены свою высоту, и появление служебного «ещё раз — выход» сжимало панель и сдвигало её
        // вниз на глазах у райдера. Тот же долг, что план 23 закрыл для полос тревоги; здесь он
        // закрыт до конца. Ценой этого полоса ложится на верх сцены — от системных часов её отводит
        // собственный отступ (<see cref="AlertStrip.TopInset"/>), который ставит хозяин рамки.
        _stage = new FrameLayout(context);
        _stage.SetBackgroundColor(options.Palette.Background);
        _stage.AddView(Current.View, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _stage.AddView(Bars, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _stage.AddView(Alert, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = GravityFlags.Top,
        });

        SetBackgroundColor(options.Palette.Background);
        AddView(_stage, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)
        {
            Gravity = GravityFlags.CenterHorizontal,
        });

        AddView(Sheet, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
    }

    /// <summary>Показанный сейчас экран — тот, которому водитель кадра носит состояние.</summary>
    public IMainScreen Current { get; private set; }

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

        if (_stage.LayoutParameters is { } layout && layout.Width != wanted)
        {
            layout.Width = wanted;
            _stage.LayoutParameters = layout;
        }

        base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
    }
}
