namespace WheelTalk.Core.Tiles;

/// <summary>Размер плитки в клетках сетки — он же класс формы: колонки × строки.</summary>
public readonly record struct TileClass(int Columns, int Rows)
{
    /// <summary>Площадь в клетках. По ней классы выстраиваются для правила монотонности.</summary>
    public int Area => Columns * Rows;
}

/// <summary>Как устроена плитка внутри.</summary>
public enum TileForm
{
    /// <summary>Подпись сверху, число во всё оставшееся место. Четвертная и все многострочные.</summary>
    Stack,

    /// <summary>Подпись слева, число справа, всё в одну линию. Низкая и широкая.</summary>
    Row,
}

/// <summary>Чем набрана плитка класса: форма и два кегля.</summary>
public readonly record struct TileTypeface(TileForm Form, float ValueSp, float UnitSp);

/// <summary>Строка одной плитки — всё, что нужно, чтобы её померить.</summary>
public readonly record struct TileText(TileClass Class, string Value, string Unit, string Label, bool Mark);

/// <summary>
/// Мерилка строк — шов между подбором кегля и платформой. Ширина возвращается в тех же единицах, в
/// которых заданы размеры плитки (на Android — пиксели).
/// <para>
/// Шов нужен по двум причинам. Первая: мерить надо <b>тем же шрифтом, которым рисуем</b> — таблица
/// средних ширин врёт на точке (в моноширинном начертании она шириной с цифру) и роняет «74.2» за
/// край. Вторая: подбор кегля — арифметика, и проверяться он обязан без телефона.
/// </para>
/// </summary>
public interface ITextRuler
{
    /// <param name="mono">Число набрано моноширинным, единица и подпись — обычным.</param>
    float Width(string text, float sizeSp, bool mono);
}

/// <summary>
/// Все размеры плитки в одном месте — то, что подбор кегля обязан знать о сетке и о полях.
/// Приходит от хозяина экрана: числа живут в <c>TilesLayout</c>, а считает по ним ядро.
/// </summary>
/// <param name="CellWidthPx">Ширина одной колонки сетки.</param>
/// <param name="RowHeightPx">Высота одной строки сетки.</param>
/// <param name="GapPx">Просвет между клетками.</param>
/// <param name="PaddingPx">Поле внутри плитки.</param>
/// <param name="LabelHeightPx">Высота строки подписи в форме «столбик».</param>
/// <param name="HeatBarPx">Высота полоски жара по низу плитки — место, которого числу не достанется.</param>
/// <param name="EditReservePx">Ширина, которую в правке забирают крест и ручка.</param>
/// <param name="EditFooterPx">Высота, которую в правке забирает подпись размера плитки.</param>
/// <param name="GapUnitPx">Просвет между числом и единицей.</param>
/// <param name="GapLabelPx">Просвет между подписью и числом в форме «строка».</param>
/// <param name="MarkPx">Место под пометку ▲▼ — считается вместе с подписью, а не после неё.</param>
public sealed record TileMetrics(
    float CellWidthPx,
    float RowHeightPx,
    float GapPx,
    float PaddingPx,
    float LabelHeightPx,
    float HeatBarPx,
    float EditReservePx,
    float EditFooterPx,
    float GapUnitPx,
    float GapLabelPx,
    float MarkPx)
{
    /// <summary>Кегль подписи в форме «строка», sp.</summary>
    public float RowLabelSp { get; init; } = 13;

    /// <summary>Пол кегля числа, sp. Ниже — не читается вовсе (план 23 и план плиток §3).</summary>
    public float MinValueSp { get; init; } = 12;

    /// <summary>
    /// Потолок кегля числа, sp. Прежние 48 и были причиной пустоты в крупной плитке: число упиралось
    /// в потолок и переставало расти вместе с местом.
    /// </summary>
    public float MaxValueSp { get; init; } = 96;

    /// <summary>Единица — долей от числа…</summary>
    public float UnitScale { get; init; } = 0.45f;

    /// <summary>…но не мельче этого: 7 px не читаются, и платит за пол число, а не читаемость.</summary>
    public float MinUnitSp { get; init; } = 11;

    /// <summary>Высота начертания к кеглю. Без поправки цифры срезаются сверху и снизу.</summary>
    public float InkRatio { get; init; } = 1.25f;

    /// <summary>Доля ширины плитки, дальше которой подпись в форме «строка» обрезается многоточием.</summary>
    public float RowLabelShare { get; init; } = 0.35f;

    /// <summary>Ширина плитки класса.</summary>
    public float Width(TileClass tile) => (tile.Columns * CellWidthPx) + ((tile.Columns - 1) * GapPx);

    /// <summary>
    /// Высота плитки класса. Просветы считаются двойными — так же, как их считает сама плитка
    /// (<c>TileView.SetRows</c>): высокая обязана встать вровень со столбиком низких.
    /// </summary>
    public float Height(TileClass tile) => (tile.Rows * RowHeightPx) + ((tile.Rows - 1) * GapPx * 2);
}

/// <summary>
/// Подбор кегля для экрана плиток: одна форма и один кегль на класс, а не на плитку.
/// <para>
/// <b>Довод не в аккуратности.</b> Раскладку собрал человек, и две плитки одного размера — его
/// заявление, что величины равны по важности. Расходиться в кегле оттого, что у одной имя длиннее,
/// они не вправе; поэтому кегль считается по <b>худшей строке класса</b> и применяется ко всем.
/// </para>
/// <para>
/// Поверх этого — <b>монотонность по площади</b>: класс, которому дали больше места, не может
/// читаться мельче меньшего. Без неё половинная строка выходила мельче четвертного соседа.
/// </para>
/// </summary>
public static class TileTypography
{
    /// <summary>Четвертная плитка: единицы на ней нет вовсе — её некуда поставить, не отняв у числа или у имени.</summary>
    public static bool IsQuarter(TileClass tile) => tile.Columns <= 3;

