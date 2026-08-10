using WheelTalk.Core.Tiles;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Подбор кегля для экрана «Цифры» — та часть, которую можно проверить без телефона: классификация
/// форм, один кегль на класс, монотонность по площади и участие пола единицы в бюджете ширины.
/// Мерилка шрифтом здесь подменена честной линейкой (<see cref="Ruler"/>): проверяется арифметика
/// подбора, а не начертание.
/// </summary>
public class TileTypographyTests
{
    /// <summary>
    /// Линейка вместо шрифта: знак моноширинного — 0,6 кегля, обычного — 0,5. Пропорции взяты у
    /// настоящих начертаний, и этого довольно: правила подбора не зависят от того, чем мерят, — но
    /// зависят от того, что мерят <b>одним и тем же</b>.
    /// </summary>
    private sealed class Ruler : ITextRuler
    {
        /// <summary>Высота строки к кеглю — та же поправка на начертание, что даёт настоящий шрифт.</summary>
        public const float Ink = 1.25f;

        public float Width(string text, float sizeSp, bool mono) => text.Length * sizeSp * (mono ? 0.6f : 0.5f);

        public float Height(float sizeSp) => sizeSp * Ink;
    }

    /// <summary>Экран-образец: узкий телефон, 360 dp. Плотность взята единичной — правила подбора безразмерны.</summary>
    private const float ScreenPx = 360;

    /// <summary>
    /// Сетка боевого экрана: 12 колонок, строка 68, просвет 3, поля 8 — и <b>колонка, посчитанная
    /// той же формулой, что на экране</b> (<c>TilesScreen.CellWidth</c>), а не круглым числом.
    /// <para>
    /// <b>Геометрия линейки обязана совпадать с настоящей.</b> Дважды это стоило ложных ответов:
    /// просвет стоял вдвое больше прочих чисел (6 против 3) — и с починкой высоты низкая плитка
    /// теряла шестую часть; колонка стояла 23 вместо 25,9 — и полоса 12×3 выходила 1,56 вместо
    /// 1,74, то есть при пороге 1,65 объявляла себя квадратом, которым на экране никогда не будет.
    /// Правило простое: <b>что задаёт пропорции боксов — берётся с настоящего экрана</b>. Отношения
    /// на этой сетке теперь те же, что на телефоне: 1/4 — 1,35; 1/2×2 — 1,31; 6×1 — 2,75;
    /// 12×3 — 1,74.
    /// </para>
    /// <para>
    /// Числа <b>бюджетов</b> (высота подписи, полоса жара, место под пометку) остались грубее
    /// настоящих и нарочно щедры к запасу: они пропорций не меняют, а подобранные по ним кегли —
    /// величины этой линейки, не телефона. Сходимость бюджетов с настоящей плиткой стережёт
    /// <see cref="TileFitsTheGridTests"/>, где сетка строится по плотности экрана.
    /// </para>
    /// </summary>
    private static TileMetrics Grid() => new(
        CellWidthPx: (ScreenPx - (3 * 11) - (8 * 2)) / 12,
        RowHeightPx: 68,
        GapPx: 3,
        PaddingPx: 8,
        LabelHeightPx: 16,
        HeatBarPx: 9,
        EditReservePx: 22,
        EditFooterPx: 14,
        GapUnitPx: 4,
        GapLabelPx: 12,
        MarkPx: 16,
        ValueBleedPx: 4);

    /// <summary>
    /// Квадратную плитку заводят ради крупных данных, и форму ей выбирает пропорция. На 360 dp:
    /// четвертная 83,8×62 — 1,35 и половина в две строки 170,5×130 — 1,31 квадраты; ряд 170,5×62 —
    /// 2,75 и полоса в три строки 344×198 — 1,74 нет. Порог 1,65 (владелец, 10.08.2026).
    /// </summary>
    [Fact]
    public void A_box_close_to_one_to_one_is_a_square()
    {
        var grid = Grid();

        Assert.True(TileTypography.IsSquare(new TileClass(3, 1), grid));
        Assert.True(TileTypography.IsSquare(new TileClass(6, 2), grid));
        Assert.False(TileTypography.IsSquare(new TileClass(6, 1), grid));
        Assert.False(TileTypography.IsSquare(new TileClass(12, 3), grid));
    }

