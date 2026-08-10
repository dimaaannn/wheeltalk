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
        public float Width(string text, float sizeSp, bool mono) => text.Length * sizeSp * (mono ? 0.6f : 0.5f);
    }

    /// <summary>Сетка боевого экрана: 12 колонок по 23 px, строка 68, просвет 3, поля 8.</summary>
    private static TileMetrics Grid() => new(
        CellWidthPx: 23,
        RowHeightPx: 68,
        GapPx: 6,
        PaddingPx: 8,
        LabelHeightPx: 16,
        HeatBarPx: 9,
        EditReservePx: 22,
        EditFooterPx: 14,
        GapUnitPx: 4,
        GapLabelPx: 12,
        MarkPx: 16);

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
        var faces = TileTypography.Measure(
        [
            new(quarter, "7", "", "Ток", false),
            new(quarter, "1234.5", "", "Мощность", false),
        ], Grid(), new Ruler());

        // Класс один — значит и кегль один, и он посчитан по худшей строке: короткое «7» не вправе
        // читаться крупнее соседа.
        float alone = TileTypography.Measure(
            [new(quarter, "7", "", "Ток", false)], Grid(), new Ruler())[quarter].ValueSp;
        float worst = TileTypography.Measure(
            [new(quarter, "1234.5", "", "Мощность", false)], Grid(), new Ruler())[quarter].ValueSp;

        Assert.True(worst < alone, "длинная строка обязана садиться мельче короткой");
        Assert.Equal(worst, faces[quarter].ValueSp);
    }

    /// <summary>
    /// Правило монотонности: класс с большей площадью не читается мельче меньшего. Без него
    /// половинная строка выходила мельче четвертного соседа — проверено на макете.
    /// </summary>
    [Fact]
    public void A_bigger_tile_never_reads_smaller_than_a_smaller_one()
    {
        var faces = TileTypography.Measure(
        [
            new(new TileClass(3, 1), "7", "", "Ток", false),
            new(new TileClass(6, 1), "1234567.8", "км", "Одометр", false),
            new(new TileClass(6, 2), "77.9", "В", "Напряжение", false),
            new(new TileClass(12, 2), "34.2", "км/ч", "Скорость", false),
        ], Grid(), new Ruler());

        var byArea = faces.OrderBy(pair => pair.Key.Area).Select(pair => pair.Value.ValueSp).ToList();

        Assert.Equal(byArea.OrderBy(size => size), byArea);
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
