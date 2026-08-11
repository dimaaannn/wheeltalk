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
    /// Полоска построена под крупный знак <b>целиком</b> и считается <b>одной формулой в одном
    /// месте</b>: по ней отступает число и по ней же идёт бюджет подбора кегля. Два счёта об одном
    /// — то, чем полоска уже ломалась: знак вырос, а разметка осталась прежней (владелец
    /// 11.08.2026).
    /// </summary>
    [Fact]
    public void The_strip_is_one_count_for_the_layout_and_for_the_budget()
    {
        // Формула живёт в раскладке: угловой отступ, крупный знак, поправка начертания — и общее
        // поле, которого подпись не занимает, потому что сидит выше него.
        Assert.Contains(
            "CornerInsetDp + (SquareLabelSp * MarkScale * InkRatio) - PaddingDp",
            RepoFiles.Read(Tiles + "TilesLayout.cs"));

        // Оба потребителя берут её готовой: своей арифметики у них нет — ни у разметки, ни у бюджета.
        string view = RepoFiles.Read(Tiles + "TileView.cs");
        Assert.Contains("CornerStripPx() => Context!.Dp(TilesLayout.CornerStripDp)", view);
        Assert.Contains("CornerLabel.Fit(", view);

        Assert.Contains(
            "SquareLabelPx = _context.Dp(TilesLayout.CornerStripDp)",
            RepoFiles.Read(Tiles + "TilesScreen.cs"));

        foreach (string drawer in (string[])["MetricTileView.cs", "ExtremumTileView.cs", "TripTileView.cs"])
        {
            Assert.Contains("TileForm.Square => CornerStripPx(),", RepoFiles.Read(Tiles + drawer));
        }
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

        // Рисует подпись по этому отступу, а не по общему полю: рисование и есть то место, где
        // прижатие либо случилось, либо нет.
        string corner = RepoFiles.MethodBody(
            RepoFiles.Read(Tiles + "TileView.cs"), "private void DrawCornerLabel(Canvas canvas)");

        Assert.Contains("Context!.Dp(TilesLayout.CornerInsetDp)", corner);
        Assert.DoesNotContain("TilesLayout.PaddingDp", corner);
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
