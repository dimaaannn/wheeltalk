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
    /// полей 65, минус знак ▲ своим крупным кеглем с просветом.
    /// </summary>
    private static float Room(float labelSp)
    {
        var ruler = new Ruler();

        return 65 - ruler.Width("▲ ", labelSp * MarkScale, mono: false);
    }

    /// <summary>
    /// Слова его раскладки — те, что стоят на четвертных: короткие для скорости и напряжения
    /// заведены этим же заходом, остальные были. Ни одно не вылезает за полоску.
    /// </summary>
    [Theory]
    [InlineData("Скор.")]
    [InlineData("Напр.")]
    [InlineData("ШИМ")]
    [InlineData("Ток")]
    [InlineData("Мощн.")]
    [InlineData("Темп.")]
    [InlineData("Мотор")]
    public void Every_word_of_the_owners_layout_fits_the_corner(string word)
    {
        const float labelSp = 10;
        var ruler = new Ruler();

        var fit = CornerLabel.Fit(word, Room(labelSp), labelSp, 8, ruler);

        Assert.True(ruler.Width(fit.Word, fit.WordSp, mono: false) <= Room(labelSp),
            $"«{word}» набрано {fit.WordSp} sp и не влезло в {Room(labelSp):F1} dp");
        Assert.DoesNotContain(CornerLabel.Ellipsis, fit.Word);
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
        Assert.Equal("Температура", fit.Word);
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
    /// Полоска перестроена под крупный знак <b>целиком</b>: её высота считается по нему, и тем же
    /// числом отступает число — иначе знак «воткнут в старую разметку», а подпись спорит с
    /// показанием (слова владельца 11.08.2026).
    /// </summary>
    [Fact]
    public void The_strip_is_built_for_the_big_mark_and_the_number_starts_below_it()
    {
        string view = RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/Tiles/TileView.cs");

        Assert.Contains(
            "Context!.Dp(TilesLayout.SquareLabelSp * TilesLayout.MarkScale) * TilesLayout.InkRatio",
            view);
        Assert.Contains("CornerLabel.Fit(", view);

        // Тем же числом живёт и бюджет подбора кегля — счёт один на разметку и на подбор.
        Assert.Contains(
            "_context.Sp(TilesLayout.SquareLabelSp * TilesLayout.MarkScale) * TilesLayout.InkRatio",
            RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/Tiles/TilesScreen.cs"));

        foreach (string drawer in (string[])["MetricTileView.cs", "ExtremumTileView.cs", "TripTileView.cs"])
        {
            Assert.Contains(
                "TileForm.Square => CornerStripPx(),",
                RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/Tiles/" + drawer));
        }
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
