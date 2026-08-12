using WheelTalk.Core.Tiles;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Разделитель — самостоятельный элемент раскладки: полоса во всю ширину, дающая между кучками
/// плиток видимый зазор (решение владельца 11.08.2026). Свойство плитки «начинать новую группу»,
/// которым это пробовали сделать сперва, им же отвергнуто: волосяная черта по краю соседа ничего не
/// отделяет, да и отступ принадлежит раскладке, а не плитке.
/// <para>
/// Здесь — арифметика строк разной высоты и замки по исходникам. Круги «запись — чтение» уехали
/// ревизией 12.08.2026 в <c>TileLayoutCompatibilityTests</c>, к своей родне: они шли через свой DTO
/// и <c>System.Text.Json</c>, боевого кодека не касаясь, и держать их среди настоящих замков значило
/// выдавать проверку сериализатора за проверку формата.
/// </para>
/// </summary>
public class DividerTests
{
    private const string Tiles = "WheelTalk.Dashboard.Droid/Screen/Tiles/";

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
    /// Свойства «начинать новую группу» нет ни в плитке, ни в кодеке, ни в меню правки — снято
    /// целиком, и вернуться молча не должно: оно уже было отвергнуто владельцем, и вторая попытка
    /// сделать зазор чертой по краю соседа обязана начинаться с разговора, а не с поля в DTO.
    /// <para>
    /// Круг «запись — чтение» старой раскладки с этим полем уехал в <c>TileLayoutCompatibilityTests</c>
    /// (ревизия 12.08.2026): он шёл через свой DTO и сериализатор, боевого кодека не касаясь, — там
    /// его родня и место. Здесь остались замки по исходникам.
    /// </para>
    /// </summary>
    [Fact]
    public void The_group_flag_has_not_crept_back()
    {
        Assert.DoesNotContain("GroupStart", RepoFiles.Read(Tiles + "MetricTile.cs"));
        Assert.DoesNotContain("TilesTileGroup", RepoFiles.Read(Tiles + "TileEditor.cs"));

        // Поле формата — по его объявлению, а не по слову «Group» на весь файл: пятибуквенная
        // подстрока однажды упадёт от невинного соседа вроде RadioGroup и будет сочтена ложной.
        Assert.DoesNotContain("Group { get; set; }", RepoFiles.Read(Tiles + "TileLayoutJson.cs"));
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

    /// <summary>
    /// Путь человека: «выбрал вид в меню → элемент появился в раскладке». Оба обрыва этого пути
    /// уже случались молча (11.08.2026): вид не был вписан в список пунктов меню, а
    /// <c>TileAdapter.Entry</c> отвергал плитку без величины, зная исключение только для пустой, —
    /// и «нажал ОК — ничего не произошло» доехало до владельца при зелёных замках ядра.
    /// </summary>
    [Fact]
    public void A_divider_reaches_the_layout_from_the_menu()
    {
        string editor = RepoFiles.Read(Tiles + "TileEditor.cs");
        Assert.Contains("translate(\"TilesKindDivider\")]", editor);
        Assert.Contains("if (kind == 5) return MetricTile.Divider()", editor);

        Assert.Contains("tile.Kind is TileKind.Empty or TileKind.Divider",
            RepoFiles.Read(Tiles + "TilesScreen.cs"));
    }
}
