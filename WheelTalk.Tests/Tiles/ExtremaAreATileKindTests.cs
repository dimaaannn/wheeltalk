using System.Text.RegularExpressions;
using WheelTalk.Core.Metrics;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Мин-максы на «Цифрах» делаются <b>видом</b> плитки — крайним значением с пометкой ▲▼ и своим
/// сбросом, — а не второй величиной рядом с основной (решение владельца 11.08.2026).
/// <para>
/// Отсюда три правила, и каждое ломается молча: величины-максимумы не предлагаются в выборе, но
/// <b>остаются в каталоге</b> (иначе собранная раньше раскладка потеряла бы плитку при первом же
/// чтении), а стартовый состав их не содержит вовсе.
/// </para>
/// </summary>
public class ExtremaAreATileKindTests
{
    /// <summary>Величины-максимумы сняты с предложения: такую плитку больше не заводят.</summary>
    [Theory]
    [InlineData("max_pwm")]
    [InlineData("top_speed")]
    public void A_maximum_metric_is_not_offered_any_more(string id)
    {
        var metric = MetricCatalogue.Find(id);

        Assert.NotNull(metric);
        Assert.False(metric.Offered);
    }

    /// <summary>
    /// Но из каталога они не выброшены: раскладка, собранная до решения, читается и показывает их
    /// как прежде. Выброс из каталога отбросил бы такую плитку молча — правилом «плитка со
    /// ссылкой на неизвестную величину отвергается целиком».
    /// </summary>
    [Theory]
    [InlineData("max_pwm")]
    [InlineData("top_speed")]
    public void An_old_layout_still_finds_them(string id)
    {
        Assert.NotNull(MetricCatalogue.Find(id));
    }

    /// <summary>Остальные величины предлагаются, как и предлагались: снято ровно две.</summary>
    [Fact]
    public void Only_the_two_maximum_metrics_left_the_choice()
    {
        var hidden = MetricCatalogue.All.Where(metric => !metric.Offered).Select(metric => metric.Id);

        Assert.Equal(["max_pwm", "top_speed"], hidden);
    }

    /// <summary>
    /// Стартовый состав держит мин-максы только крайними. Проверяется по исходнику: раскладка живёт
    /// в android-библиотеке, а правило простое и читается глазами — величины-максимумы в списке не
    /// упоминаются вовсе, а на их местах стоят крайние по скорости и току.
    /// </summary>
    [Fact]
    public void The_starting_layout_keeps_extrema_as_a_kind()
    {
        string fixedLayout = Starting();

        Assert.DoesNotContain("max_pwm", fixedLayout);
        Assert.DoesNotContain("top_speed", fixedLayout);

        Assert.Contains("new(\"speed\", TileKind.Extremum, new(3, 1)", fixedLayout);
        Assert.Contains("new(\"current\", TileKind.Extremum, new(3, 1)", fixedLayout);
    }

    /// <summary>
    /// Состав умолчания — тот, что владелец собрал руками на телефоне и принял 11.08.2026, один в
    /// один: величина, вид и размер каждой из четырнадцати плиток в его порядке, а следом — его же
    /// пер-плиточные настройки. Замок на состав, а не на вкус: раскладку правит человек, а эта —
    /// то, что он <b>принял</b> как начало для свежей установки. Двинется — двинется молча, и
    /// заметить будет нечем.
    /// </summary>
    [Fact]
    public void The_starting_layout_is_the_one_the_owner_assembled()
    {
        var tiles = Regex.Matches(Starting(), @"new\(""(\w+)"", TileKind\.(\w+), new\((\d+), (\d+)\)")
            .Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value} {m.Groups[3].Value}×{m.Groups[4].Value}");

        Assert.Equal(
        [
            "speed Chart 12×3",
            "pwm Chart 6×2",
            "voltage Chart 6×2",
            "speed Extremum 3×1",
            "pwm Extremum 3×1",
            "current Extremum 3×1",
            "voltage Extremum 3×1",
            "battery_level Value 6×2",
            "current Value 3×1",
            "power Value 3×1",
            "system_temp Value 3×1",
            "temp2 Value 3×1",
            "distance Value 6×1",
            "totaldistance Value 6×1",
        ], tiles);

        // Пер-плиточное — часть того же умолчания: пороги скорости, ШИМ и температур, обрезка
        // графиков по крайним значениям, пики у ШИМ и выключенная полоса жара у его пика.
        string source = Starting();

        Assert.Contains("new TileLimits(25, 50, Rising: true)", source);
        Assert.Contains("new TileLimits(70, 80, Rising: true)", source);
        Assert.Contains("new TileLimits(50, 80, Rising: true)", source);
        Assert.Contains("new TileLimits(60, 80, Rising: true)", source);
        Assert.Contains("Smoothing: ChartSmoothing.Peaks", source);
        Assert.Contains("ShowHeatBar: false", source);
        Assert.Equal(3, Regex.Matches(source, @"Zoom: true").Count);
    }

    /// <summary>
    /// Имена плиток в умолчании не зашиты: их раздаёт экран при чтении и тут же сохраняет. Зашитое
    /// имя было бы одним и тем же у всех установок, а по нему хранятся точки отсчёта дистанций.
    /// </summary>
    [Fact]
    public void The_starting_layout_hardcodes_no_tile_names()
    {
        Assert.DoesNotContain("Id =", Starting());
    }

    private static string Starting() => RepoFiles.MethodBody(
        RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/Tiles/TilesLayout.cs"),
        "public static IReadOnlyList<MetricTile> Fixed =>");

    /// <summary>
    /// Пометки ▲▼ — только на плитке. В меню плитки сторона крайнего называется <b>словами</b>
    /// (решение владельца 11.08.2026): значок в списке выбора нечитаем и непереводим, а строка
    /// ресурса говорит прямо.
    /// </summary>
    [Fact]
    public void The_menu_names_the_side_with_words()
    {
        string editor = RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/Tiles/TileEditor.cs");

        Assert.Contains("translate(\"TilesTileLowest\")", editor);
        Assert.DoesNotContain("▲", editor);
        Assert.DoesNotContain("▼", editor);

        // Само слово — про минимум и максимум, а не про «нижний край».
        string words = RepoFiles.Read("WheelTalk.Droid/Resources/Strings/AppStrings.resx");
        Assert.Contains("<data name=\"TilesTileLowest\"", words);
        Assert.Contains("минимум", words);
    }
}