    /// <summary>
    /// Порог поднят до 1,65 ради <b>широкого</b> экрана: там плитки шире в dp, и при 1,45 четвертная
    /// (1,56) переставала быть квадратом, хотя на узком (1,35) им была. Классификация обязана быть
    /// одной на обоих — иначе одна и та же раскладка выглядит на двух телефонах по-разному.
    /// <para>
    /// Запас у порога разный с двух сторон, и это надо знать в лицо: снизу до квадратов 1,65/1,56 —
    /// шесть процентов на широком экране, сверху до полосы 12×3 (1,74) — пять. Двигать порог дальше
    /// без пересчёта обеих сторон — значит менять форму целому классу.
    /// </para>
    /// </summary>
    [Fact]
    public void The_same_tile_is_a_square_on_a_wide_screen_too()
    {
        // Широкий телефон: 411 dp — та ширина, на которой прежний порог 1,45 ломался.
        var wide = Grid() with { CellWidthPx = (411 - (3 * 11) - (8 * 2)) / 12f };

        Assert.True(TileTypography.IsSquare(new TileClass(3, 1), wide));
        Assert.True(TileTypography.IsSquare(new TileClass(6, 2), wide));
        Assert.False(TileTypography.IsSquare(new TileClass(6, 1), wide));
        Assert.False(TileTypography.IsSquare(new TileClass(12, 3), wide));
    }

    /// <summary>
    /// То, ради чего форма заведена: квадрат обязан читаться <b>заметно крупнее</b> вытянутого
    /// соседа той же ширины. Прежде оба упирались в одну и ту же ширину и выходили в один кегль —
    /// высота квадратной плитки не работала вовсе (жалоба владельца 10.08.2026).
    /// </summary>
    [Fact]
    public void A_square_reads_noticeably_bigger_than_a_flat_tile_of_the_same_width()
    {
        var faces = TileTypography.Measure(
        [
            new(new TileClass(6, 2), "888.88", "В", "Напряжение", false),
            new(new TileClass(6, 1), "888.88", "В", "Напряжение", false),
        ], Grid(), new Ruler());

        float square = faces[new TileClass(6, 2)].ValueSp;
        float flat = faces[new TileClass(6, 1)].ValueSp;

        Assert.Equal(TileForm.Square, faces[new TileClass(6, 2)].Form);
        Assert.True(square > flat * 1.25f,
            $"квадрат {square} sp против плоского {flat} sp — разница обязана быть заметной");
    }

    [Fact]
    public void A_quarter_tile_is_a_stack_and_a_wide_low_one_may_be_a_row()
    {
        Assert.True(TileTypography.IsQuarter(new TileClass(3, 1)));
        Assert.False(TileTypography.IsQuarter(new TileClass(6, 1)));

        // Форму позволяет пропорция: одна строка сетки и не меньше шести колонок.
        Assert.False(TileTypography.CanBeRow(new TileClass(3, 1)));
        Assert.True(TileTypography.CanBeRow(new TileClass(6, 1)));
        Assert.True(TileTypography.CanBeRow(new TileClass(12, 1)));
        Assert.False(TileTypography.CanBeRow(new TileClass(12, 2)));
    }

    /// <summary>Четвертная — та плитка, на которой единицы нет вовсе: её некуда поставить.</summary>
    [Fact]
    public void A_quarter_tile_shows_no_unit_at_all()
    {
        Assert.Equal("", TileTypography.UnitOn(new TileClass(3, 1), "км/ч"));
        Assert.Equal("км/ч", TileTypography.UnitOn(new TileClass(6, 1), "км/ч"));
        Assert.Equal("км/ч", TileTypography.UnitOn(new TileClass(12, 2), "км/ч"));
    }

