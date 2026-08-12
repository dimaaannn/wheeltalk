using System.Globalization;
using System.Text.RegularExpressions;
using WheelTalk.Core.Tiles;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Угловая подпись квадратной плитки: «▲ Скорость» обязано влезть в свою полоску при любой длине
/// слова — и на четвертной плитке тоже.
/// <para>
/// <b>Случившаяся поломка (владелец, 11.08.2026).</b> Пометку ▲▼ сделали в полтора раза крупнее, а
/// полоску под неё не перестроили: знак удлинил строку, ширина слова ничем не ограничивалась, и
/// канва — которая сама не ужимает и не обрезает — вывела «▼ Напряжени» прямо за край плитки. Здесь
/// стережётся весь путь владельца: его раскладка, его четвертные крайние, его слова.
/// </para>
/// </summary>
public class CornerLabelTests
{
    private const string Tiles = "WheelTalk.Dashboard.Droid/Screen/Tiles/";

    /// <summary>Знак ширины не меняет: он смысловой и не ужимается никогда.</summary>
    private const float MarkScale = 1.5f;

    /// <summary>Мерилка вроде шрифта подписи: знак обычного начертания — половина кегля.</summary>
    private sealed class Ruler : ITextRuler
    {
        public float Width(string text, float sizeSp, bool mono) => text.Length * sizeSp * (mono ? 0.6f : 0.5f);

        public float Height(float sizeSp) => sizeSp * 1.25f;
    }

    /// <summary>
    /// Место под слово на четвертной плитке владельца: 360 dp, колонка 29, плитка 3×1 — 81 dp без
    /// угловых отступов 69, минус знак ▲ своим крупным кеглем с просветом. Отступ угла свой, малый
    /// (<c>TilesLayout.CornerInsetDp</c>), а не общее поле плитки, — оттого места на 4 dp больше,
    /// чем было.
    /// </summary>
    private static float Room(float labelSp)
    {
        var ruler = new Ruler();

        return 69 - ruler.Width("▲ ", labelSp * MarkScale, mono: false);
    }

    /// <summary>
    /// Слова его раскладки — те, что стоят на четвертных: короткие для скорости и напряжения
    /// заведены этим же заходом, остальные были. Ни одно не вылезает за полоску, и каждое выходит
    /// <b>заглавными</b> (слова владельца 11.08.2026).
    /// </summary>
    [Theory]
    [InlineData("Скор.", "СКОР.")]
    [InlineData("Напр.", "НАПР.")]
    [InlineData("ШИМ", "ШИМ")]
    [InlineData("Ток", "ТОК")]
    [InlineData("Мощн.", "МОЩН.")]
    [InlineData("Темп.", "ТЕМП.")]
    [InlineData("Мотор", "МОТОР")]
    // «Ячейка» вместо «Банки» — ревизия текстов 12.08.2026 свела семью к одному слову. Оно на знак
    // длиннее прежнего, поэтому стоит здесь: угол у четвертной плитки самый тесный на экране.
    [InlineData("Ячейка", "ЯЧЕЙКА")]
    public void Every_word_of_the_owners_layout_fits_the_corner(string word, string caps)
    {
        const float labelSp = 10;
        var ruler = new Ruler();

        var fit = CornerLabel.Fit(word, Room(labelSp), labelSp, 8, ruler);

        Assert.Equal(caps, fit.Word);
        Assert.True(ruler.Width(fit.Word, fit.WordSp, mono: false) <= Room(labelSp),
            $"«{word}» набрано {fit.WordSp} sp и не влезло в {Room(labelSp):F1} dp");
        Assert.DoesNotContain(CornerLabel.Ellipsis, fit.Word);
    }

    /// <summary>
    /// Капс — не украшение рисующего, а часть посадки: слово поднимается <b>до</b> замера, и на
    /// экран уходит ровно то, что мерили. Мерь строчными, рисуй заглавными — и подпись вылезет за
    /// полоску ровно на разницу их ширин, молча, как уже вылезала.
    /// </summary>
    [Fact]
    public void The_word_is_measured_in_the_same_caps_it_is_drawn_in()
    {
        var ruler = new Ruler();

        var fit = CornerLabel.Fit("Скорость", room: 39, wordSp: 10, minSp: 8, ruler);

        // Наружу уходит поднятое слово — оно же и мерено: ужалось до 9, потому что заглавным своим
        // кеглем в это место не село.
        Assert.Equal("СКОРОСТЬ", fit.Word);
        Assert.Equal(9, fit.WordSp);
        Assert.True(ruler.Width(fit.Word, fit.WordSp, mono: false) <= 39);

        // Режется тоже поднятое: огрызок с многоточием — заглавный, как и всё слово.
        var cut = CornerLabel.Fit("Электродвигатель", room: 20, wordSp: 10, minSp: 8, ruler);

        Assert.Equal("ЭЛЕК" + CornerLabel.Ellipsis, cut.Word);
    }

