using Android.Content;
using Android.Views;
using Android.Widget;
using WheelTalk.Dashboard.Droid.Layouts;

namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Визуальная композиция главного экрана целиком: полоса тревоги колеса, панель
/// <see cref="TwinTapesDashboard"/> и шторка быстрых команд поверх. Живёт в библиотеке, а не в
/// приложении, чтобы стенд показывал ровно тот экран, который видит райдер, — тем же классом, а не
/// похожей копией (тот же ход, которым плашка связи и точка записи раньше переехали внутрь панели).
/// Приложению остаётся проводка: данные, команды шторки, инсеты и жесты.
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

    public MainScreenView(Context context, DashboardOptions options) : base(context)
    {
        Dashboard = new TwinTapesDashboard(context, options);
        Alert = new AlertStrip(context);
        Sheet = new QuickSheet(context);

        var content = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        content.SetBackgroundColor(options.Palette.Background);
        content.AddView(Alert, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        content.AddView(Dashboard, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        SetBackgroundColor(options.Palette.Background);
        _content = content;
        AddView(content, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)
        {
            Gravity = GravityFlags.CenterHorizontal,
        });

        AddView(Sheet, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
    }

    public TwinTapesDashboard Dashboard { get; }

    public AlertStrip Alert { get; }

    public QuickSheet Sheet { get; }

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