    /// <summary>
    /// Пол единицы — решение о читаемости, и платит за него число: 11 sp остаются 11 sp, как бы
    /// мелко ни набрали число.
    /// </summary>
    [Fact]
    public void The_unit_never_goes_below_its_floor()
    {
        var metrics = Grid();

        Assert.Equal(27f, TileTypography.UnitSp(60, metrics));
        Assert.Equal(11f, TileTypography.UnitSp(20, metrics));
        Assert.Equal(11f, TileTypography.UnitSp(12, metrics));
    }

    /// <summary>
    /// Ради чего всё: две плитки одного размера — заявление человека, что величины равны по
    /// важности. Длинное число у одной опускает кегль <b>класса</b>, а не только свой.
    /// </summary>
    [Fact]
    public void Tiles_of_one_size_read_at_one_size()
    {
        var quarter = new TileClass(3, 1);

        // Строка соседа взята заведомо широкой: на четвертной плитке кегль упирается то в ширину,
        // то в высоту, и мерить правило надо там, где решает ширина, — иначе тест проверяет не
        // «худшую строку класса», а потолок высоты, одинаковый у обеих.
        var faces = TileTypography.Measure(
        [
            new(quarter, "7", "", "Ток", false),
            new(quarter, "123456.7", "", "Мощность", false),
        ], Grid(), new Ruler());

        // Класс один — значит и кегль один, и он посчитан по худшей строке: короткое «7» не вправе
        // читаться крупнее соседа.
        float alone = TileTypography.Measure(
            [new(quarter, "7", "", "Ток", false)], Grid(), new Ruler())[quarter].ValueSp;
        float worst = TileTypography.Measure(
            [new(quarter, "123456.7", "", "Мощность", false)], Grid(), new Ruler())[quarter].ValueSp;

        Assert.True(worst < alone, "длинная строка обязана садиться мельче короткой");
        Assert.Equal(worst, faces[quarter].ValueSp);
    }

    /// <summary>
    /// Правило монотонности — <b>для квадратов</b>: квадрат с большей площадью не читается мельче
    /// квадрата помельче. Прямоугольных оно не касается вовсе (решение владельца 10.08.2026): у них
    /// своя порода и свой принятый вид, и подтягивание под чужой класс однажды сломало строки.
    /// </summary>
    [Fact]
    public void A_bigger_square_never_reads_smaller_than_a_smaller_square()
    {
        var faces = TileTypography.Measure(
        [
            new(new TileClass(3, 1), "888.88", "", "Ток", false),
            new(new TileClass(6, 2), "888.88", "В", "Напряжение", false),
            new(new TileClass(6, 1), "88888.88", "км", "Одометр", false),
            new(new TileClass(12, 2), "88888.8", "км/ч", "Скорость", false),
        ], Grid(), new Ruler());

        var squares = faces
            .Where(pair => pair.Value.Form == TileForm.Square)
            .OrderBy(pair => pair.Key.Area)
            .Select(pair => pair.Value.ValueSp)
            .ToList();

        Assert.Equal(2, squares.Count);
        Assert.Equal(squares.OrderBy(size => size), squares);
    }

    /// <summary>
    /// Форма проверяется результатом. Длинная подпись съедает у строки ширину, и если число от
    /// этого садится мельче, чем столбиком, — класс остаётся столбиком.
    /// </summary>
    [Fact]
    public void The_form_that_makes_the_number_smaller_is_the_wrong_form()
    {
        var half = new TileClass(6, 1);

        // Короткое число: в одну линию ему просторнее, чем под подписью.
        var wide = TileTypography.Measure([new(half, "12", "км", "Пробег", false)], Grid(), new Ruler());
        Assert.Equal(TileForm.Row, wide[half].Form);

        // Длинное: строка отдаёт треть ширины подписи, и столбик оказывается крупнее.
        var narrow = TileTypography.Measure(
            [new(half, "1234567.8", "км", "Одометр", false)], Grid(), new Ruler());
        Assert.Equal(TileForm.Stack, narrow[half].Form);
    }

