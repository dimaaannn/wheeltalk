using WheelTalk.Core.Metrics;
using WheelTalk.Core.Tiles;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Гейт «ничего не срезано» (план плиток §8.2) — счётом, а не глазами: подобранное число обязано
/// уместиться в <b>то место, которое даёт укладчик</b>, на любом экране.
/// <para>
/// <b>Дыра, ради которой тест заведён.</b> Соседний <c>TileTypographyTests</c> проверяет подбор
/// против того же бюджета, по которому подбор и считает: разойдись бюджет с настоящей плиткой —
/// оба соврут согласованно и промолчат. Здесь место считается <b>вторым путём</b> — по правилу
/// укладчика (<c>TileGridLayoutManager.OnLayoutChildren</c>: <c>top = row·H + gap</c>,
/// <c>bottom = (row+rows)·H − gap</c>) и по разметке самой плитки (поля, строка подписи, отступ
/// числа). Два пути сходятся — число внутри; разошлись — на телефоне срез.
/// </para>
/// <para>
/// <b>Случившаяся поломка.</b> 10.08.2026 у пробегов сняли сотый знак, худшая строка класса 6×1
/// сузилась, кегль перестал упираться в ширину и упёрся в высоту — и число вылезло за низ плитки:
/// бюджет высоты считал плитку на <c>2 · gap · rows</c> выше, чем её кладёт укладчик. Пока размер
/// держала ширина, ошибка была невидима.
/// </para>
/// <para>
/// Экран — параметр, а не константа: дизайн адаптивный, и телефон владельца здесь лишь один из
/// столбцов таблицы.
/// </para>
/// </summary>
public class TileFitsTheGridTests
{
    // Числа вида — те же, что в TilesLayout (библиотеку android отсюда не поднять). Разойдутся —
    // разойдётся и проверка, поэтому имена оставлены один в один.
    private const float RowHeightDp = 68;
    private const float GapDp = 3;
    private const float PaddingDp = 8;
    private const float LabelDp = 11;
    private const float SquareLabelDp = 10;
    private const float ValueTopMarginDp = 2;
    private const float HeatStrokeDp = 3;
    private const float InkRatio = 1.25f;

    /// <summary>Разряды до точки: у прямоугольных — принятые пять, у квадрата — засев (<c>MetricNumber</c>).</summary>
    private const int RectangleDigits = 5;
    private const int SquareDigits = 3;

    /// <summary>
    /// Мерилка вроде <c>PaintRuler</c>: кегль приходит в dp, ответ — в пикселях, то есть через
    /// плотность. Пропорции знаков те же, что у соседнего теста; важна не их точность, а то, что
    /// ширину и высоту меряет одна рука.
    /// </summary>
    private sealed class Ruler(float density) : ITextRuler
    {
        public float Width(string text, float sizeSp, bool mono) =>
            text.Length * sizeSp * density * (mono ? 0.6f : 0.5f);

        public float Height(float sizeSp) => sizeSp * density * InkRatio;
    }

    /// <summary>Поля самого списка плиток — по ним укладчик и режет клетку (<c>TilesScreen.ListPaddingDp</c>).</summary>
    private const float ListPaddingDp = 6;

    /// <summary>Сетка так, как её строит <c>TilesScreen.Metrics</c>: всё в пикселях этого экрана.</summary>
    private static TileMetrics Grid(float density, int screenPx)
    {
        float gap = MathF.Round(GapDp * density);
        float padding = MathF.Round(PaddingDp * density);
        float cell = MathF.Max(1, (screenPx - (MathF.Round(ListPaddingDp * density) * 2)) / 12);

        return new TileMetrics(
            CellWidthPx: cell,
            RowHeightPx: MathF.Round(RowHeightDp * density),
            GapPx: gap,
            PaddingPx: padding,
            LabelHeightPx: LabelDp * density * InkRatio,
            HeatBarPx: MathF.Round(HeatStrokeDp * density),
            EditReservePx: MathF.Round(22 * density),
            EditFooterPx: MathF.Round(14 * density),
            GapUnitPx: MathF.Round(4 * density),
            GapLabelPx: MathF.Round(12 * density),
            MarkPx: MathF.Round(12 * density),
            ValueBleedPx: MathF.Round(4 * density))
        {
            SquareLabelPx = SquareLabelDp * density * InkRatio,
        };
    }

