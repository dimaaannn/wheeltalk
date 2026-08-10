using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Metrics;
using WheelTalk.Core.Tiles;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Плитка вида <see cref="TileKind.Trip"/>: путь, пройденный с точки, которую поставил человек
/// (решение владельца 10.08.2026). Считается из общего пробега колеса — <c>одометр − точка</c>.
/// <para>
/// <b>Не сбрасывается ничем, кроме руки хозяина.</b> Ни сменой колеса, ни новой поездкой, ни
/// перезапуском приложения: точка живёт в хранилище по паре «колесо + плитка», и вернувшийся к
/// прежнему колесу продолжает прежний счёт. Этим вид и отличается от крайнего значения, которое
/// смену колеса как раз обязано забывать.
/// </para>
/// <para>
/// <b>Плиток может стоять несколько</b>, и точки у них разные: «с последнего ТО» и «за сегодня»
/// считают один и тот же одометр. Различает их устойчивое имя плитки (<see cref="MetricTile.Id"/>),
/// а называет — своя подпись.
/// </para>
/// </summary>
internal sealed class TripTileView : TileView
{
    /// <summary>
    /// Знаков после запятой, пока плитке не задали своих. Одометр показывается целыми — тысячи
    /// километров, — но дистанция это не одометр, а пробег, и читается она как пробег: десятыми.
    /// </summary>
    private const int DefaultDecimals = 1;

    private readonly TextView _value;
    private readonly Func<string> _wheel;

    private MetricDescriptor? _metric;
    private TripPoints? _points;
    private string _tile = "";
    private string _format = "F1";
    private string _unit = "";
    private string _shown = "";
    private double? _odometer;
    private int _unitPx = 11;

    /// <summary>Сколько знаков в худшей строке этой плитки — по нему число и встаёт неподвижно.</summary>
    private Func<int> _box = () => 0;

    public TripTileView(Context context, DashboardOptions options, Func<string> wheel)
        : base(context, options)
    {
        _wheel = wheel;

        _value = new TextView(context) { Gravity = GravityFlags.Center };
        _value.SetTextColor(Palette.Ink);
        _value.SetMaxLines(1);
        _value.SetTypeface(PaintRuler.Mono, Android.Graphics.TypefaceStyle.Normal);
        _value.SetTextSize(ComplexUnitType.Dip, TilesLayout.ValueMinSp);

        AddView(_value, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f)
        {
            TopMargin = context.Dp(TilesLayout.ValueTopMarginDp),
        });
    }

    /// <summary>Сбрасывать дистанцию есть чем — этим и живёт пункт «Сбросить» в меню плитки.</summary>
    public override bool CanReset => true;

    public void Bind(MetricDescriptor metric, string label, string unit, TileSize size, bool showLabel,
        TileLimits? limits, TileTypeface face, bool heatBar, int? decimals, string tile, TripPoints points,
        Func<int> box)
    {
        _box = box;
        _metric = metric;
        _points = points;
        _tile = tile;
        _format = MetricRounding.Format(metric, decimals ?? DefaultDecimals);
        _unit = TileTypography.UnitOn(new TileClass(size.Columns, size.Rows), unit);
        _shown = "";

        BindFrame(label, size, showLabel,
            heatBar && MetricHeat.Limits(metric.Id, Options, limits) is not null);
        ApplyForm(face.Form, size);

        _value.SetTextSize(ComplexUnitType.Dip, face.ValueSp);
        _value.Gravity = face.Form == TileForm.Row
            ? GravityFlags.End | GravityFlags.CenterVertical
            : GravityFlags.Center;
        _unitPx = (int)Math.Round((double)Context!.Dp(face.UnitSp));

        if (_value.LayoutParameters is LinearLayout.LayoutParams layout)
        {
            layout.Width = face.Form == TileForm.Row ? 0 : ViewGroup.LayoutParams.MatchParent;
            layout.Height = face.Form == TileForm.Row ? ViewGroup.LayoutParams.MatchParent : 0;
            layout.Weight = 1f;
            layout.TopMargin = face.Form == TileForm.Row ? 0 : Context.Dp(TilesLayout.ValueTopMarginDp);

            int bleed = face.Form == TileForm.Square ? -Context.Dp(TilesLayout.ValueBleedDp) : 0;
            layout.LeftMargin = face.Form == TileForm.Row ? Context.Dp(TilesLayout.RowGapDp) : bleed;
            layout.RightMargin = face.Form == TileForm.Row ? 0 : bleed;
            _value.LayoutParameters = layout;
        }

        Render(null);
    }

    /// <summary>
    /// Начать счёт заново — из меню плитки, а не тапом: путь копится неделями, и терять его от
    /// касания в кармане нельзя. Колесо молчит — сбрасывать не от чего, точка остаётся прежней.
    /// </summary>
    public override void ResetValue()
    {
        if (_points is not { } points || _odometer is not { } odometer || Wheel() is not { } wheel) return;

        points.Reset(wheel, _tile, odometer);
        _shown = "";
        Render(null);
    }

    protected override void ShowContent(bool visible) =>
        _value.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;

    protected override TextView? Content => _value;

    /// <summary>
    /// Очередной снимок. Молчащий одометр — прочерк: без него дистанцию не из чего вычесть, а ноль
    /// на его месте читался бы как «сегодня никуда не ездили».
    /// </summary>
    public override void Render(TelemetrySnapshot? snapshot)
    {
        if (_metric is not { } metric || _points is not { } points) return;

        if (snapshot is not null) _odometer = MetricNumber.Value(metric, snapshot);

        double? passed = _odometer is { } odometer && Wheel() is { } wheel
            ? points.Since(wheel, _tile, odometer)
            : null;

        string text = NumberBox.Fit(MetricNumber.Text(passed, _format), _box());
        if (_shown == text) return;

        _shown = text;
        _value.TextFormatted = MetricNumber.Compose(text, _unit, Palette.Dim, _unitPx);
        Measured(metric.Id, text);
        ShowMuted(passed is null);
    }

    /// <summary>
    /// К какому колесу отнести счёт. Пусто — колесо не выбрано вовсе, и дистанции взяться неоткуда:
    /// сложить чужие пути в одну кучу хуже, чем показать прочерк.
    /// </summary>
    private string? Wheel() => _wheel() is { Length: > 0 } wheel ? wheel : null;
}
