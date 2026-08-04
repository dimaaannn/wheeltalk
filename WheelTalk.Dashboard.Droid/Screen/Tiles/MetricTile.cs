using System.Globalization;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Размер плитки в клетках сетки (план 23 §3.3): ширина в колонках, высота в строках. Двенадцать
/// колонок в ряду — НОК для одной, двух, трёх и четырёх плиток в ряд.
/// <para>
/// <b>Высота — своя мера, а не следствие ширины</b> (решение владельца 04.08.2026 отменено им же
/// 04.08.2026 вечером). Без неё не собрать ряд «половина плюс четыре четвертных в два столбика»: он
/// требует, чтобы рядом с двухстрочной плиткой стояли однострочные.
/// </para>
/// </summary>
public readonly record struct TileSize(int Columns, int Rows);

public static class TileSizes
{
    /// <summary>
    /// Подпись размера — долей ряда: «1/4», «1/2 × 2». Слов здесь нет намеренно: доля и множитель
    /// читаются на любом языке, и переводить их не приходится.
    /// </summary>
    public static string Describe(this TileSize size)
    {
        string width = size.Columns >= TilesLayout.Columns
            ? "1"
            : $"1/{TilesLayout.Columns / size.Columns}";

        return size.Rows == 1 ? width : $"{width} × {size.Rows.ToString(CultureInfo.InvariantCulture)}";
    }
}

/// <summary>
/// Как рисуется плитка. Величина — параметр рисовальщика, а не его вид: ровно то, ради чего заведён
/// <c>MetricCatalogue</c> (план 23 §7 — «тип плитки на каждую величину» запрещён).
/// </summary>
public enum TileKind
{
    /// <summary>Текущее число из живого снимка.</summary>
    Value,

    /// <summary>
    /// Место, оставленное пустым. Не рисует ничего и занимает клетки — этим и держит дырку в
    /// раскладке: укладчик идёт по списку вперёд и в обход занятого не возвращается, поэтому пустое
    /// место остаётся ровно там, где его поставили.
    /// </summary>
    Empty,

    // Chart (график из таблицы телеметрии) и Extremum (максимум/минимум с независимым сбросом)
    // приходят шагами 6 и 7 плана 23. Их здесь нет намеренно: рисовальщик без своих данных — это
    // пустая плитка, а не задел.
}

/// <summary>
/// Одна плитка раскладки: чем рисовать, что рисовать и какого размера. Размер задаётся при создании
/// плитки и переносом не меняется (решение владельца 04.08.2026): перетаскивание двигает плитку, а
/// не подгоняет её под место.
/// </summary>
/// <param name="MetricId">Имя величины из <c>MetricCatalogue</c>. У пустой плитки пусто.</param>
public sealed record MetricTile(string MetricId, TileKind Kind, TileSize Size)
{
    /// <summary>Пустое место заданного размера.</summary>
    public static MetricTile Empty(TileSize size) => new("", TileKind.Empty, size);
}
