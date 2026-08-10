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
/// <b>Сброс — из меню плитки</b> (решение владельца 10.08.2026). Прежде его делал голый тап, и это
/// снято: меню одно на все виды, а пик, стёртый касанием в кармане, не восстановить — он копился
/// всю поездку.
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

    /// <summary>Сколько знаков в худшей строке этой плитки — по нему число и встаёт неподвижно.</summary>
    private Func<int> _box = () => 0;

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
        TileExtremum options, TileLimits? limits, TileTypeface face, bool heatBar, int? decimals,
        Func<int> box)
    {
        _box = box;

        // Величина или сторона сменились — прежнее крайнее к ним не относится: максимум тока не
        // может стать минимумом напряжения.
        if (_metric?.Id != metric.Id || _lowest != options.Lowest) _extremum = null;

        _metric = metric;
        _format = MetricRounding.Format(metric, decimals);
        _unit = TileTypography.UnitOn(new TileClass(size.Columns, size.Rows), unit);
        _lowest = options.Lowest;
        _limits = limits;
        _shown = "";

        // Пометка ▲▼ — перед подписью и крупнее её (решение владельца 11.08.2026): у крайних своё
        // поведение, и узнаваться они обязаны с одного взгляда. Место под неё учтено в подборе
        // кегля (TilesLayout.MarkDp).
        BindFrame(label, size, showLabel,
            heatBar && MetricHeat.Limits(metric.Id, Options, limits) is not null);
        MarkLabel(options.Lowest ? "▼" : "▲", label);
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

    /// <summary>Пик есть чем сбросить — этим и живёт пункт «Сбросить» в меню плитки.</summary>
    public override bool CanReset => true;

    /// <summary>
    /// Забыть накопленное и начать заново — с ближайшего же отсчёта. Зовётся из меню плитки и при
    /// смене колеса: максимум прежнего колеса ничего не говорит о новом.
    /// </summary>
    public override void ResetValue()
    {
        _extremum = null;
        _shown = "";
        Render(null);
    }

    protected override void ShowContent(bool visible) =>
        _value.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;

    protected override TextView? Content => _value;

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

        string text = NumberBox.Fit(MetricNumber.Text(_extremum, _format), _box());

        if (_shown == text) return;

        _shown = text;
        _value.TextFormatted = MetricNumber.Compose(text, _unit, Palette.Dim, _unitPx);
        Measured(metric.Id, text);
        ShowMuted(_extremum is null);
        ShowHeat(MetricHeat.Of(metric.Id, _extremum, Options, _limits),
            MetricHeat.Limits(metric.Id, Options, _limits));
    }
}