    /// <summary>
    /// Высота плитки <b>по укладчику</b>: он врезает каждую плитку на просвет сверху и снизу, а не
    /// раздаёт просветы между строками. Считается здесь заново нарочно — этим вторым путём
    /// проверка и живёт.
    /// </summary>
    private static float PackedHeight(TileClass tile, TileMetrics grid) =>
        (tile.Rows * grid.RowHeightPx) - (2 * grid.GapPx);

    /// <summary>
    /// Ширина плитки <b>по укладчику</b>: клетка режется по колонкам от ширины списка без его полей,
    /// и плитка врезается внутрь на просвет слева и справа. Тоже вторым путём — той же арифметикой,
    /// какой считает <c>TileGridLayoutManager.OnLayoutChildren</c>, а не формулой бюджета.
    /// </summary>
    private static float PackedWidth(TileClass tile, TileMetrics grid, float density, int screenPx)
    {
        float usable = screenPx - (MathF.Round(ListPaddingDp * density) * 2);

        return (tile.Columns * usable / 12) - (2 * grid.GapPx);
    }

    /// <summary>
    /// Сколько ширины достаётся строке «число + единица»: поля плитки, у квадрата — с прибавкой
    /// вылета за поля, у «строки» — минус подпись слева с её просветом.
    /// </summary>
    private static float RoomForLine(TileClass tile, TileForm form, TileMetrics grid, float density,
        int screenPx, string label, bool mark, ITextRuler ruler)
    {
        float box = PackedWidth(tile, grid, density, screenPx);

        // Рамка жара идёт по всем четырём сторонам и на боках отнимает у числа столько же, сколько
        // сверху: зона числа кончается там, где начинается её линия, — иначе край рамки режет
        // последний знак (телефон, 11.08.2026).
        float frame = 2 * grid.HeatBarPx;

        return form switch
        {
            TileForm.Square => box - (2 * grid.PaddingPx) + (2 * grid.ValueBleedPx) - frame,
            TileForm.Row => box - (2 * grid.PaddingPx) - frame - grid.GapLabelPx - MathF.Min(
                ruler.Width(label, grid.RowLabelSp, mono: false) + (mark ? grid.MarkPx : 0),
                box * grid.RowLabelShare),
            _ => box - (2 * grid.PaddingPx) - frame,
        };
    }

    /// <summary>
    /// Сколько высоты достаётся самому числу в разметке плитки: поля, затем — по форме — строка
    /// подписи с отступом («столбик»), отступ числа (квадрат) либо ничего («строка», где подпись
    /// стоит сбоку).
    /// </summary>
    private static float RoomForNumber(TileClass tile, TileForm form, TileMetrics grid, float density)
    {
        float inner = PackedHeight(tile, grid) - (2 * grid.PaddingPx);
        float valueMargin = MathF.Round(ValueTopMarginDp * density);

        return form switch
        {
            TileForm.Row => inner,
            TileForm.Square => inner - valueMargin,
            _ => inner - (LabelDp * density * InkRatio) - valueMargin,
        };
    }

    /// <summary>Раскладка, с которой приложение стартует (<c>TilesLayout.Fixed</c>), — величинами и классами.</summary>
    private static readonly (string Metric, int Columns, int Rows, string Label, bool Mark)[] Layout =
    [
        ("speed", 12, 2, "Скорость", false),
        ("pwm", 6, 2, "ШИМ", false),
        ("battery_level", 6, 2, "Заряд", false),
        ("voltage", 6, 2, "Напряжение", false),
        ("current", 3, 1, "Ток", false),
        ("power", 3, 1, "Мощн.", false),
        ("phase_current", 3, 1, "Фазный", false),
        // Мин-максы стоят крайними (владелец 11.08.2026) — с пометкой, а не отдельной величиной.
        ("speed", 3, 1, "Скорость", true),
        ("current", 3, 1, "Ток", true),
        ("system_temp", 3, 1, "Темп.", false),
        ("temp2", 3, 1, "Мотор", false),
        ("tilt", 3, 1, "Наклон", false),
        ("distance", 6, 1, "За поездку", false),
        ("totaldistance", 6, 1, "Одометр", false),
        ("pwm", 6, 1, "ШИМ", true),
        ("voltage", 6, 1, "Напряжение", true),
    ];

    private static IEnumerable<TileText> Texts(TileMetrics grid)
    {
        foreach (var text in Lines(grid)) yield return text;
    }

