using System.Text.RegularExpressions;

namespace WheelTalk.Tests.TestSupport;

/// <summary>Одно место раскладки: величина, вид и размер. Разделитель — без величины.</summary>
public readonly record struct LayoutSpot(string Metric, string Kind, int Columns, int Rows)
{
    public bool IsDivider => Kind == "Divider";

    public override string ToString() =>
        IsDivider ? "разделитель" : $"{Metric} {Kind} {Columns}×{Rows}";
}

/// <summary>
/// Раскладка, с которой приложение стартует, — <c>TilesLayout.Fixed</c>, прочитанная из исходника.
/// <para>
/// <b>Зачем разбор текста.</b> Раскладка живёт в android-библиотеке, тестам не видной, а держать
/// нужно именно её. Копия списка в тесте уже дважды уезжала от боевой молча (замер 12.08.2026:
/// <c>TileFitsTheGridTests</c> и <c>TileTypographyCostTests</c> стерегли состав, снятый решением
/// владельца 11.08), и оба раза это не уронило ни одного теста — оттого разбор один и общий.
/// </para>
/// </summary>
public static class OwnerLayout
{
    private const string Source = "WheelTalk.Dashboard.Droid/Screen/Tiles/TilesLayout.cs";

    /// <summary>Состав по порядку — вместе с разделителем: его место в ряду тоже часть раскладки.</summary>
    public static IReadOnlyList<LayoutSpot> Spots() => Regex
        .Matches(Fixed(), @"new\(""(\w+)"", TileKind\.(\w+), new\((\d+), (\d+)\)|MetricTile\.Divider\(\)")
        .Select(match => match.Groups[1].Success
            ? new LayoutSpot(match.Groups[1].Value, match.Groups[2].Value,
                int.Parse(match.Groups[3].Value), int.Parse(match.Groups[4].Value))
            : new LayoutSpot("", "Divider", 12, 1))
        .ToList();

    /// <summary>Только плитки с числом: разделителю подбирать нечего, у него нет содержимого.</summary>
    public static IReadOnlyList<LayoutSpot> Tiles() =>
        Spots().Where(spot => !spot.IsDivider).ToList();

    /// <summary>Тело списка <c>Fixed</c> — для замков, которым нужен сам текст (пороги, признаки).</summary>
    public static string Fixed() =>
        RepoFiles.MethodBody(RepoFiles.Read(Source), "public static IReadOnlyList<MetricTile> Fixed =>");
}
