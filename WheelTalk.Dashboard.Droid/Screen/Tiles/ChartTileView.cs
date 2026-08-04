using System.Globalization;
using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using Com.Github.Mikephil.Charting.Charts;
using Com.Github.Mikephil.Charting.Data;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Metrics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Плитка вида <see cref="TileKind.Chart"/>: ход величины во времени. Рисует та же библиотека, что и
/// полноэкранный просмотр (решение владельца 04.08.2026) — <c>LineChart</c> с выключенной легендой и
/// жестами. Своя реализация на канве была и снята: два рисовальщика одного и того же расходятся
/// видом, и держать их ради полутора десятков строк незачем.
/// <para>
/// Точки приходят готовыми (<see cref="SetPoints"/>): читает их экран у <see cref="IMetricHistory"/>
/// по таймеру и вне потока отрисовки — запрос к SQLite на кадре это заикание (план 23 §5.6).
/// </para>
/// <para>
/// <b>Число лежит поверх линии</b>, а не под ней: плитка мелкая, и делить её надвое значило бы
/// оставить графику половину высоты.
/// </para>
/// </summary>
internal sealed class ChartTileView : TileView
{
    private readonly FrameLayout _stack;
    private readonly LineChart _chart;
    private readonly TextView _value;
    private readonly TextView _range;

    private MetricDescriptor? _metric;
    private string _format = "F0";
    private string _unit = "";
    private string _shown = "";
    private readonly Func<string, string> _words;

    private bool _zoom;

    /// <param name="words">Ключ ресурса → слово: библиотека ресурсов приложения не видит, слова ей отдаёт экран.</param>
    public ChartTileView(Context context, DashboardPalette palette, Func<string, string> words)
        : base(context, palette)
    {
        _words = words;

        _chart = new LineChart(context);

        // Легенда, описание и рамка на плитке шириной в палец не читаются, а место занимают. Жесты
        // выключены нарочно: короткий тап принадлежит экрану — он открывает полноэкранный просмотр.
        _chart.Description!.Enabled = false;
        _chart.Legend!.Enabled = false;
        _chart.XAxis!.Enabled = false;
        _chart.AxisRight!.Enabled = false;
        _chart.SetTouchEnabled(false);
        _chart.SetNoDataText("");
        _chart.SetDrawGridBackground(false);
        _chart.SetDrawBorders(false);

        // Шкала слева остаётся: без неё линия показывает форму, но не величину — а на плитку смотрят
        // как раз затем, чтобы увидеть, «сколько было».
        _chart.AxisLeft!.Enabled = true;
        _chart.AxisLeft.TextColor = palette.Dim;
        _chart.AxisLeft.TextSize = TilesLayout.ChartAxisSp;
        _chart.AxisLeft.SetLabelCount(TilesLayout.ChartAxisLabels, true);
        _chart.AxisLeft.SetDrawAxisLine(false);
        _chart.AxisLeft.SetDrawGridLines(false);

        _value = new TextView(context) { Gravity = GravityFlags.Center };
        _value.SetTextColor(palette.Ink);
        _value.SetMaxLines(1);
        _value.SetAutoSizeTextTypeUniformWithConfiguration(
            TilesLayout.ValueMinSp, TilesLayout.ChartValueMaxSp, TilesLayout.ValueStepSp, (int)ComplexUnitType.Sp);

        // За какое время нарисована линия — под шкалой, в том же углу: это подпись к шкале, а не к
        // плитке, и стоять она должна там, где кончаются деления.
        _range = new TextView(context);
        _range.SetTextSize(ComplexUnitType.Sp, TilesLayout.ChartRangeSp);
        _range.SetTextColor(palette.Dim);

        _stack = new FrameLayout(context);
        _stack.AddView(_chart, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _stack.AddView(_value, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _stack.AddView(_range, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Bottom | GravityFlags.Start));

        AddView(_stack, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f)
        {
            TopMargin = context.Dp(TilesLayout.ValueTopMarginDp),
        });
    }

    /// <summary>Сколько точек просить у истории: больше, чем пикселей в ширине, рисовать некуда.</summary>
    public int Points => Math.Max(1, Width);

    public void Bind(MetricDescriptor metric, string label, string unit, TileSize size, bool showLabel,
        TileChart options)
    {
        _metric = metric;
        _format = "F" + metric.Decimals;
        _unit = unit;
        _shown = "";
        _zoom = options.Zoom;

        _chart.Data = null;
        _chart.Invalidate();

        _value.Visibility = options.ShowValue ? ViewStates.Visible : ViewStates.Gone;
        _range.Text = Describe(options.Window);

        BindFrame(label, size, showLabel);
        Render(null);
    }

    /// <summary>Свежая история. Зовётся раз в секунду-две, а не на кадр, — потому набор строится заново.</summary>
    public void SetPoints(IReadOnlyList<MetricPoint> points)
    {
        // Масштаб по крайним значениям или от нуля — решает плитка (решение владельца 04.08.2026).
        // У напряжения и температуры размах мал против самого значения, и без обрезки линия у них
        // прямая; у тока и ШИМ, наоборот, важна доля от нуля.
        if (_zoom) _chart.AxisLeft!.ResetAxisMinimum();
        else _chart.AxisLeft!.AxisMinimum = 0f;

        _chart.Data = ChartLine.Build(points, Palette, label: "");
        _chart.Invalidate();
    }

    protected override void ShowContent(bool visible) =>
        _stack.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;

    public override void Render(TelemetrySnapshot? snapshot)
    {
        if (_metric is not { } metric || _value.Visibility != ViewStates.Visible) return;

        string text = MetricNumber.Text(metric, snapshot, _format);

        if (_shown == text) return;

        _shown = text;
        _value.TextFormatted = MetricNumber.Compose(text, _unit, Palette.Dim);
    }

    /// <summary>Окно словами: «15 мин», «3 ч». Слов на это хватает двух, и оба уже переведены.</summary>
    private string Describe(TimeSpan window) => window < TimeSpan.FromHours(1)
        ? $"{window.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture)} {_words("UnitMinutesShort")}"
        : $"{window.TotalHours.ToString("F0", CultureInfo.InvariantCulture)} {_words("UnitHoursShort")}";
}