    /// <summary>
    /// Пометка ▲▼ считается <b>внутри</b> лимита подписи, а не сверх него: иначе одна плитка с
    /// пометкой роняет кегль всего класса.
    /// </summary>
    [Fact]
    public void The_mark_lives_inside_the_label_limit()
    {
        var half = new TileClass(6, 1);

        float plain = TileTypography.Measure(
            [new(half, "74.2", "В", "Напряжение", false)], Grid(), new Ruler())[half].ValueSp;
        float marked = TileTypography.Measure(
            [new(half, "74.2", "В", "Напряжение", true)], Grid(), new Ruler())[half].ValueSp;

        Assert.Equal(plain, marked);
    }

    /// <summary>
    /// В правке место под крест, ручку и подпись размера вычитается из бюджета. Число от этого
    /// садится мельче — и это правильно: отступ сжал бы бокс, но текст бы не порезал, и число
    /// печаталось бы прямо по кресту.
    /// </summary>
    [Fact]
    public void Editing_pays_for_its_buttons_out_of_the_size_budget()
    {
        var tiles = new TileText[] { new(new TileClass(6, 2), "77.9", "В", "Напряжение", false) };

        float shown = TileTypography.Measure(tiles, Grid(), new Ruler())[new TileClass(6, 2)].ValueSp;
        float editing = TileTypography.Measure(tiles, Grid(), new Ruler(), editing: true)[new TileClass(6, 2)].ValueSp;

        Assert.True(editing < shown, "в правке кегль обязан быть меньше — место занято кнопками");
    }

    /// <summary>
    /// Замок на регрессию 10.08.2026: у строки 6×1 число вылезло за низ плитки наполовину, потому
    /// что потолок по высоте считался делением <b>пикселей</b> на поправку в <b>sp</b> — на экране
    /// с плотностью 2 кегль выходил вдвое больше дозволенного. Теперь высоту меряет та же мерилка,
    /// что и ширину, и проверяется это здесь: строка обязана поместиться в свою высоту.
    /// </summary>
    [Theory]
    [InlineData(6, 1)]
    [InlineData(3, 1)]
    [InlineData(6, 2)]
    [InlineData(12, 2)]
    public void A_number_never_grows_taller_than_the_room_it_was_given(int columns, int rows)
    {
        var grid = Grid();
        var ruler = new Ruler();
        var tile = new TileClass(columns, rows);

        var faces = TileTypography.Measure(
            [new(tile, "888.88", "В", "Напряжение", false)], grid, ruler);

        // Потолок общий для всех трёх форм: высота плитки без полей и без линии рамки. Считать его
        // по квадрату нельзя — у «строки» подписи над числом нет вовсе, и её бюджет щедрее
        // квадратного на целую метку; с широкой колонкой класс 6×1 как раз в «строку» и уходит.
        // Точная посадка каждой формы стережётся отдельно — TileFitsTheGridTests, вторым путём.
        float room = grid.Height(tile) - (2 * grid.PaddingPx) - grid.HeatBarPx;

        Assert.True(ruler.Height(faces[tile].ValueSp) <= room,
            $"{columns}×{rows}: строка {ruler.Height(faces[tile].ValueSp):F1} px в бюджете {room:F1} px");
    }

