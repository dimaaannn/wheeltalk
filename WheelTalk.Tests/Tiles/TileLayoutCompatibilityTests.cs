using System.Text.Json;
using System.Text.Json.Serialization;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Старая сохранённая раскладка обязана читаться. Полоса жара по низу плитки появилась полем
/// <c>heatBar</c>, которого в сохранённых до неё файлах нет вовсе, — и отсутствие поля значит
/// «включена», а не «выключена»: человек ничего не выключал.
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
    /// Замок на само объявление: без инициализатора отсутствующее поле прочтётся как <c>false</c>,
    /// и старая раскладка потеряет полосы молча — ни исключения, ни строки в журнале.
    /// </summary>
    [Fact]
    public void The_real_dto_declares_the_bar_as_on_by_default()
    {
        string source = RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/Tiles/TileLayoutJson.cs");

        Assert.Contains("public bool HeatBar { get; set; } = true;", source);
        Assert.Contains("public bool Label { get; set; } = true;", source);
    }
}
