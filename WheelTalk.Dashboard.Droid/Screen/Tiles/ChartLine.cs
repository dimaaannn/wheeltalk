using Com.Github.Mikephil.Charting.Data;
using WheelTalk.Core.Metrics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Точки истории → набор данных библиотеки. Общее у плитки и полноэкранного просмотра: линия должна
/// выглядеть одинаково в обоих местах, а разойтись двум её описаниям — дело одной правки.
/// </summary>
internal static class ChartLine
{
    public static LineData? Build(IReadOnlyList<MetricPoint> points, DashboardPalette palette, string label)
    {
        if (points.Count < 2) return null;

        long startMs = points[0].AtMs;
        var entries = new List<Entry>(points.Count);
        // По X — секунды от первой точки, а не unix-время: библиотека считает во float, и миллисекунды
        // суток в него уже не помещаются без потери.
        foreach (var point in points) entries.Add(new Entry((point.AtMs - startMs) / 1000f, (float)point.Value));

        // Линия и заливка — краской спокойной шкалы, а не основной: поверх графика лежит число, и
        // белое по белому не читается ни при какой густоте. Тот же цвет носят ленты панели, так что
        // экран говорит её языком, а не заводит свой.
        var set = new LineDataSet(entries, label)
        {
            Color = palette.Calm,
            LineWidth = TilesLayout.ChartStrokeDp,
            HighLightColor = palette.Ink,
            FillColor = palette.Calm,
            FillAlpha = TilesLayout.ChartFillAlpha,
        };

        set.SetDrawCircles(false);
        set.SetDrawValues(false);
        set.SetDrawFilled(TilesLayout.ChartFillAlpha > 0);

        // Набор добавляется отдельным вызовом: конструктор LineData принимает варарг из интерфейсов,
        // а такие сигнатуры генератор обёрток не переносит.
        var data = new LineData();
        data.AddDataSet(set);

        return data;
    }
}
