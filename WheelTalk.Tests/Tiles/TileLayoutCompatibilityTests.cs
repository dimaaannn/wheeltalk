using System.Text.Json;
using System.Text.Json.Serialization;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Старая сохранённая раскладка обязана читаться. Полоса жара по низу плитки появилась полем
/// <c>heatBar</c>, которого в сохранённых до неё файлах нет вовсе, — и отсутствие поля значит
/// «включена», а не «выключена»: человек ничего не выключал. Тем же порядком заведено округление
/// (<c>decimals</c>): нет поля — размерность типа величины, а не ноль знаков.
/// <para>
/// <b>Чем проверяется.</b> Разбор раскладки живёт в android-библиотеке, и поднять его отсюда
/// нельзя. Гарантия держится на двух вещах, и обе проверены здесь: <c>System.Text.Json</c>
/// оставляет отсутствующему в файле полю его начальное значение (первый тест — тем же
/// сериализатором и той же формой записи), а наш DTO это начальное значение объявляет
/// (второй — чтением исходника, как замок §29.2). Убери инициализатор — старые раскладки молча
/// погасят полосы, и заметить это будет нечем.
/// </para>
/// </summary>
public class TileLayoutCompatibilityTests
{
    /// <summary>Форма записи наша: имена в camelCase, поле полосы с умолчанием «включена».</summary>
    private sealed class TileDto
    {
        public string? Kind { get; set; }
        public string? Metric { get; set; }
        public int Columns { get; set; }
        public int Rows { get; set; }
        public bool Label { get; set; } = true;
        public bool HeatBar { get; set; } = true;

        /// <summary>Своё округление плитки. Пусто — «по умолчанию»; ноль — «показывать целыми».</summary>
        public int? Decimals { get; set; }
    }

    /// <summary>Те же настройки разбора, что у боевого контекста (<c>TileLayoutJson.TileLayoutContext</c>).</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void A_layout_saved_before_the_bar_existed_keeps_the_bar_on()
    {
        // Ровно то, что лежит на телефоне у того, кто собирал раскладку до этой правки.
        const string saved = """
            [{"kind":"value","metric":"speed","columns":12,"rows":2,"label":true}]
            """;

        var tiles = JsonSerializer.Deserialize<List<TileDto>>(saved, Options);

        Assert.NotNull(tiles);
        Assert.Single(tiles);
        Assert.True(tiles[0].HeatBar, "поля в файле нет — полоса обязана остаться включённой");
        Assert.True(tiles[0].Label);
    }

    [Fact]
    public void A_layout_that_turned_the_bar_off_keeps_it_off()
    {
        const string saved = """
            [{"kind":"value","metric":"totaldistance","columns":6,"rows":1,"heatBar":false}]
            """;

        var tiles = JsonSerializer.Deserialize<List<TileDto>>(saved, Options);

        Assert.NotNull(tiles);
        Assert.False(tiles[0].HeatBar);
    }

    /// <summary>
    /// Округление появилось полем <c>decimals</c>, и его отсутствие значит «по умолчанию» —
    /// умолчание типа величины, а не ноль знаков. Разница не косметическая: ноль огрубил бы старой
    /// раскладке все числа разом, включая скорость и напряжение.
    /// </summary>
    [Fact]
    public void A_layout_saved_before_rounding_existed_keeps_the_metric_default()
    {
        const string saved = """
            [{"kind":"value","metric":"speed","columns":12,"rows":2,"label":true,"heatBar":true}]
            """;

        var tiles = JsonSerializer.Deserialize<List<TileDto>>(saved, Options);

        Assert.NotNull(tiles);
        Assert.Null(tiles[0].Decimals);
    }

    /// <summary>
    /// А заданный ноль — это выбор «показывать целыми», и от «поля нет» он обязан отличаться:
    /// ради этого различия поле и объявлено <c>int?</c>, а не <c>int</c> с умолчанием.
    /// </summary>
    [Fact]
    public void A_tile_told_to_show_whole_numbers_keeps_that()
    {
        const string saved = """
            [{"kind":"value","metric":"pwm","columns":6,"rows":2,"decimals":0}]
            """;

        var tiles = JsonSerializer.Deserialize<List<TileDto>>(saved, Options);

        Assert.NotNull(tiles);
        Assert.Equal(0, tiles[0].Decimals);
    }

    /// <summary>
    /// Замок на само объявление: без инициализатора отсутствующее поле прочтётся как <c>false</c>,
    /// и старая раскладка потеряет полосы молча — ни исключения, ни строки в журнале. У округления
    /// та же беда с другой стороны: не-<c>null</c>-тип превратил бы «не задано» в «целыми».
    /// </summary>
    [Fact]
    public void The_real_dto_declares_the_bar_as_on_by_default()
    {
        string source = RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/Tiles/TileLayoutJson.cs");

        Assert.Contains("public bool HeatBar { get; set; } = true;", source);
        Assert.Contains("public bool Label { get; set; } = true;", source);
        Assert.Contains("public int? Decimals { get; set; }", source);
    }
}
