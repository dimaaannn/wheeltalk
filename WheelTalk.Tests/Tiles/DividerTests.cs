using System.Text.Json;
using System.Text.Json.Serialization;
using WheelTalk.Core.Tiles;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Разделитель — самостоятельный элемент раскладки: полоса во всю ширину, дающая между кучками
/// плиток видимый зазор (решение владельца 11.08.2026). Свойство плитки «начинать новую группу»,
/// которым это пробовали сделать сперва, им же отвергнуто: волосяная черта по краю соседа ничего не
/// отделяет, да и отступ принадлежит раскладке, а не плитке.
/// </summary>
public class DividerTests
{
    private const string Tiles = "WheelTalk.Dashboard.Droid/Screen/Tiles/";

    /// <summary>Форма записи наша — та же, что у остальных полей раскладки.</summary>
    private sealed class TileDto
    {
        public string? Id { get; set; }
        public string? Kind { get; set; }
        public string? Metric { get; set; }
        public int Columns { get; set; }
        public int Rows { get; set; }
        public bool Label { get; set; } = true;
        public bool HeatBar { get; set; } = true;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Строки разной высоты: разделитель занимает свою и делает её ниже обычной, а соседи съезжают
    /// ровно на разницу — не больше и не меньше. Это и есть та арифметика, которой укладчик раньше
    /// не знал вовсе: место строки считалось умножением, и элемент нецелой высоты сдвинул бы всё,
    /// что ниже, на целую строку.
    /// </summary>
    [Fact]
    public void A_divider_shortens_only_its_own_row()
    {
        float[] plain = TileRows.Tops([68, 68, 68]);
        float[] withDivider = TileRows.Tops([68, 24, 68]);

        // Строка над разделителем стоит там же, строка под ним поднялась ровно на 68 − 24.
        Assert.Equal(68, withDivider[1]);
        Assert.Equal(plain[2] - 44, withDivider[2]);
        Assert.Equal(plain[3] - 44, withDivider[3]);

        // Последним числом — низ всей сетки: по нему считается длина прокрутки.
        Assert.Equal(160, withDivider[^1]);
    }

    /// <summary>Пустая сетка — пустая раскладка: ноль строк, ноль высоты, и никаких особых случаев.</summary>
    [Fact]
    public void No_rows_means_no_height()
    {
        Assert.Equal([0f], TileRows.Tops([]));
    }

    /// <summary>
    /// Разделитель переживает круг «запись — чтение»: вид пишется и читается одним словом, а
    /// величина ему не нужна вовсе — у него нет содержимого.
    /// </summary>
    [Fact]
    public void A_divider_survives_the_round_trip()
    {
        const string saved = """
            [{"id":"a1b2c3d4","kind":"divider","columns":12,"rows":1}]
            """;

        var tiles = JsonSerializer.Deserialize<List<TileDto>>(saved, Options);

        Assert.NotNull(tiles);
        Assert.Equal("divider", tiles[0].Kind);
        Assert.Null(tiles[0].Metric);

        var again = JsonSerializer.Deserialize<List<TileDto>>(
            JsonSerializer.Serialize(tiles, Options), Options);

        Assert.Equal("divider", again![0].Kind);
        Assert.Equal("a1b2c3d4", again[0].Id);

        string json = RepoFiles.Read(Tiles + "TileLayoutJson.cs");
        Assert.Contains("TileKind.Divider => \"divider\"", json);
        Assert.Contains("case \"divider\":", json);
    }

    /// <summary>
    /// Раскладка, собранная в тот день, когда разделитель был свойством, читается без единой
    /// оговорки: поле <c>group</c> формат больше не знает, а незнакомое поле он игнорирует. Ради
    /// этого мёртвое объявление в DTO не оставлено — оно врало бы о составе формата.
    /// </summary>
    [Fact]
    public void A_layout_from_the_day_of_the_group_flag_still_reads()
    {
        const string saved = """
            [{"id":"18404b5b","group":true,"kind":"value","metric":"speed","columns":12,"rows":2}]
            """;

        var tiles = JsonSerializer.Deserialize<List<TileDto>>(saved, Options);

        Assert.NotNull(tiles);
        Assert.Single(tiles);
        Assert.Equal("speed", tiles[0].Metric);

        // Свойства нет ни в плитке, ни в кодеке, ни в меню правки — снято целиком.
        Assert.DoesNotContain("GroupStart", RepoFiles.Read(Tiles + "MetricTile.cs"));
        Assert.DoesNotContain("Group", RepoFiles.Read(Tiles + "TileLayoutJson.cs"));
        Assert.DoesNotContain("TilesTileGroup", RepoFiles.Read(Tiles + "TileEditor.cs"));
    }

    /// <summary>
    /// Устройство элемента: своя пониженная строка (зазор), тонкая линия по её середине (называет
    /// зазор, а не спорит с рамками плиток) и служебная густота — та же, что у ручки правки.
    /// </summary>
    [Fact]
    public void The_gap_is_made_by_height_and_only_named_by_the_line()
    {
        string layout = RepoFiles.Read(Tiles + "TilesLayout.cs");

        Assert.Contains("public static int DividerRowDp => 24;", layout);
        Assert.Contains("public static float DividerLineDp => 1.5f;", layout);
        Assert.Contains("public static int DividerAlpha => 110;", layout);

        // Строка разделителя ниже обычной — иначе зазора не выйдет вовсе.
        Assert.Contains("public static int RowHeightDp => 68;", layout);

        string view = RepoFiles.Read(Tiles + "DividerView.cs");
        Assert.Contains("canvas.DrawLine(0, middle, Width, middle, _line)", view);
    }

    /// <summary>
    /// Укладчик знает про разделитель и спрашивает о нём раскладку, а не гадает по размеру: элемент
    /// движется и удаляется в правке как всякий другой — для сетки он такой же член списка.
    /// </summary>
    [Fact]
    public void The_packer_asks_the_layout_which_row_is_a_divider()
    {
        string manager = RepoFiles.Read(Tiles + "TileGridLayoutManager.cs");

        Assert.Contains("Func<int, bool> dividerAt", manager);
        Assert.Contains("if (dividerAt(position)) heights[row] = dividerHeight;", manager);
        Assert.Contains("TileRows.Tops(heights)", manager);

        Assert.Contains(
            "public bool DividerAt(int position) => _tiles[position].Tile.Kind == TileKind.Divider;",
            RepoFiles.Read(Tiles + "TilesScreen.cs"));
    }
}
