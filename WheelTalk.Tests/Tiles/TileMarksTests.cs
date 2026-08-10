using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Метки плитки независимы (решение владельца 11.08.2026): ставится любая — только жёлтая, только
/// красная, обе или ни одной. Прежде одинокая молча пропадала на трёх рубежах сразу — в меню, в
/// формате раскладки и в расчёте жара, — и сохранить её было нельзя.
/// <para>
/// Проверяется по исходникам: разбор и показ живут в android-библиотеке, а правило простое и
/// ломается тихо — вернувшееся «нужны обе» никак себя не проявит, кроме исчезнувшей настройки.
/// </para>
/// </summary>
public class TileMarksTests
{
    private const string Tiles = "WheelTalk.Dashboard.Droid/Screen/Tiles/";

    /// <summary>Обе метки — необязательные числа: у нуля свой смысл, «не предупреждать».</summary>
    [Fact]
    public void Both_marks_may_be_absent_on_their_own()
    {
        Assert.Contains(
            "public readonly record struct TileLimits(double? Warn, double? Danger, bool Rising)",
            RepoFiles.Read(Tiles + "MetricTile.cs"));

        string json = RepoFiles.Read(Tiles + "TileLayoutJson.cs");
        Assert.Contains("public double? Warn { get; set; }", json);
        Assert.Contains("public double? Danger { get; set; }", json);
    }

    /// <summary>
    /// Меню сохраняет одинокую метку: правило «нужны обе» (<c>is { } low && is { } high</c>) и было
    /// тем местом, где вторая половина настройки пропадала.
    /// </summary>
    [Fact]
    public void The_menu_keeps_a_lonely_mark()
    {
        string limits = RepoFiles.MethodBody(
            RepoFiles.Read(Tiles + "TileEditor.cs"),
            "private static TileLimits? Limits(EditText warn, EditText danger, bool falling)");

        // Обе метки читаются порознь, и пустой считается только пара: одинокая доживает до плитки.
        Assert.Contains("low is null && high is null ? null", limits);
    }

    /// <summary>
    /// Одинокая метка доживает до показа: кодек не требует пары, а жар считает шкалу от нуля до
    /// уставки. Пусты обе — метки берутся из настроек тревог, как и раньше.
    /// </summary>
    [Fact]
    public void A_lonely_mark_survives_the_format_and_the_heat()
    {
        string json = RepoFiles.MethodBody(
            RepoFiles.Read(Tiles + "TileLayoutJson.cs"), "private static TileLimits? ToLimits(LimitsDto? dto)");

        Assert.Contains("warn is null && danger is null ? null", json);

        string heat = RepoFiles.MethodBody(
            RepoFiles.Read(Tiles + "MetricHeat.cs"), "private static double Heat(double value, TileLimits limits)");

        Assert.Contains("Alone(", heat);
    }

    /// <summary>
    /// Одинокая жёлтая не краснеет оттого, что красной рядом не поставили: краску выбирает тот, чья
    /// метка стоит. Это и есть «подкрас по достижении уставки» — полная краска метки на её конце
    /// шкалы, а не чужая.
    /// </summary>
    [Fact]
    public void A_lonely_mark_burns_with_its_own_colour()
    {
        string heat = RepoFiles.Read(Tiles + "MetricHeat.cs");

        Assert.Contains("public static Color Tint(double heat, DashboardPalette palette, TileLimits? limits)", heat);
        Assert.Contains("{ Danger: not null } => Mix(palette.Dim, palette.Danger", heat);
    }

    /// <summary>
    /// Пустое место не показывает чужого слова. Подпись квадрата живёт не в разметке, а меткой в
    /// углу — её рисует сама плитка, — и сетка переиспользует вью: не почисти её здесь, и пустая
    /// плитка встанет с чужим именем («Мотор» на пустоте, владелец 11.08.2026).
    /// </summary>
    [Fact]
    public void An_empty_tile_forgets_the_word_of_the_one_before_it()
    {
        string empty = RepoFiles.MethodBody(
            RepoFiles.Read(Tiles + "TileView.cs"), "public void BindEmpty(TileSize size)");

        Assert.Contains("Label.Text = \"\";", empty);
        Assert.Contains("_cornerLabel = \"\";", empty);
    }

    /// <summary>
    /// Пометка ▲▼ стоит <b>перед</b> подписью и крупнее её: глаз читает начало строки, а не её
    /// конец, и крайняя плитка обязана узнаваться с одного взгляда.
    /// </summary>
    [Fact]
    public void The_mark_leads_the_label_and_is_bigger()
    {
        Assert.Contains(
            "MarkLabel(options.Lowest ? \"▼\" : \"▲\", label)",
            RepoFiles.Read(Tiles + "ExtremumTileView.cs"));

        string mark = RepoFiles.MethodBody(
            RepoFiles.Read(Tiles + "TileView.cs"), "protected void MarkLabel(string mark, string label)");

        Assert.Contains("$\"{mark} {label}\"", mark);
        Assert.Contains("RelativeSizeSpan(TilesLayout.MarkScale)", mark);
    }
}