    /// <summary>
    /// Длинное слово садится ужатием кегля, а не обрезкой: укоротить значит отнять смысл, уменьшить
    /// — только вес. Ужимается <b>слово</b>; знак остаётся крупным, он смысловой.
    /// </summary>
    [Fact]
    public void A_long_word_shrinks_before_it_is_cut()
    {
        const float labelSp = 10;

        // Полное имя величины на четвертной — тот самый случай: своим кеглем оно не влезает.
        var fit = CornerLabel.Fit("Температура", Room(labelSp), labelSp, 8, new Ruler());

        Assert.True(fit.WordSp < labelSp, "слово обязано ужаться");
        Assert.Equal("ТЕМПЕРАТУРА", fit.Word);
    }

    /// <summary>
    /// А то, что не садится и на полу читаемости, обрезается <b>честно</b> — многоточием, и всё
    /// равно влезает. Молча срезанное краем слово — ровно то, что владелец увидел на телефоне.
    /// </summary>
    [Fact]
    public void What_cannot_shrink_enough_is_cut_with_an_ellipsis()
    {
        var ruler = new Ruler();
        float room = Room(10);

        var fit = CornerLabel.Fit("Электродвигатель левый", room, 10, 8, ruler);

        Assert.Contains(CornerLabel.Ellipsis, fit.Word);
        Assert.True(ruler.Width(fit.Word, fit.WordSp, mono: false) <= room);
        Assert.Equal(8, fit.WordSp);
    }

    /// <summary>Места нет вовсе — не рисуем ничего: огрызок в один знак не подпись.</summary>
    [Fact]
    public void No_room_means_no_word()
    {
        Assert.Equal("", CornerLabel.Fit("Напряжение", 0, 10, 8, new Ruler()).Word);
    }

    /// <summary>
    /// Полоска считается <b>одной формулой в одном месте</b>: по ней отступает число и по ней же
    /// идёт бюджет подбора кегля — и у квадрата, и у строки разметки. Два счёта об одном — то, чем
    /// полоска уже ломалась: знак вырос, а разметка осталась прежней (владелец 11.08.2026).
    /// </summary>
    [Fact]
    public void The_strip_is_one_count_for_the_layout_and_for_the_budget()
    {
        string style = RepoFiles.Read(Tiles + "TileLabelStyle.cs");

        // Формула одна на все формы: угловой отступ, рисунок строки — и общее поле, которого
        // подпись не занимает, потому что сидит выше него. Разнится только кегль.
        Assert.Contains(
            "InsetPx(context) + (inkBottom - inkTop) - context.Dp(TilesLayout.PaddingDp)", style);
        Assert.Contains("public static int StripPx(Context context, float labelDp)", style);

        // Оба потребителя берут её готовой: своей арифметики нет ни у разметки, ни у бюджета.
        string view = RepoFiles.Read(Tiles + "TileView.cs");
        Assert.Contains("TileLabelStyle.StripPx(Context!, LabelSizeDp(form))", view);

        string screen = RepoFiles.Read(Tiles + "TilesScreen.cs");
        Assert.Contains("SquareLabelPx = TileLabelStyle.StripPx(_context, TilesLayout.SquareLabelSp)", screen);
        Assert.Contains("LabelHeightPx: TileLabelStyle.StripPx(_context, TilesLayout.LabelSp)", screen);

        // Ею же отступает содержимое всякой плитки: и число, и график.
        foreach (string drawer in (string[])["MetricTileView.cs", "ExtremumTileView.cs", "TripTileView.cs"])
        {
            Assert.Contains(
                "layout.TopMargin = face.Form == TileForm.Row ? 0 : LabelStripPx(face.Form);",
                RepoFiles.Read(Tiles + drawer));
        }

        Assert.Contains(
            "layout.TopMargin = LabelStripPx(TileForm.Stack);", RepoFiles.Read(Tiles + "ChartTileView.cs"));
    }