    private static IEnumerable<TileText> Lines(TileMetrics grid)
    {
        foreach (var (id, columns, rows, label, mark) in Layout)
        {
            var shape = new TileClass(columns, rows);
            var metric = MetricCatalogue.Find(id)!;
            int digits = TileTypography.IsSquare(shape, grid) ? SquareDigits : RectangleDigits;
            string whole = new('8', digits);
            string widest = metric.Decimals > 0 ? whole + "." + new string('8', metric.Decimals) : whole;
            string unit = metric.UnitKey switch
            {
                "UnitKmh" => "км/ч",
                "UnitPercent" => "%",
                "UnitVolts" => "В",
                "UnitAmperes" => "А",
                "UnitWatts" => "Вт",
                "UnitKm" => "км",
                "UnitCelsius" => "°C",
                _ => "",
            };

            yield return new TileText(shape, widest, unit, label, mark);
        }
    }

    /// <summary>
    /// Число не выходит за низ плитки ни на одном экране — ни в показе, ни в правке. Экраны взяты
    /// ходовые: от плотности 1 (эмулятор, планшет) до 3 (телефон-флагман); телефон владельца —
    /// плотность 2.
    /// </summary>
    [Theory]
    [InlineData(1.0f, 360, false)]
    [InlineData(2.0f, 720, false)]
    [InlineData(2.625f, 1080, false)]
    [InlineData(3.0f, 1440, false)]
    [InlineData(2.0f, 720, true)]
    public void A_number_never_leaves_the_box_the_packer_gives_it(float density, int screenPx, bool editing)
    {
        var grid = Grid(density, screenPx);
        var ruler = new Ruler(density);

        var faces = TileTypography.Measure(Texts(grid), grid, ruler, editing);

        foreach (var (tile, face) in faces)
        {
            float drawn = ruler.Height(face.ValueSp);
            float room = RoomForNumber(tile, face.Form, grid, density);

            Assert.True(drawn <= room,
                $"{tile.Columns}×{tile.Rows} ({face.Form}) при плотности {density}: строка {drawn:F1} px "
                + $"в месте {room:F1} px — низ числа срезан");
        }
    }

    /// <summary>
    /// Ширина — тем же вторым путём: строка «число с единицей» не выходит за бока плитки, которую
    /// кладёт укладчик.
    /// <para>
    /// <b>Случившаяся поломка.</b> С якорем запятой (11.08.2026) число заняло всю худшую строку, и
    /// запас, которым жили два промаха бюджета, исчез: плитка считалась шире настоящей (просветы
    /// складывались вместо вычитания), а единицу мерили чужим начертанием и отступом разметки
    /// вместо пробела-знака. На стенде это дало пустые четвертные и «77,6» без «В».
    /// </para>
    /// <para>
    /// Меряется <b>нарисованная</b> строка: число в моноширинном, затем пробел и единица — тем же
    /// моноширинным, только мельче (<c>MetricNumber.Compose</c> меняет спаном размер, а не шрифт).
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1.0f, 360, false)]
    [InlineData(2.0f, 720, false)]
    [InlineData(2.625f, 1080, false)]
    [InlineData(3.0f, 1440, false)]
    [InlineData(2.0f, 720, true)]
    public void A_line_never_leaves_the_sides_the_packer_gives_it(float density, int screenPx, bool editing)
    {
        var grid = Grid(density, screenPx);
        var ruler = new Ruler(density);

        var faces = TileTypography.Measure(Texts(grid), grid, ruler, editing);

        foreach (var text in Lines(grid))
        {
            var face = faces[text.Class];

            // Пол кегля — принятое исключение (см. «A_number_that_fits_nowhere_still_gets_the_floor»):
            // ниже него подбор не опускается, даже когда строка не влезает никак. Такую плитку
            // меряет глаз, а не этот замок.
            if (face.ValueSp <= grid.MinValueSp) continue;

            string unit = TileTypography.UnitOn(text.Class, text.Unit);

            float drawn = ruler.Width(text.Value, face.ValueSp, mono: true)
                + (unit.Length > 0 ? ruler.Width(" " + unit, face.UnitSp, mono: true) : 0);

            float room = RoomForLine(text.Class, face.Form, grid, density, screenPx, text.Label, text.Mark, ruler)
                - (editing ? grid.EditReservePx : 0);

            Assert.True(drawn <= room,
                $"{text.Class.Columns}×{text.Class.Rows} ({face.Form}) при плотности {density}: строка "
                + $"«{text.Value}{(unit.Length > 0 ? " " + unit : "")}» {drawn:F1} px в месте {room:F1} px "
                + $"— края срезаны");
        }
    }
}
