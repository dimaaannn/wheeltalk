using System.Globalization;
using Android.App;
using Android.Content;
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
    public static void Show(Context context, DashboardPalette palette, IMetricHistory history,
        MetricDescriptor metric, string label, string unit, TimeSpan window)
    {
        var to = DateTimeOffset.Now;
        var from = to - window;

        var title = new TextView(context) { Text = label };
        title.SetTextSize(ComplexUnitType.Sp, TilesLayout.ViewerTitleSp);
        title.SetTextColor(palette.Ink);

        // Значение выбранной точки — здесь же, под заголовком: маркер поверх линии закрыл бы сам
        // график, а место под ним всё равно есть.
        var picked = new TextView(context) { Text = "" };
        picked.SetTextSize(ComplexUnitType.Sp, TilesLayout.ViewerPickedSp);
        picked.SetTextColor(palette.Dim);

        var chart = BuildChart(context, palette, from);
        chart.SetOnChartValueSelectedListener(new Selection(entry =>
            picked.Text = $"{from.AddSeconds(entry.GetX()):HH:mm:ss} · "
                + $"{entry.GetY().ToString("F" + metric.Decimals, CultureInfo.InvariantCulture)} {unit}"));

        int pad = context.Dp(TilesLayout.ViewerPaddingDp);
        var root = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        root.SetBackgroundColor(palette.Background);
        root.SetPadding(pad, pad, pad, pad);
        root.AddView(title);
        root.AddView(picked);
        root.AddView(chart, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        var dialog = new Dialog(context, Android.Resource.Style.ThemeBlackNoTitleBarFullScreen);
        dialog.SetContentView(root);
        dialog.Show();

        _ = FillAsync(chart, palette, history, metric.Id, label, from, to);
    }

    private static LineChart BuildChart(Context context, DashboardPalette palette, DateTimeOffset from)
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
        // а миллисекунды суток в него уже не помещаются без потери.
        chart.XAxis.ValueFormatter = new TimeAxis(from);

        chart.AxisLeft!.TextColor = palette.Dim;
        chart.AxisLeft.GridColor = palette.Dim;

        return chart;
    }

    private static async Task FillAsync(LineChart chart, DashboardPalette palette, IMetricHistory history,
        string metricId, string label, DateTimeOffset from, DateTimeOffset to)
    {
        var points = await history.ReadAsync(metricId, from, to, TilesLayout.ViewerPoints, CancellationToken.None);
        if (ChartLine.Build(points, palette, label) is not { } data) return;

        chart.Post(() =>
        {
            chart.Data = data;
            chart.Invalidate();
        });
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

    private sealed class TimeAxis(DateTimeOffset from) : Java.Lang.Object, IAxisValueFormatter
    {
        public string GetFormattedValue(float value, AxisBase? axis) => from.AddSeconds(value).ToString("HH:mm");
    }
}
