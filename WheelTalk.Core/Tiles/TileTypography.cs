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

    /// <summary>
    /// Квадратная: главный житель — число, подпись уходит меткой в угол и строки себе не берёт.
    /// Заводят такую плитку ради крупных данных, и кегль в ней обязан быть заметно крупнее, чем у
    /// вытянутого соседа той же ширины (решение владельца 10.08.2026).
    /// </summary>
    Square,
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

    /// <summary>
    /// Высота строки этого кегля — в тех же единицах, что и ширина.
    /// <para>
    /// <b>Мерится, а не считается.</b> Пока потолок по высоте считался делением бюджета на
    /// поправку 1,25, он делил <b>пиксели на кегль в sp</b> — и на экране с плотностью 2 давал
    /// вдвое больший кегль: строки 6×1 срезало ровно наполовину (стенд, 10.08.2026). Сравнивать
    /// высоту с бюджетом обязан тот же, кто меряет ширину, — тогда единицы сходятся по построению.
    /// </para>
    /// </summary>
    float Height(float sizeSp);
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
/// <param name="ValueBleedPx">
/// Насколько число вправе выйти за поле плитки в бок. <b>Только в квадрате</b>: там число —
/// главный житель, и поле в 8 dp с каждой стороны стоило ему трети кегля. Прямоугольные породы
/// живут со своими полями, как их приняли (решение владельца 10.08.2026).
/// </param>
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
    float MarkPx,
    float ValueBleedPx = 0)
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

    /// <summary>Доля ширины плитки, дальше которой подпись в форме «строка» обрезается многоточием.</summary>
    public float RowLabelShare { get; init; } = 0.35f;

    /// <summary>
    /// До какого отношения сторон плитка считается квадратной. Отношение безразмерно и от плотности
    /// экрана не зависит — но от его ширины в dp зависит, и это стоит помнить.
    /// <para>
    /// Замер пересчитан 10.08.2026 по <b>настоящей</b> высоте плитки (прежний считал её выше на два
    /// просвета). На 360 dp: четвертная 83,8×62 — 1,35; половина в две строки 170,5×130 — 1,31; ряд
    /// 170,5×62 — 2,75; полоса в три строки 343,8×198 — 1,74. Прежний порог 1,45 отделял первые две
    /// от последних двух, но запас сверху был тесным: на экране в 411 dp четвертная выходит 1,56 и
    /// квадратом уже не считалась бы. 1,65 (слово владельца 10.08.2026) держит квадраты на обеих
    /// ширинах, а ряды и полосы (1,74 и выше) не задевает.
    /// </para>
    /// </summary>
    public float SquareRatio { get; init; } = 1.65f;

    /// <summary>
    /// Высота метки-подписи в квадратной плитке. Она нарисована в углу поверх поля, а не строкой
    /// разметки, поэтому забирает у числа только свою полоску, а не целую строку с отступом.
    /// </summary>
    public float SquareLabelPx { get; init; } = 14;

    /// <summary>Ширина плитки класса.</summary>
    public float Width(TileClass tile) => (tile.Columns * CellWidthPx) + ((tile.Columns - 1) * GapPx);

    /// <summary>
    /// Высота плитки класса — <b>та, что даёт укладчик</b>: он режет сетку по строкам и врезает
    /// плитку внутрь клетки на просвет сверху и снизу
    /// (<c>TileGridLayoutManager</c>: <c>top = row·H + gap</c>, <c>bottom = (row+rows)·H − gap</c>).
    /// Оттого просветы <b>вычитаются</b>, а не прибавляются, и вычитаются они ровно два — сколько
    /// бы строк плитка ни занимала: внутренние границы строк проходят внутри самой плитки и места
    /// у неё не отнимают.
    /// <para>
    /// <b>Замок:</b> прежняя формула прибавляла <c>(rows−1)·2·gap</c> и считала плитку на
    /// <c>2·gap·rows</c> выше настоящей. Пока кегль держала ширина строки, ошибка не была видна;
    /// стоило сузить худшую строку класса 6×1 (пробеги лишились сотых, 10.08.2026) — число упёрлось
    /// в этот завышенный потолок и вылезло за низ плитки. Сходимость с укладчиком стережёт
    /// <c>TileFitsTheGridTests</c>, где место считается вторым путём.
    /// </para>
    /// </summary>
    public float Height(TileClass tile) => (tile.Rows * RowHeightPx) - (2 * GapPx);
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

    /// <summary>
    /// Квадратная плитка — та, у которой бокс близок к 1:1 (<see cref="TileMetrics.SquareRatio"/>).
    /// Форму, как и у прочих, выбирает пропорция, а не величина: одна и та же величина может стоять
    /// и квадратом, и полосой, и это два разных вида, а не один в двух кеглях.
    /// </summary>
    public static bool IsSquare(TileClass tile, TileMetrics metrics)
    {
        float width = metrics.Width(tile);
        float height = metrics.Height(tile);
        float ratio = width >= height ? width / height : height / width;
        return ratio <= metrics.SquareRatio;
    }

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
        var worst = new Dictionary<TileClass, (float Row, float Stack, float Square)>();

        foreach (var tile in tiles)
        {
            float stack = FitStack(tile, metrics, ruler, editing);
            float row = CanBeRow(tile.Class) ? FitRow(tile, metrics, ruler, editing) : 0;
            float square = IsSquare(tile.Class, metrics) ? FitSquare(tile, metrics, ruler, editing) : 0;

            worst[tile.Class] = worst.TryGetValue(tile.Class, out var known)
                ? (Math.Min(known.Row, row), Math.Min(known.Stack, stack), Math.Min(known.Square, square))
                : (row, stack, square);
        }

        var chosen = new Dictionary<TileClass, TileTypeface>();
        foreach (var (tile, fit) in worst)
        {
            // Квадратной форму делает пропорция, и спорить с ней нечему: подпись там метка в углу,
            // а не строка, — числу достаётся вся плитка, и мельче столбика оно быть не может.
            // Между строкой и столбиком форму по-прежнему выбирает результат: если в строку число
            // садится мельче, значит для этих подписей строка — неверная форма.
            (TileForm form, float size) = IsSquare(tile, metrics)
                ? (TileForm.Square, fit.Square)
                : CanBeRow(tile) && fit.Row >= fit.Stack
                    ? (TileForm.Row, fit.Row)
                    : (TileForm.Stack, fit.Stack);

            chosen[tile] = new TileTypeface(form, size, UnitSp(size, metrics));
        }

        return Monotonic(chosen, metrics);
    }

    /// <summary>
    /// Один проход по <b>квадратам</b> от меньшей площади к большей: квадрат, которому дали больше
    /// места, не может читаться мельче квадрата помельче.
    /// <para>
    /// Прямоугольных проход не касается вовсе (решение владельца 10.08.2026): у них своя порода и
    /// свой принятый вид, а подтягивание под чужой класс — тот самый общий рычаг, который сломал
    /// строки. Их кегль — ровно то, что дал их собственный бюджет.
    /// </para>
    /// </summary>
    private static IReadOnlyDictionary<TileClass, TileTypeface> Monotonic(
        Dictionary<TileClass, TileTypeface> chosen, TileMetrics metrics)
    {
        float floor = 0;
        var squares = chosen
            .Where(pair => pair.Value.Form == TileForm.Square)
            .Select(pair => pair.Key)
            .OrderBy(tile => tile.Area)
            .ThenBy(tile => tile.Columns)
            .ToList();

        foreach (var tile in squares)
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
        // Поля не ужимаются: прямоугольная порода осталась ровно такой, какой её приняли, и
        // «полезная общая правка» ей не положена — ровно она однажды подняла кегль строки и вывела
        // число за низ плитки (стенд 10.08.2026, решение владельца: породы разделить).
        float width = metrics.Width(tile.Class) - (2 * metrics.PaddingPx)
            - (editing ? metrics.EditReservePx : 0);
        float height = metrics.Height(tile.Class) - (2 * metrics.PaddingPx)
            - metrics.LabelHeightPx - metrics.HeatBarPx
            - (editing && !IsQuarter(tile.Class) ? metrics.EditFooterPx : 0);

        return Fit(tile, width, height, metrics, ruler);
    }

    /// <summary>
    /// Квадрат: числу достаётся плитка целиком — поля в бок ужаты (<see cref="TileMetrics.ValueBleedPx"/>),
    /// подпись сверху забирает не строку, а свою полоску (<see cref="TileMetrics.SquareLabelPx"/>),
    /// потому что нарисована меткой в углу.
    /// </summary>
    private static float FitSquare(TileText tile, TileMetrics metrics, ITextRuler ruler, bool editing)
    {
        float width = metrics.Width(tile.Class) - (2 * metrics.PaddingPx) + (2 * metrics.ValueBleedPx)
            - (editing ? metrics.EditReservePx : 0);
        float height = metrics.Height(tile.Class) - (2 * metrics.PaddingPx)
            - metrics.SquareLabelPx - metrics.HeatBarPx
            - (editing ? metrics.EditFooterPx : 0);

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

        for (float size = metrics.MaxValueSp; size > metrics.MinValueSp; size--)
        {
            // Высота — первой: она отсекает крупные кегли разом, и мерить их ширину незачем.
            if (ruler.Height(size) > height) continue;

            float line = ruler.Width(tile.Value, size, mono: true);
            if (unit.Length > 0) line += metrics.GapUnitPx + ruler.Width(unit, UnitSp(size, metrics), mono: false);

            if (line <= width) return size;
        }

        return metrics.MinValueSp;
    }
}
