using WheelTalk.Core.Metrics;
using WheelTalk.Core.Tiles;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Гейт «ничего не срезано» (план плиток §8.2) — счётом, а не глазами: подобранное число обязано
/// уместиться в <b>то место, которое даёт укладчик</b>, на любом экране.
/// <para>
/// <b>Дыра, ради которой тест заведён.</b> Соседний <c>TileTypographyTests</c> проверяет подбор
/// против того же бюджета, по которому подбор и считает: разойдись бюджет с настоящей плиткой —
/// оба соврут согласованно и промолчат. Здесь место считается <b>вторым путём</b> — по правилу
/// укладчика (<c>TileGridLayoutManager.OnLayoutChildren</c>: <c>top = row·H + gap</c>,
/// <c>bottom = (row+rows)·H − gap</c>) и по разметке самой плитки (поля, полоска подписи, вылет
/// числа). Два пути сходятся — число внутри; разошлись — на телефоне срез.
/// </para>
/// <para>
/// <b>Ревизия 12.08.2026.</b> Оба пути этого замка отстали от кода и стерегли позапрошлый экран:
/// раскладка-список держала состав, снятый решением владельца 11.08 (12×2 у скорости, фазный ток,
/// наклон — и ни разделителя, ни полосы 12×3), а место подписи считалось как <c>кегль × InkRatio</c>,
/// хотя боевой экран с 12.08 меряет её по кромкам глифов. Квадрат при этом не стерёгся вовсе: его
/// «второй путь» вычитал отступ строки в 2 dp вместо целой полоски и имел постоянный запас в
/// два десятка пикселей. Теперь состав <b>сверяется с <c>TilesLayout.Fixed</c></b>
/// (<see cref="OwnerLayout"/>), а полоска считается боевой формулой ядра
/// (<see cref="LabelStrip.Px"/>) — той же, что зовёт <c>TileLabelStyle</c> на телефоне.
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
    private const float HeatStrokeDp = 3;
    private const float CornerInsetDp = HeatStrokeDp + 3;
    private const float MarkScale = 1.5f;
    private const float InkRatio = 1.25f;

    /// <summary>Разряды до точки: у прямоугольных — принятые пять, у квадрата — засев (<c>MetricNumber</c>).</summary>
    private const int RectangleDigits = 5;
    private const int SquareDigits = 3;

    /// <summary>
    /// Начертание подписи, доля кегля: докуда достаёт капс вверх и куда уходит вынос «Щ» вниз.
    /// Пропорции робото-подобного шрифта — <b>модель</b>, и это честно: настоящие кромки живут в
    /// шрифте телефона (<c>Paint.GetTextBounds</c>), отсюда их не снять. Заперта здесь не буква, а
    /// формула места под неё — её тест зовёт боевую.
    /// </summary>
    private const float CapHeight = 0.71f;
    private const float Descender = 0.10f;

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

    /// <summary>
    /// Полоска подписи — <b>боевой формулой ядра</b> (<see cref="LabelStrip.Px"/>), той же, которой
    /// её считает <c>TileLabelStyle.StripPx</c> на телефоне: угловой отступ, высота краски строки и
    /// вычтенное общее поле. Строка меряется по худшему жителю — знаку ▲ своим крупным кеглем и
    /// капсу с выносом вниз.
    /// </summary>
    private static float StripPx(float labelDp, float density)
    {
        float word = MathF.Round(labelDp * density);
        float inkTop = -CapHeight * word * MarkScale;
        float inkBottom = Descender * word;

        return MathF.Round(LabelStrip.Px(
            MathF.Round(CornerInsetDp * density), inkTop, inkBottom, MathF.Round(PaddingDp * density)));
    }

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
            // Обе подписи — той же полоской, что отступает содержимое: счёт один на разметку и на
            // бюджет, как его и ведёт TilesScreen.Metrics.
            LabelHeightPx: StripPx(LabelDp, density),
            HeatBarPx: MathF.Round(HeatStrokeDp * density),
            EditReservePx: MathF.Round(22 * density),
            EditFooterPx: MathF.Round(14 * density),
            GapUnitPx: MathF.Round(4 * density),
            GapLabelPx: MathF.Round(12 * density),
            MarkPx: MathF.Round(18 * density),
            ValueBleedPx: MathF.Round(4 * density))
        {
            SquareLabelPx = StripPx(SquareLabelDp, density),
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
    /// Сколько высоты достаётся самому числу в разметке плитки: поля, а под ними — <b>полоска
    /// подписи</b>, которой плитка отступает содержимое (<c>TileView.LabelStripPx</c>, и у квадрата,
    /// и у столбика — одним числом). В «строке» подпись стоит сбоку и сверху не берёт ничего.
    /// <para>
    /// До ревизии 12.08.2026 здесь у квадрата вычитался отступ строки в 2 dp, а не полоска: место
    /// считалось на два десятка пикселей щедрее настоящего, и квадрат этим замком не стерёгся.
    /// </para>
    /// </summary>
    private static float RoomForNumber(TileClass tile, TileForm form, TileMetrics grid, float density)
    {
        float inner = PackedHeight(tile, grid) - (2 * grid.PaddingPx);

        return form switch
        {
            TileForm.Row => inner,
            TileForm.Square => inner - StripPx(SquareLabelDp, density),
            _ => inner - StripPx(LabelDp, density),
        };
    }

    /// <summary>
    /// Раскладка, с которой приложение стартует, — величинами и классами. Держится равной
    /// <c>TilesLayout.Fixed</c> замком ниже: до ревизии 12.08.2026 этот список дважды уезжал от
    /// боевого молча, и оба раза замок продолжал светить зелёным на снятом составе.
    /// </summary>
    private static readonly (string Metric, int Columns, int Rows, string Label, bool Mark)[] Layout =
    [
        ("speed", 12, 3, "Скорость", false),
        ("pwm", 6, 2, "ШИМ", false),
        ("voltage", 6, 2, "Напряжение", false),
        // Ряд крайних: пометка ▲▼ ставится видом плитки, а не второй величиной.
        ("speed", 3, 1, "Скор.", true),
        ("pwm", 3, 1, "ШИМ", true),
        ("current", 3, 1, "Ток", true),
        ("voltage", 3, 1, "Напр.", true),
        ("battery_level", 6, 2, "Заряд", false),
        ("current", 3, 1, "Ток", false),
        ("power", 3, 1, "Мощн.", false),
        ("system_temp", 3, 1, "Темп.", false),
        ("temp2", 3, 1, "Мотор", false),
        ("distance", 6, 1, "За поездку", false),
        ("totaldistance", 6, 1, "Одометр", false),
    ];

    /// <summary>
    /// Список выше — тот самый, что стоит в <c>TilesLayout.Fixed</c>: величина, вид и размер каждого
    /// места, в его порядке. Разделитель числа не показывает и в подборе не участвует, поэтому из
    /// сверки он выпадает — но его место в раскладке стережёт <c>ExtremaAreATileKindTests</c>.
    /// </summary>
    [Fact]
    public void The_measured_layout_is_the_one_the_app_starts_with()
    {
        Assert.Equal(
            OwnerLayout.Tiles().Select(spot => $"{spot.Metric} {spot.Columns}×{spot.Rows}"),
            Layout.Select(tile => $"{tile.Metric} {tile.Columns}×{tile.Rows}"));
    }

    /// <summary>
    /// Подпись не вправе съесть у мелкой плитки больше четверти высоты. Замок абсолютный, и в этом
    /// его смысл: бюджет с разметкой считаются одной формулой и вырастут <b>вместе</b>, согласованно
    /// промолчав, — а вот полоска, ставшая вдвое выше, отберёт место у числа на всех квадратах разом
    /// и не уронит ни одной сверки «двух путей».
    /// </summary>
    [Theory]
    [InlineData(1.0f)]
    [InlineData(2.0f)]
    [InlineData(3.0f)]
    public void The_label_never_eats_more_than_a_quarter_of_the_smallest_tile(float density)
    {
        float tile = MathF.Round(RowHeightDp * density) - (2 * MathF.Round(GapDp * density));

        foreach (float labelDp in (float[])[SquareLabelDp, LabelDp])
        {
            float strip = StripPx(labelDp, density);

            Assert.InRange(strip, MathF.Round(CornerInsetDp * density), tile / 4);
        }
    }

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
