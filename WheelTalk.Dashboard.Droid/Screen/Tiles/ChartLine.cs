using Com.Github.Mikephil.Charting.Data;
using WheelTalk.Core.Metrics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Точки истории → набор данных библиотеки. Общее у плитки и полноэкранного просмотра: линия должна
/// выглядеть одинаково в обоих местах, а разойтись двум её описаниям — дело одной правки.
/// </summary>
internal static class ChartLine
{
    /// <param name="from">
    /// Начало окна. По X откладываются секунды <b>от него</b>, а не от первой пришедшей точки: иначе
    /// окно, у которого данные кончились раньше срока, растягивается по ним — правый край липнет к
    /// «сейчас», а левый уползает за экран. Время должно ехать, а не сжиматься под данные.
    /// </param>
    public static LineData? Build(IReadOnlyList<MetricPoint> points, DashboardPalette palette, string label,
        DateTimeOffset from, TileChart options)
    {
        var shown = Smooth(points, options.Smoothing);
        if (shown.Count < 2) return null;

        var data = new LineData();
        long startMs = from.ToUnixTimeMilliseconds();

        // Каждый кусок — своим набором: одним линия протянулась бы через ночь между поездками ровной
        // прямой, и на суточном окне это было бы прямой неправдой — «ехал всю ночь на этих ваттах».
        foreach (var piece in Pieces(shown))
        {
            var entries = new List<Entry>(piece.Count);
            // По X — секунды, а не unix-время: библиотека считает во float, и миллисекунды суток в
            // него уже не помещаются без потери.
            foreach (var point in piece) entries.Add(new Entry((point.AtMs - startMs) / 1000f, (float)point.Value));

            // Линия одноцветная — краской спокойной шкалы. Тревожное в ней не красится: «опасно» это
            // свойство шкалы, а не линии, и рисуется зоной поверх графика (ChartZonesView). Поцветный
            // список точек, который стоял тут раньше, заставлял библиотеку рисовать линию тысячей
            // сегментов, и на плитке шириной в палец она пропадала кусками.
            //
            // Краска спокойная, а не основная, потому что поверх графика лежит число: белое по белому
            // не читается ни при какой густоте. Тот же цвет носят ленты панели.
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
            set.SetDrawFilled(options.Fill && TilesLayout.ChartFillAlpha > 0);

            // Набор добавляется отдельным вызовом: конструктор LineData принимает варарг из
            // интерфейсов, а такие сигнатуры генератор обёрток не переносит.
            data.AddDataSet(set);
        }

        return data;
    }

    /// <summary>
    /// Разрезать историю там, где её нет. Дыра — промежуток заметно больший обычного шага отсчётов:
    /// колесо выключили, приложение закрыли, между поездками прошла ночь.
    /// <para>
    /// Шаг берётся из самих данных — по самому частому расстоянию между соседями, — а не из ширины
    /// окна: прореживание корзинами делает его разным на разных окнах, и одной меры на все не бывает.
    /// </para>
    /// </summary>
    private static IEnumerable<IReadOnlyList<MetricPoint>> Pieces(IReadOnlyList<MetricPoint> points)
    {
        long step = Step(points);
        long limit = step * TilesLayout.ChartGapSteps;

        int start = 0;
        for (int index = 1; index <= points.Count; index++)
        {
            bool broken = index == points.Count || points[index].AtMs - points[index - 1].AtMs > limit;
            if (!broken) continue;

            // Кусок в одну точку рисовать нечем — линии из неё не выйдет, а кружки выключены.
            if (index - start >= 2) yield return Slice(points, start, index - start);
            start = index;
        }
    }

    /// <summary>Обычный шаг отсчётов — медиана: среднее увело бы как раз та дыра, которую ищем.</summary>
    private static long Step(IReadOnlyList<MetricPoint> points)
    {
        var gaps = new List<long>(points.Count - 1);
        for (int index = 1; index < points.Count; index++) gaps.Add(points[index].AtMs - points[index - 1].AtMs);

        gaps.Sort();

        return Math.Max(1, gaps[gaps.Count / 2]);
    }

    private static IReadOnlyList<MetricPoint> Slice(IReadOnlyList<MetricPoint> points, int from, int count)
    {
        var piece = new List<MetricPoint>(count);
        for (int index = from; index < from + count; index++) piece.Add(points[index]);

        return piece;
    }

    /// <summary>
    /// Какой стороной периода рисовать. История приходит корзинами, и в каждой лежат минимум и
    /// максимум (план 23 §5.6): «только пики» и «только провалы» — это выбор одной из двух точек, а
    /// не пересчёт. Оттого сглаживание здесь честное — оно выбрасывает половину данных, а не
    /// придумывает промежуточные.
    /// </summary>
    private static IReadOnlyList<MetricPoint> Smooth(IReadOnlyList<MetricPoint> points, ChartSmoothing smoothing)
    {
        if (smoothing == ChartSmoothing.MinMax || points.Count < 2) return points;

        var kept = new List<MetricPoint>(points.Count / 2 + 1);
        // Корзина отдаёт минимум первым, максимум вторым, и второй появляется только когда они
        // разошлись; поэтому пара ищется по времени, а не по чётности.
        for (int index = 0; index < points.Count; index++)
        {
            bool pairedWithNext = index + 1 < points.Count && points[index + 1].AtMs > points[index].AtMs
                && (index + 2 >= points.Count || points[index + 2].AtMs > points[index + 1].AtMs);

            if (!pairedWithNext)
            {
                kept.Add(points[index]);
                continue;
            }

            var low = points[index];
            var high = points[index + 1];
            kept.Add(smoothing == ChartSmoothing.Peaks
                ? (high.Value >= low.Value ? high : low)
                : (high.Value <= low.Value ? high : low));
            index++;
        }

        return kept;
    }
}
