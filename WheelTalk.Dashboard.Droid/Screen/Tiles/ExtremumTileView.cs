using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Metrics;
using WheelTalk.Core.Tiles;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Плитка вида <see cref="TileKind.Extremum"/>: самое большое или самое малое, что величина показала
/// с последнего сброса (план 23 §3.2).
/// <para>
/// <b>Истории не спрашивает вовсе.</b> Крайнему значению нужна не запись, а память о двух числах,
/// поэтому плитка живёт и там, где телеметрия не пишется, — а при выключенной записи графики пусты.
/// Этим же она отличается от «максимума» из каталога: тот считает колесо и сбрасывает по своим
/// правилам, а этот — наш, и сбрасывается когда попросят.
/// </para>
/// <para>
/// <b>Сброс — короткий тап по плитке</b> вне режима правки. Жест выбран потому, что он у этого вида
/// единственный свободный: у графика им открывают полноэкранный просмотр, долгий занят правкой
/// раскладки. Случайное касание не разрушительно — крайнее наберётся снова из живого потока.
/// </para>
/// </summary>
internal sealed class ExtremumTileView : TileView
{
    private readonly TextView _value;

    private MetricDescriptor? _metric;
    private string _format = "F0";
    private string _unit = "";
    private string _shown = "";
    private bool _lowest;
    private double? _extremum;
    private TileLimits? _limits;
    private int _unitPx = 11;

    public ExtremumTileView(Context context, DashboardOptions options) : base(context, options)
    {
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

    public void Bind(MetricDescriptor metric, string label, string unit, TileSize size, bool showLabel,
        TileExtremum options, TileLimits? limits, TileTypeface face, bool heatBar, int? decimals)
    {
        // Величина или сторона сменились — прежнее крайнее к ним не относится: максимум тока не
        // может стать минимумом напряжения.
        if (_metric?.Id != metric.Id || _lowest != options.Lowest) _extremum = null;

        _metric = metric;
        _format = MetricRounding.Format(metric, decimals);
        _unit = TileTypography.UnitOn(new TileClass(size.Columns, size.Rows), unit);
        _lowest = options.Lowest;
        _limits = limits;
        _shown = "";

        // Пометка ▲▼ рядом с подписью: у крайних своё поведение — тап их сбрасывает, — и выглядеть
        // они обязаны иначе (план плиток §5). Место под неё уже учтено в подборе кегля.
        BindFrame($"{label} {(options.Lowest ? "▼" : "▲")}", size, showLabel,
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

            // Поля ужимает только квадрат — ровно на столько, на сколько его расширил подбор
            // кегля (ValueBleedDp). Прямоугольные породы живут со своими полями, как их приняли.
            int bleed = face.Form == TileForm.Square ? -Context.Dp(TilesLayout.ValueBleedDp) : 0;
            layout.LeftMargin = face.Form == TileForm.Row ? Context.Dp(TilesLayout.RowGapDp) : bleed;
            layout.RightMargin = face.Form == TileForm.Row ? 0 : bleed;
            _value.LayoutParameters = layout;
        }

        Render(null);
    }

    /// <summary>Забыть накопленное и начать заново — с ближайшего же отсчёта.</summary>
    public void Reset()
    {
        _extremum = null;
        _shown = "";
        Render(null);
    }

    protected override void ShowContent(bool visible) =>
        _value.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;

    /// <summary>
    /// Очередной снимок. Молчание величины крайнего не двигает: <c>null</c> значит «колесо этого не
    /// сообщает», и принять его за ноль означало бы записать в минимум показание, которого не было.
    /// </summary>
    public override void Render(TelemetrySnapshot? snapshot)
    {
        if (_metric is not { } metric) return;

        if (MetricNumber.Value(metric, snapshot) is { } number)
        {
            _extremum = _extremum is not { } kept ? number
                : _lowest ? Math.Min(kept, number)
                : Math.Max(kept, number);
        }

        string text = MetricNumber.Text(_extremum, _format);

        if (_shown == text) return;

        _shown = text;
        _value.TextFormatted = MetricNumber.Compose(text, _unit, Palette.Dim, _unitPx);
        ShowMuted(_extremum is null);
        ShowHeat(MetricHeat.Of(metric.Id, _extremum, Options, _limits));
    }
}
