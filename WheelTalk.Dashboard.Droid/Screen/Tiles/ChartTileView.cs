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
/// Данные приходят <b>готовым набором</b> (<see cref="ShowData"/>): и чтение истории, и стройка
/// набора идут вне потока отрисовки — запрос к SQLite на кадре это заикание (план 23 §5.6), а
/// стройка тысячи точек за JNI-швом — заикание вдесятеро дороже (план 31 §3.2).
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
    private readonly ChartZonesView _zones;

    private MetricDescriptor? _metric;
    private string _format = "F0";
    private string _unit = "";
    private string _shown = "";
    private readonly Func<string, string> _words;

    private TileLimits? _limits;
    private TileChart _options = new(TimeSpan.FromMinutes(15), ShowValue: true, Zoom: false);

    /// <param name="words">Ключ ресурса → слово: библиотека ресурсов приложения не видит, слова ей отдаёт экран.</param>
    public ChartTileView(Context context, DashboardOptions options, Func<string, string> words)
        : base(context, options)
    {
        _words = words;

        var palette = Palette;

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
        // Деления выбирает библиотека, а не делим диапазон поровну: ровно три части дают некруглые
        // «76 / 38 / 0», а на плитке круглое число дороже точного их счёта.
        _chart.AxisLeft.SetLabelCount(TilesLayout.ChartAxisLabels, false);
        _chart.AxisLeft.SetDrawAxisLine(false);
        _chart.AxisLeft.SetDrawGridLines(false);

        // Число — в правый верхний угол, а не по центру: типичный ход величины идёт через середину
        // плитки, и центрированное число ложилось ровно на линию. Угол свободен — там нет ни шкалы,
        // ни подписи окна.
        _value = new TextView(context) { Gravity = GravityFlags.Top | GravityFlags.End };
        _value.SetTextColor(palette.Ink);
        _value.SetMaxLines(1);
        _value.SetAutoSizeTextTypeUniformWithConfiguration(
            TilesLayout.ValueMinSp, TilesLayout.ChartValueMaxSp, TilesLayout.ValueStepSp, (int)ComplexUnitType.Dip);

        // За какое время нарисована линия — под шкалой, в том же углу: это подпись к шкале, а не к
        // плитке, и стоять она должна там, где кончаются деления.
        _range = new TextView(context);
        _range.SetTextSize(ComplexUnitType.Dip, TilesLayout.ChartRangeSp);
        _range.SetTextColor(palette.Dim);

        // Зоны — поверх графика, но под числом: они подсказка о шкале, а показание важнее.
        _zones = new ChartZonesView(context, _chart, palette);

        _stack = new FrameLayout(context);
        _stack.AddView(_chart, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _stack.AddView(_zones, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _stack.AddView(_value, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _stack.AddView(_range, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Bottom | GravityFlags.Start));

        // Нижний отступ — внутри плитки и за счёт самого графика: линия и подпись времени
        // упирались в край, и низ графика читался как рамка плитки, стоящей ниже.
        AddView(_stack, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f)
        {
            TopMargin = context.Dp(TilesLayout.ValueTopMarginDp),
            BottomMargin = context.Dp(TilesLayout.ChartBottomDp),
        });
    }

    /// <summary>
    /// Сколько точек влезет в линию: больше, чем пикселей в ширине, рисовать некуда.
    /// <para>
    /// <b>Это не то же, что число корзин запроса.</b> История отдаёт из каждой корзины минимум и
    /// максимум (план 23 §5.6), то есть до двух точек на корзину, — и плитка шириной 700 px,
    /// попросившая 700 корзин, получала 1334 точки (замер 10.08.2026). Пересчёт корзин делает тот,
    /// кто спрашивает: <see cref="PointsPerBucket"/>.
    /// </para>
    /// </summary>
    public int Points => Math.Max(1, Width);

    /// <summary>Сколько точек приходит из одной корзины истории: минимум и максимум.</summary>
    public const int PointsPerBucket = 2;

    public void Bind(MetricDescriptor metric, string label, string unit, TileSize size, bool showLabel,
        TileChart options, TileLimits? limits, bool heatBar, int? decimals)
    {
        _metric = metric;
        _format = MetricRounding.Format(metric, decimals);
        _unit = unit;
        _shown = "";
        _options = options;

        // Шкала слева — по выбору плитки: на четвертной она съедает треть ширины, а на широкой без
        // неё линия показывает форму, но не величину.
        _chart.AxisLeft!.Enabled = options.Axis;

        _chart.Data = null;
        _chart.Invalidate();
        _limits = limits;
        _zones.Limits = MetricHeat.Limits(metric.Id, Options, limits);

        _value.Visibility = options.ShowValue ? ViewStates.Visible : ViewStates.Gone;
        _range.Text = Describe(options.Window);

        BindFrame(label, size, showLabel,
            heatBar && MetricHeat.Limits(metric.Id, Options, limits) is not null);
        Render(null);
    }

    /// <summary>
    /// Свежая история за окно <paramref name="from"/>…<paramref name="to"/>. Зовётся раз в
    /// секунду-две, а не на кадр, — потому набор строится заново.
    /// <para>
    /// <b>Окно едет, а не растёт.</b> Границы по времени ставятся здесь и всегда, независимо от
    /// того, докуда дотянулись данные: кончились они раньше срока — линия просто уезжает влево, а
    /// справа остаётся пустота. Без этого библиотека растягивала бы диапазон по пришедшим точкам, и
    /// правый край липнул бы к «сейчас», пока левый уползает за экран.
    /// </para>
    /// </summary>
    /// <summary>
    /// Вручить <b>готовый</b> набор данных. Главному потоку здесь остаётся только то, что вне его
    /// сделать нельзя: границы осей, подмена <c>Data</c> и перерисовка.
    /// <para>
    /// Стройка набора (<see cref="ChartLine.Build"/> — по объекту-точке за JNI-швом, до тысячи с
    /// лишним точек) живёт у того, кто читал историю, и идёт вне потока отрисовки: замер на
    /// устройстве 10.08.2026 дал 115–370 мс на главном потоке каждые полторы секунды — от семи до
    /// двадцати пропущенных кадров подряд, и в прокрутке, и в покое (план 31 §3.2).
    /// </para>
    /// </summary>
    public void ShowData(LineData? data, DateTimeOffset from, DateTimeOffset to)
    {
        // Масштаб по крайним значениям или от нуля — решает плитка (решение владельца 04.08.2026).
        // У напряжения и температуры размах мал против самого значения, и без обрезки линия у них
        // прямая; у тока и ШИМ, наоборот, важна доля от нуля.
        if (_options.Zoom) _chart.AxisLeft!.ResetAxisMinimum();
        else _chart.AxisLeft!.AxisMinimum = 0f;

        _chart.XAxis!.AxisMinimum = 0f;
        _chart.XAxis.AxisMaximum = (float)(to - from).TotalSeconds;

        _chart.Data = data;
        _chart.Invalidate();

        // Зоны перерисовываются следом: шкала могла сдвинуться вместе с новыми точками.
        _zones.Invalidate();
    }

    protected override void ShowContent(bool visible) =>
        _stack.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;

    /// <summary>
    /// Очередной снимок: число поверх линии и нагрев подложки по нему же (решение владельца
    /// 05.08.2026). Прежний довод — «плитка рассказывает про четверть часа, а жар про мгновение» —
    /// снят владельцем: тревога должна быть видна на всякой плитке, где показано текущее значение, и
    /// молчащая среди греющихся читается как «здесь всё хорошо».
    /// </summary>
    public override void Render(TelemetrySnapshot? snapshot)
    {
        if (_metric is not { } metric) return;

        double? value = MetricNumber.Value(metric, snapshot);
        ShowHeat(MetricHeat.Of(metric.Id, value, Options, _limits));

        if (_value.Visibility != ViewStates.Visible) return;

        string text = MetricNumber.Text(value, _format);

        if (_shown == text) return;

        _shown = text;
        // Кегль единицы у графика прежний — доля от кегля числа, которое здесь подпись к линии, а
        // не главное на плитке (потолок ChartValueMaxSp). Пол в 11 sp сюда не приезжает: он живёт в
        // подборе плиток значения, где за него платит число.
        _value.TextFormatted = MetricNumber.Compose(text, _unit, Palette.Dim,
            (int)Math.Round((double)Context!.Dp(TilesLayout.ChartValueMaxSp * TilesLayout.UnitScale)));
    }

    /// <summary>Окно словами: «15 мин», «3 ч». Слов на это хватает двух, и оба уже переведены.</summary>
    private string Describe(TimeSpan window) => window < TimeSpan.FromHours(1)
        ? $"{window.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture)} {_words("UnitMinutesShort")}"
        : $"{window.TotalHours.ToString("F0", CultureInfo.InvariantCulture)} {_words("UnitHoursShort")}";
}
