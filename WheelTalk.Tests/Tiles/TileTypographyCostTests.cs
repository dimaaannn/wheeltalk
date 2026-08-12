using WheelTalk.Core.Metrics;
using WheelTalk.Core.Tiles;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Цена подбора кегля — в обращениях к шрифту. Считается она здесь потому, что на телефоне каждое
/// такое обращение уходит через JNI в системный шрифт, и разница между «сорок» и «девять тысяч» —
/// это разница между незаметным переходом и секундой стоя́щего экрана (баг владельца 10.08.2026:
/// лаг ~1 с при входе в правку, выходе и первом показе «Цифр» — общее у всех трёх ровно этот
/// подбор).
/// <para>
/// Замок держит <b>число вызовов</b>, а не миллисекунды: миллисекунды зависят от телефона, а вызовы
/// — от нашего кода. Считается на боевой раскладке — той самой, что читается из
/// <c>TilesLayout.Fixed</c>.
/// </para>
/// <para>
/// Что он <b>не</b> ловит: сам подбор зовётся здесь напрямую, поэтому лишний его вызов с экрана
/// (дважды за кадр, скажем) замку не виден — он стережёт цену одного прохода, а не число проходов.
/// </para>
/// </summary>
public class TileTypographyCostTests
{
    /// <summary>Линейка, которая считает, сколько раз её спросили.</summary>
    private sealed class CountingRuler : ITextRuler
    {
        public int Widths { get; private set; }

        public int Heights { get; private set; }

        public float Width(string text, float sizeSp, bool mono)
        {
            Widths++;
            return text.Length * sizeSp * (mono ? 0.6f : 0.5f);
        }

        public float Height(float sizeSp)
        {
            Heights++;
            return sizeSp * 1.25f;
        }
    }

    private static TileMetrics Grid() => new(
        CellWidthPx: 23, RowHeightPx: 68, GapPx: 6, PaddingPx: 8, LabelHeightPx: 16, HeatBarPx: 9,
        EditReservePx: 22, EditFooterPx: 14, GapUnitPx: 4, GapLabelPx: 12, MarkPx: 16, ValueBleedPx: 4);

    /// <summary>
    /// Раскладка, с которой приложение стартует, — <b>прочитанная из <c>TilesLayout.Fixed</c></b>
    /// (<see cref="OwnerLayout"/>), а не переписанная сюда руками. Список руками здесь и стоял, и к
    /// ревизии 12.08.2026 отстал на целый состав: держал снятые решением владельца плитки (фазный
    /// ток, наклон, «Пик ШИМ» отдельной величиной) и не знал ни полосы 12×3, ни разделителя. Цена
    /// подбора считается по тому, что на экране, иначе это цена вымышленного экрана.
    /// </summary>
    private static IReadOnlyList<TileText> Layout() => OwnerLayout.Tiles()
        .Select(spot =>
        {
            var metric = MetricCatalogue.Find(spot.Metric)!;
            string whole = new('8', 5);

            return new TileText(
                new TileClass(spot.Columns, spot.Rows),
                metric.Decimals > 0 ? whole + "." + new string('8', metric.Decimals) : whole,
                "%",
                metric.LabelKey,
                Mark: spot.Kind == "Extremum");
        })
        .ToList();

    /// <summary>
    /// Потолок цены. Подбор перебирает кегли от потолка к полу, и на каждый шаг спрашивает ширину —
    /// это его природа; но спрашивать он обязан <b>разумное число раз</b>, а не по сотне на плитку
    /// на каждую из трёх форм.
    /// <para>
    /// Число выбрано с запасом вдвое к нынешнему: замок ловит порядок, а не пиксель. Вырос на
    /// порядок — значит кто-то вернул перебор туда, откуда его убрали.
    /// </para>
    /// </summary>
    [Fact]
    public void Fitting_the_whole_layout_stays_within_its_budget()
    {
        var ruler = new CountingRuler();

        TileTypography.Measure(Layout(), Grid(), ruler);

        // Замер 10.08.2026: 846 ширин и 2249 высот. Потолок с запасом вдвое — замок ловит порядок,
        // а не пиксель: вырос втрое — значит кто-то вернул перебор туда, откуда его убрали.
        Assert.True(ruler.Widths + ruler.Heights < 6200,
            $"подбор спросил шрифт {ruler.Widths} раз о ширине и {ruler.Heights} о высоте");
    }
}
