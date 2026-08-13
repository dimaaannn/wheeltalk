using System.Globalization;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using Android.Widget;
using Com.Github.Mikephil.Charting.Charts;
using Com.Github.Mikephil.Charting.Components;
using Com.Github.Mikephil.Charting.Data;
using Com.Github.Mikephil.Charting.Formatter;
using Com.Github.Mikephil.Charting.Highlight;
using Com.Github.Mikephil.Charting.Listener;
using WheelTalk.Core.Metrics;
using WheelTalk.Core.Tiles;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Полноэкранный просмотр графика — то, ради чего взята чужая библиотека (план 23 §4): зум,
/// попадание пальцем в ближайшую точку из тысяч и маркер, не уезжающий за край. Мелкая плитка
/// рисует линию сама (<see cref="ChartTileView"/>), а движок осей и жестов живёт только здесь.
/// <para>
/// Открывается <b>коротким</b> нажатием по плитке (решение владельца 04.08.2026 — долгий тап
/// отменён в пользу короткого): тот, кто хотел посмотреть, не должен попадать в правку раскладки.
/// </para>
/// <para>
/// Диалогом, а не отдельной <c>Activity</c>: экран живёт в библиотеке, и своя активность
/// потребовала бы записи в манифесты обоих приложений — боевого и стенда.
/// </para>
/// </summary>
internal static class ChartViewer
{
    /// <param name="decimals">
    /// Округление той плитки, с которой сюда пришли: просмотр открыт по ней, и показывать в нём
    /// величину подробнее, чем на самой плитке, значило бы дать два ответа на один вопрос.
    /// </param>
    /// <returns>
    /// Показанный диалог — <b>хозяину экрана</b>. Просмотр живёт на окне активности, и брошенный
    /// без присмотра он утекает вместе со всей своей ветвью, графиком и данными: 10.08.2026
    /// активность была уничтожена с открытым просмотром, и в дампе владельца остался
    /// <c>WindowLeaked</c> со стеком ровно от тапа по плитке. Закрывает его тот, кто открыл
    /// (<see cref="TilesScreen"/>), когда экран уходит из окна.
    /// </returns>
    public static Dialog Show(Context context, DashboardOptions dashboard, IMetricHistory history,
        MetricDescriptor metric, string label, string unit, TileChart options, TileLimits? limits,
        int? decimals)
    {
        string format = MetricRounding.Format(metric, decimals);

        var palette = dashboard.Palette;
        var to = DateTimeOffset.Now;
        var from = to - options.Window;

        // С единицей: без неё «ШИМ» не говорит, проценты это или что-то ещё, а узнаётся
        // она только тапом по точке.
        var title = new TextView(context) { Text = unit.Length == 0 ? label : $"{label}, {unit}" };
        title.SetTextSize(ComplexUnitType.Dip, TilesLayout.ViewerTitleSp);
        title.SetTextColor(palette.Ink);

        // Значение выбранной точки — здесь же, под заголовком: маркер поверх линии закрыл бы сам
        // график, а место под ним всё равно есть.
        var picked = new TextView(context) { Text = "" };
        picked.SetTextSize(ComplexUnitType.Dip, TilesLayout.ViewerPickedSp);
        picked.SetTextColor(palette.Dim);

        var chart = BuildChart(context, palette, from, options.Window);

        // Место точки на оси проверяется тем же счётом, что и метки оси (AxisTime): выбор приходит
        // из той же чужой библиотеки и той же природы — до первой отрисовки в нём бывает пусто.
        chart.SetOnChartValueSelectedListener(new Selection(entry =>
            picked.Text = AxisTime.At(from, entry.GetX(), options.Window.TotalSeconds) is { } at
                ? $"{at:HH:mm:ss} · {entry.GetY().ToString(format, CultureInfo.InvariantCulture)} {unit}"
                : ""));

        int pad = context.Dp(TilesLayout.ViewerPaddingDp);
        var root = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        root.SetBackgroundColor(palette.Background);
        root.SetPadding(pad, pad, pad, pad);
        root.AddView(title);
        root.AddView(picked);
        // Зоны — тем же элементом, что на плитке: одно правило показа порога на обоих экранах.
        var zones = new ChartZonesView(context, chart, palette)
        {
            Limits = MetricHeat.Limits(metric.Id, dashboard, limits),
        };

        var stack = new FrameLayout(context);
        stack.AddView(chart, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        stack.AddView(zones, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        root.AddView(stack, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        var dialog = new Dialog(context, Android.Resource.Style.ThemeBlackNoTitleBarFullScreen);
        dialog.SetContentView(root);

        // Заполнение живёт ровно столько, сколько окно. История читается из базы и возвращается
        // через десятки миллисекунд, а то и позже закрытия просмотра — и тогда рисовать некуда: за
        // графиком стоят peer-объекты уничтоженной активности, а сама задача держит их живыми.
        var alive = new CancellationTokenSource();
        dialog.DismissEvent += (_, _) => alive.Cancel();
        dialog.Show();

        _ = FillAsync(context, chart, zones, dashboard, history, metric, label, from, to, options, limits,
            alive.Token);

        return dialog;
    }

    private static LineChart BuildChart(Context context, DashboardPalette palette, DateTimeOffset from,
        TimeSpan window)
    {
        var chart = new LineChart(context);

        // Ничего, кроме линии и осей: подпись величины уже стоит заголовком, а легенда из одного
        // пункта повторяет её же.
        chart.Description!.Enabled = false;
        chart.Legend!.Enabled = false;
        chart.AxisRight!.Enabled = false;
        chart.SetNoDataText("");

        chart.XAxis!.Position = XAxis.XAxisPosition.Bottom;
        chart.XAxis.TextColor = palette.Dim;
        chart.XAxis.SetDrawGridLines(false);
        // Ось времени: по X лежат секунды от начала окна, а не unix-время — float считает их точно,
        // а миллисекунды суток в него уже не помещаются без потери. Окно ось знает: по нему она и
        // отличает свою метку от мусора, который приносит чтение вслух.
        chart.XAxis.ValueFormatter = new TimeAxis(from, window.TotalSeconds);

        // Окно задано временем, а не данными: там, где истории нет, остаётся пустое место, и видно,
        // что её нет, — вместо растянутого по остаткам графика.
        chart.XAxis.AxisMinimum = 0f;
        chart.XAxis.AxisMaximum = (float)window.TotalSeconds;

        chart.AxisLeft!.TextColor = palette.Dim;
        chart.AxisLeft.GridColor = Color.Argb(
            TilesLayout.ViewerGridAlpha, palette.Dim.R, palette.Dim.G, palette.Dim.B);

        return chart;
    }

    /// <param name="alive">
    /// Пока просмотр открыт. Отменён — работать больше не над чем и трогать нечего: чтение
    /// прерывается, а догнавшее продолжение молча уходит.
    /// </param>
    private static async Task FillAsync(Context context, LineChart chart, ChartZonesView zones,
        DashboardOptions dashboard, IMetricHistory history, MetricDescriptor metric, string label,
        DateTimeOffset from, DateTimeOffset to, TileChart options, TileLimits? limits,
        CancellationToken alive)
    {
        IReadOnlyList<MetricPoint> points;
        try
        {
            points = await history.ReadAsync(metric.Id, from, to, TilesLayout.ViewerPoints, alive);
        }
        catch (OperationCanceledException)
        {
            // Просмотр закрыли, пока читалась история: обычный конец задачи, а не беда.
            return;
        }

        if (alive.IsCancellationRequested) return;

        if (ChartLine.Build(points, dashboard.Palette, label, from, options) is not { } data) return;

        // Обрезка по крайним значениям — та же, что у плитки: иначе ось уходит ниже нуля, и заливка
        // рисует полосу под ним во всю ширину экрана.
        if (options.Zoom)
        {
            // Пол оси — свойство величины: ниже пола рисовать нечего, а авто-отступ уводил заливку
            // под ноль (владелец 11.08.2026).
            if (metric.Floor is { } floor) chart.AxisLeft!.AxisMinimum = Math.Max((float)floor, data.YMin);
            else chart.AxisLeft!.ResetAxisMinimum();
        }
        else chart.AxisLeft!.AxisMinimum = 0f;

        // Пороги чертой поперёк графика: видно, докуда дотянулся пик, не прикладывая палец к точкам.
        // На плитке их нет намеренно — там две черты заняли бы треть высоты и стали бы шумом.
        chart.AxisLeft.RemoveAllLimitLines();
        // Черта — на каждую метку, какая есть: они независимы, и одинокая жёлтая законна
        // (решение владельца 11.08.2026).
        if (MetricHeat.Limits(metric.Id, dashboard, limits) is { } marks)
        {
            if (marks.Warn is { } warn)
            {
                chart.AxisLeft.AddLimitLine(Mark(context, (float)warn, dashboard.Palette.Caution));
            }

            if (marks.Danger is { } danger)
            {
                chart.AxisLeft.AddLimitLine(Mark(context, (float)danger, dashboard.Palette.Danger));
            }
        }

        chart.Post(() =>
        {
            if (alive.IsCancellationRequested) return;

            chart.Data = data;
            chart.Invalidate();
            zones.Invalidate();
        });
    }

    /// <summary>
    /// Черта порога. Пунктиром и без подписи: подпись повторила бы число, которое и так стоит на
    /// шкале слева, а сплошная линия спорила бы с самими данными.
    /// </summary>
    private static LimitLine Mark(Context context, float at, Color color)
    {
        var line = new LimitLine(at)
        {
            LineColor = color,
            LineWidth = TilesLayout.ChartStrokeDp / 2f,
        };

        line.EnableDashedLine(context.Dp(TilesLayout.LimitDashDp), context.Dp(TilesLayout.LimitDashDp), 0);

        return line;
    }

    /// <summary>Тап по точке — значение и время, когда она записана (план 23 §3.2).</summary>
    private sealed class Selection(Action<Entry> picked) : Java.Lang.Object, IOnChartValueSelectedListener
    {
        public void OnValueSelected(Entry? entry, Highlight? highlight)
        {
            if (entry is not null) picked(entry);
        }

        public void OnNothingSelected()
        {
        }
    }

    /// <summary>
    /// Подпись метки оси — время. Спрашивают её не только за нарисованные метки: система доступности
    /// читает график вслух (<c>dispatchPopulateAccessibilityEvent</c> при открытии окна) и просит
    /// подписать значения, когда рисовать ещё нечего, — а в пустой видимой области у чужой
    /// библиотеки лежат крайние числа float. Такой метке на оси места нет, и подписи у неё нет тоже:
    /// пустая строка вместо краха (телефон владельца, сборка 20).
    /// </summary>
    private sealed class TimeAxis(DateTimeOffset from, double windowSeconds) : Java.Lang.Object, IAxisValueFormatter
    {
        public string GetFormattedValue(float value, AxisBase? axis) =>
            AxisTime.At(from, value, windowSeconds) is { } at ? at.ToString("HH:mm") : "";
    }
}
