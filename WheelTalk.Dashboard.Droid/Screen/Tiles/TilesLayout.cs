namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Раскладка плиток, зашитая в коде (план 23 §8, шаг 4). Править её руками — добавлять, убирать,
/// переносить и менять ширину — райдер сможет шагом 5; тогда же она переедет в настройку, а этот
/// список останется тем, с чего начинается новая установка.
/// <para>
/// Набор подобран так, чтобы на экране были все три ширины и обе стороны правила про молчание:
/// наклон приходит только от Veteran, температура двигателя — только от Begode, и на любом колесе
/// одна из этих двух плиток стоит с прочерком.
/// </para>
/// </summary>
public static class TilesLayout
{
    /// <summary>Шесть — НОК для одной, двух и трёх плиток в ряд (план 23 §3.3).</summary>
    public const int Columns = 6;

    /// <summary>
    /// Строка сетки — <b>одна мера на всю сетку</b>, а не высота по содержимому: иначе ряд из узкой
    /// плитки и широкой разъехался бы, а высота плитки перестала бы зависеть только от её ширины.
    /// <c>GridLayoutManager</c> даёт лишь ширину, высоту ставит сама плитка.
    /// </summary>
    public const int RowHeightDp = 64;

    /// <summary>
    /// Просвет между плитками — по нему же считается высота двухстрочной: две строки плюс просвет,
    /// который был бы между двумя однострочными. Тогда широкая плитка встаёт вровень с парой узких.
    /// </summary>
    public const int GapDp = 3;

    public static readonly IReadOnlyList<MetricTile> Fixed =
    [
        new("speed", TileKind.Value, TileWidth.Full),

        new("pwm", TileKind.Value, TileWidth.Half),
        new("battery_level", TileKind.Value, TileWidth.Half),

        new("voltage", TileKind.Value, TileWidth.Third),
        new("current", TileKind.Value, TileWidth.Third),
        new("power", TileKind.Value, TileWidth.Third),

        new("system_temp", TileKind.Value, TileWidth.Third),
        new("temp2", TileKind.Value, TileWidth.Third),
        new("phase_current", TileKind.Value, TileWidth.Third),

        new("distance", TileKind.Value, TileWidth.Half),
        new("totaldistance", TileKind.Value, TileWidth.Half),

        new("max_pwm", TileKind.Value, TileWidth.Third),
        new("top_speed", TileKind.Value, TileWidth.Third),
        new("tilt", TileKind.Value, TileWidth.Third),
    ];
}