    /// <summary>Форму «строка» позволяет только пропорция: одна строка сетки и не меньше шести колонок.</summary>
    public static bool CanBeRow(TileClass tile) => tile.Rows == 1 && tile.Columns >= 6;

    /// <summary>Единица при числе такого кегля. Пол участвует в подборе — иначе строка вылезет ровно на него.</summary>
    public static float UnitSp(float valueSp, TileMetrics metrics) =>
        Math.Max(metrics.MinUnitSp, (float)Math.Round(valueSp * metrics.UnitScale));

    /// <summary>Единица, которая реально попадёт на экран: на четвертной её нет.</summary>
    public static string UnitOn(TileClass tile, string unit) => IsQuarter(tile) ? "" : unit;

    /// <summary>
    /// Набор для каждого класса раскладки: форма и кегли. Один вызов на экран — не на плитку и не на
    /// кадр.
    /// </summary>
    /// <param name="editing">
    /// В правке место под крест, ручку и подпись размера <b>вычитается из бюджета</b>, а не
    /// отнимается отступом после подбора: отступ сжимает бокс, но текст не режет, и число печатается
    /// прямо по кресту.
    /// </param>
    public static IReadOnlyDictionary<TileClass, TileTypeface> Measure(
        IEnumerable<TileText> tiles, TileMetrics metrics, ITextRuler ruler, bool editing = false)
    {
        var worst = new Dictionary<TileClass, (float Row, float Stack)>();

        foreach (var tile in tiles)
        {
            float stack = FitStack(tile, metrics, ruler, editing);
            float row = CanBeRow(tile.Class) ? FitRow(tile, metrics, ruler, editing) : 0;

            worst[tile.Class] = worst.TryGetValue(tile.Class, out var known)
                ? (Math.Min(known.Row, row), Math.Min(known.Stack, stack))
                : (row, stack);
        }

        var chosen = new Dictionary<TileClass, TileTypeface>();
        foreach (var (tile, fit) in worst)
        {
            // Форма проверяется результатом: если в строку число садится мельче, чем в столбик,
            // значит для этих подписей строка — неверная форма.
            bool row = CanBeRow(tile) && fit.Row >= fit.Stack;
            float size = row ? fit.Row : fit.Stack;
            chosen[tile] = new TileTypeface(row ? TileForm.Row : TileForm.Stack, size, UnitSp(size, metrics));
        }

        return Monotonic(chosen, metrics);
    }

    /// <summary>
    /// Один проход по классам от меньшей площади к большей: кегль класса не может быть меньше
    /// кегля класса помельче. Правило системы, а не подгонка одного класса.
    /// </summary>
    private static IReadOnlyDictionary<TileClass, TileTypeface> Monotonic(
        Dictionary<TileClass, TileTypeface> chosen, TileMetrics metrics)
    {
        float floor = 0;
        foreach (var tile in chosen.Keys.OrderBy(t => t.Area).ThenBy(t => t.Columns).ToList())
        {
            var face = chosen[tile];
            float size = Math.Max(face.ValueSp, floor);
            floor = size;
            chosen[tile] = face with { ValueSp = size, UnitSp = UnitSp(size, metrics) };
        }

        return chosen;
    }

    private static float FitStack(TileText tile, TileMetrics metrics, ITextRuler ruler, bool editing)
    {
        float width = metrics.Width(tile.Class) - (2 * metrics.PaddingPx) - (editing ? metrics.EditReservePx : 0);
        float height = metrics.Height(tile.Class) - (2 * metrics.PaddingPx)
            - metrics.LabelHeightPx - metrics.HeatBarPx
            - (editing && !IsQuarter(tile.Class) ? metrics.EditFooterPx : 0);

        return Fit(tile, width, height, metrics, ruler);
    }

    private static float FitRow(TileText tile, TileMetrics metrics, ITextRuler ruler, bool editing)
    {
        float box = metrics.Width(tile.Class);

        // Ограничение считается вместе с пометкой ▲▼, а не после неё: иначе одна плитка с пометкой
        // роняет кегль всего класса.
        float label = Math.Min(
            ruler.Width(tile.Label, metrics.RowLabelSp, mono: false) + (tile.Mark ? metrics.MarkPx : 0),
            box * metrics.RowLabelShare);

        float width = box - (2 * metrics.PaddingPx) - label - metrics.GapLabelPx
            - (editing ? metrics.EditReservePx : 0);
        float height = metrics.Height(tile.Class) - (2 * metrics.PaddingPx) - metrics.HeatBarPx;

        return Fit(tile, width, height, metrics, ruler);
    }

    /// <summary>
    /// Самый крупный кегль, при котором строка «число + единица» влезает и в ширину, и в высоту.
    /// Сверху ограничивает высота (с поправкой на начертание), снизу — пол читаемости.
    /// </summary>
    private static float Fit(TileText tile, float width, float height, TileMetrics metrics, ITextRuler ruler)
    {
        string unit = UnitOn(tile.Class, tile.Unit);

        float top = Math.Min(metrics.MaxValueSp, (float)Math.Floor(height / metrics.InkRatio));
        if (top < metrics.MinValueSp) return metrics.MinValueSp;

        for (float size = top; size > metrics.MinValueSp; size--)
        {
            float line = ruler.Width(tile.Value, size, mono: true);
            if (unit.Length > 0) line += metrics.GapUnitPx + ruler.Width(unit, UnitSp(size, metrics), mono: false);

            if (line <= width) return size;
        }

        return metrics.MinValueSp;
    }
}