    /// <summary>
    /// Замок на разделение пород (решение владельца 10.08.2026): ужатые поля — дело квадрата, и на
    /// прямоугольную плитку они не действуют вовсе. Общая «полезная» правка полей однажды подняла
    /// кегль строки и вывела число за низ плитки.
    /// </summary>
    [Fact]
    public void Squeezing_the_side_fields_does_not_touch_a_rectangle()
    {
        var flat = new TileClass(6, 1);
        var text = new TileText[] { new(flat, "88888.88", "В", "Напряжение", false) };

        float tight = TileTypography.Measure(text, Grid(), new Ruler())[flat].ValueSp;
        float loose = TileTypography.Measure(text, Grid() with { }, new Ruler())[flat].ValueSp;
        float bled = TileTypography.Measure(
            text,
            Grid() with { CellWidthPx = 23 },
            new Ruler())[flat].ValueSp;

        Assert.Equal(tight, loose);
        Assert.Equal(tight, bled);
    }

    /// <summary>
    /// Монотонность — правило квадратов, и прямоугольных она не касается: подтягивать их под чужой
    /// класс значит менять принятый вид ради соседа.
    /// </summary>
    [Fact]
    public void Monotonicity_lifts_squares_and_leaves_rectangles_alone()
    {
        var square = new TileClass(6, 2);
        var flat = new TileClass(12, 2);

        var faces = TileTypography.Measure(
        [
            new(square, "8.8", "В", "Напряжение", false),
            new(flat, "88888.88", "км/ч", "Скорость", false),
        ], Grid(), new Ruler());

        float alone = TileTypography.Measure(
            [new(flat, "88888.88", "км/ч", "Скорость", false)], Grid(), new Ruler())[flat].ValueSp;

        Assert.Equal(TileForm.Square, faces[square].Form);
        Assert.Equal(TileForm.Stack, faces[flat].Form);
        Assert.Equal(alone, faces[flat].ValueSp);
    }

    /// <summary>
    /// Замок на принятый вид прямоугольных: их кегли — то, что владелец принял по снимку, и меняться
    /// они не должны ни от правки квадрата, ни от «общей полезной» правки полей. Числа сняты с
    /// тестовой линейки и тестовой сетки; важна не их величина, а то, что они держатся: сдвинулись —
    /// значит прямоугольную породу опять задели.
    /// <para>
    /// <b>Числа пересняты дважды 10.08.2026.</b> Сперва 21 → 16 (6×1) и 64 → 57 (12×2 и 12×3):
    /// прежние сняты с плитки, которую подбор считал выше настоящей на два просвета
    /// (<see cref="TileMetrics.Height"/>), и низ числа на телефоне срезало — кегль, ужавшийся до
    /// того, что плитка даёт на самом деле, и есть верный. Затем 57 → 64 у широких: колонка на
    /// линейке стояла 23 вместо настоящих 25,9, и классы, которым кегль задаёт <b>ширина</b>,
    /// садились мельче, чем на экране. Класс 6×1 остался 16 — его держит высота, а её правка
    /// колонки не трогает.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(6, 1, 16)]
    [InlineData(12, 2, 64)]
    [InlineData(12, 3, 64)]
    public void A_rectangle_keeps_the_size_it_was_accepted_with(int columns, int rows, float expected)
    {
        var tile = new TileClass(columns, rows);

        // Худшая строка прямоугольной породы — принятые пять разрядов и два знака после точки.
        var faces = TileTypography.Measure(
        [
            new(tile, "88888.88", "В", "Напряжение", false),
            new(new TileClass(3, 1), "8", "", "Ток", false),
        ], Grid(), new Ruler());

        Assert.NotEqual(TileForm.Square, faces[tile].Form);
        Assert.Equal(expected, faces[tile].ValueSp);
    }

    /// <summary>Пол кегля числа: ниже него подбор не опускается, даже когда строка не влезает никак.</summary>
    [Fact]
    public void A_number_that_fits_nowhere_still_gets_the_floor()
    {
        var quarter = new TileClass(3, 1);
        var faces = TileTypography.Measure(
            [new(quarter, "1234567890123456", "", "Ток", false)], Grid(), new Ruler());

        Assert.Equal(12f, faces[quarter].ValueSp);
    }
}
