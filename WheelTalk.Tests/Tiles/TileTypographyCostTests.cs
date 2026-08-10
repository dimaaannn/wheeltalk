using WheelTalk.Core.Tiles;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Цена подбора кегля — в обращениях к шрифту. Считается она здесь потому, что на телефоне каждое
/// такое обращение уходит через JNI в системный шрифт, и разница между «сорок» и «девять тысяч» —
/// это разница между незаметным переходом и секундой стоя́щего экрана (баг владельца 10.08.2026:
/// лаг ~1 с при входе в правку, выходе и первом показе «Цифр» — общее у всех трёх ровно этот
/// подбор).
/// <para>
/// Замок держит <b>число вызовов</b>, а не миллисекунды: миллисекунды зависят от телефона, а вызовы
/// — от нашего кода. Считается на боевой раскладке: 18 плиток четырёх классов.
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

    /// <summary>Раскладка <c>TilesLayout.Fixed</c> как она есть: восемнадцать плиток четырёх классов.</summary>
    private static IReadOnlyList<TileText> Layout() =>
    [
        new(new TileClass(12, 2), "88888.8", "км/ч", "Скорость", false),
        new(new TileClass(6, 2), "88888.8", "%", "ШИМ", false),
        new(new TileClass(6, 2), "88888", "%", "Заряд", false),
        new(new TileClass(6, 2), "88888.88", "В", "Напряжение", false),
        new(new TileClass(3, 1), "888.88", "А", "Ток", false),
        new(new TileClass(3, 1), "888", "Вт", "Мощн.", false),
        new(new TileClass(3, 1), "888.88", "А", "Фазный", false),
        new(new TileClass(3, 1), "888.8", "%", "Пик ШИМ", false),
        new(new TileClass(3, 1), "888", "°C", "Темп.", false),
        new(new TileClass(3, 1), "888", "°C", "Мотор", false),
        new(new TileClass(3, 1), "888.8", "°", "Наклон", false),
        new(new TileClass(3, 1), "888.8", "км/ч", "Макс.", false),
        new(new TileClass(6, 1), "88888.88", "км", "За поездку", false),
        new(new TileClass(6, 1), "88888", "км", "Одометр", false),
        new(new TileClass(6, 1), "88888.8", "%", "ШИМ", true),
        new(new TileClass(6, 1), "88888.88", "В", "Напряжение", true),
        new(new TileClass(12, 2), "88888.8", "%", "ШИМ", false),
    ];

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