    /// <summary>
    /// Зазор меряется <b>по рисунку буквы</b>, а не по кеглю: у глифов свои внутренние поля, и
    /// отступ, отмеренный от номинала, оставляет над видимой кромкой пустоту сверх заданной — сдвиг
    /// с 8 dp на 6 владелец оттого и не увидел (11.08.2026).
    /// </summary>
    [Fact]
    public void The_gap_is_measured_by_the_ink_of_the_glyph_and_not_by_the_size()
    {
        // Кромки снимаются у шрифта, а не выводятся из кегля.
        Assert.Contains(
            "paint.GetTextBounds(text, 0, text.Length, Box);", RepoFiles.Read(Tiles + "TileLabelStyle.cs"));

        string view = RepoFiles.Read(Tiles + "TileView.cs");
        string placed = RepoFiles.MethodBody(view, "private LabelText PlaceLabel()");

        Assert.Contains("TileLabelStyle.InkOf(", placed);
        Assert.Contains("TileLabelStyle.BaselineFor(", placed);
        Assert.Contains("TileLabelStyle.LeftFor(", placed);

        // И ни замера, ни посадки в кадре: за каждым JNI, а рисуют шестьдесят раз в секунду
        // (уроки плана 31).
        string drawn = RepoFiles.MethodBody(view, "private void DrawLabel(Canvas canvas)");

        Assert.Contains("_placed ??= PlaceLabel()", drawn);
        Assert.DoesNotContain("CornerLabel.Fit(", drawn);
        Assert.DoesNotContain("MeasureText", drawn);
    }

    /// <summary>
    /// <b>Единообразие табличек</b> (слова владельца 11.08.2026: «единообразие вводили для быстрых
    /// правок, а не костылей»). Техника подписи одна на все формы — канва: ни одна форма не держит
    /// своего <c>TextView</c> подписи и ни одна не считает своих чисел стиля. Две техники и были
    /// корнем долготы: правка шла дважды, а поля шрифта с клипом по полю группы срезали буквам верх.
    /// </summary>
    [Fact]
    public void Every_form_draws_its_label_by_the_one_technique()
    {
        string view = RepoFiles.Read(Tiles + "TileView.cs");

        // Регистр — одной рукой на обе ветки подписи: обычную и помеченную.
        Assert.Contains("_label = showLabel ? TileLabelStyle.Caps(label) : \"\";", view);
        Assert.Contains("_label = TileLabelStyle.Caps($\"{mark} {label}\");", view);
        Assert.Contains(
            "public static string Caps(string label) => label.ToUpperInvariant();",
            RepoFiles.Read(Tiles + "TileLabelStyle.cs"));

        // Рисовальщик один, и зовётся он на всякую форму — не только на квадрат.
        Assert.Contains("if (_label.Length > 0) DrawLabel(canvas);", view);

        // Ни в рамке плитки, ни в её видах нет своего TextView подписи: он ушёл целиком.
        Assert.DoesNotContain("Label.SetIncludeFontPadding", view);
        Assert.DoesNotContain("protected TextView Label", view);

        foreach (string drawer in
                 (string[])["MetricTileView.cs", "ExtremumTileView.cs", "TripTileView.cs", "ChartTileView.cs"])
        {
            string source = RepoFiles.Read(Tiles + drawer);

            Assert.DoesNotContain("Label.", source);
            Assert.DoesNotContain("TilesLayout.LabelSp", source);
            Assert.DoesNotContain("TilesLayout.CornerInsetDp", source);
        }

        // Своих чисел стиля нет и у формы: кегль подписи она спрашивает, а не выбирает.
        string sizes = RepoFiles.MethodBody(view, "private static float LabelSizeDp(TileForm form)");

        Assert.Contains("TileForm.Square => TilesLayout.SquareLabelSp", sizes);
        Assert.Contains("TileForm.Row => TilesLayout.RowLabelSp", sizes);
    }

    /// <summary>
    /// Подпись прижата к углу <b>своим малым отступом</b>: он меньше общего поля — иначе прижимать
    /// было бы нечего, — и не меньше рамки с зазором, чтобы слово не легло на её линию (слова
    /// владельца 11.08.2026). Полоса жара идёт нижней стороной рамки и верхнего угла не касается,
    /// так что этот зазор от неё не зависит.
    /// </summary>
    [Fact]
    public void The_corner_inset_is_smaller_than_the_padding_and_clears_the_frame()
    {
        float inset = Knob("CornerInsetDp");
        float frame = Knob("HeatStrokeDp");
        float padding = Knob("PaddingDp");

        Assert.True(inset < padding, $"угловой отступ {inset} dp не меньше общего поля {padding} dp");
        Assert.True(inset >= frame + 2, $"подпись в {inset} dp прижата к рамке в {frame} dp без зазора");

        // Отступ один на все формы, и берут его из стиля, а не из общего поля плитки.
        string style = RepoFiles.Read(Tiles + "TileLabelStyle.cs");

        Assert.Contains(
            "public static int InsetPx(Context context) => context.Dp(TilesLayout.CornerInsetDp);", style);

        string placed = RepoFiles.MethodBody(
            RepoFiles.Read(Tiles + "TileView.cs"), "private LabelText PlaceLabel()");

        Assert.Contains("TileLabelStyle.InsetPx(Context!)", placed);
        Assert.DoesNotContain("TilesLayout.PaddingDp", placed);
    }

    /// <summary>
    /// Подпись <b>нечем срезать</b>: она не вид в разметке, а краска на канве самой плитки, и
    /// рисуется <b>после</b> детей — за пределами клипа, которым группа режет их по своему полю.
    /// Прежде подпись была видом с отрицательным отступом, и <c>clipToPadding</c> отъедал ей верх
    /// букв (телефон, 11.08.2026); гасить клип нельзя — краска пойдёт по соседям.
    /// </summary>
    [Fact]
    public void The_label_is_paint_on_the_canvas_and_nothing_can_clip_it()
    {
        string drawn = RepoFiles.MethodBody(
            RepoFiles.Read(Tiles + "TileView.cs"), "protected override void DispatchDraw(Canvas canvas)");

        // Порядок важен: сперва дети со своим клипом, потом подпись — уже без него.
        int children = drawn.IndexOf("base.DispatchDraw(canvas);", StringComparison.Ordinal);
        int label = drawn.IndexOf("DrawLabel(canvas);", StringComparison.Ordinal);

        Assert.True(children >= 0 && label > children, "подпись рисуется раньше детей — её срежет клипом");

        // И клип никто не гасит: это лечило бы срез ценой краски по соседям.
        string view = RepoFiles.Read(Tiles + "TileView.cs");

        Assert.DoesNotContain("SetClipToPadding", view);
        Assert.DoesNotContain("SetClipChildren", view);
    }

    /// <summary>
    /// Число ручки раскладки, прочитанное из её исходника: либо само число, либо сумма — «другая
    /// ручка + число», как задан угловой отступ (рамка плюс зазор). Сверяются <b>числа</b>, а не
    /// написание: android-библиотеку отсюда не поднять, но правило про зазор — арифметическое.
    /// </summary>
    private static float Knob(string name)
    {
        var found = Regex.Match(
            RepoFiles.Read(Tiles + "TilesLayout.cs"), $@"public static \w+ {name} => ([^;]+);");

        Assert.True(found.Success, $"в TilesLayout нет ручки {name}");

        float sum = 0;
        foreach (string term in found.Groups[1].Value.Split('+'))
        {
            string value = term.Trim().TrimEnd('f');
            sum += float.TryParse(value, CultureInfo.InvariantCulture, out float number) ? number : Knob(value);
        }

        return sum;
    }

    /// <summary>
    /// Короткие подписи четвертных уважают все виды, включая крайние: у скорости и напряжения их не
    /// было вовсе — эти величины не стояли четвертными, пока владелец не собрал ряд крайних.
    /// </summary>
    [Fact]
    public void The_quarter_words_exist_for_every_metric_of_that_row()
    {
        string words = RepoFiles.Read("WheelTalk.Droid/Resources/Strings/AppStrings.resx");
        string stand = RepoFiles.Read("WheelTalk.Lab.Droid/Ui/LabMetricWords.cs");

        foreach (string key in (string[])["TelemetrySpeedShort", "TelemetryVoltageShort"])
        {
            Assert.Contains(key, words);
            Assert.Contains(key, stand);
        }

        // Короткую берёт сама подпись плитки — по размеру, а не по виду: крайнее на четвертной
        // обязано звучать так же коротко, как величина на ней же.
        string screen = RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/Tiles/TilesScreen.cs");
        Assert.Contains("string key = metric.LabelKey + \"Short\";", screen);
    }
}
