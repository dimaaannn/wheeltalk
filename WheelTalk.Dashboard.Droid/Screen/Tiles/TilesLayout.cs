namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Раскладка плиток и все их размерные величины, зашитые в коде (план 23 §8, шаг 4). Править
/// раскладку руками — добавлять, убирать, переносить и менять ширину — райдер сможет шагом 5; тогда
/// же она переедет в настройку, а этот список останется тем, с чего начинается новая установка.
/// <para>
/// Набор подобран так, чтобы на экране были все три ширины и обе стороны правила про молчание:
/// наклон приходит только от Veteran, температура двигателя — только от Begode, и на любом колесе
/// одна из этих двух плиток стоит с прочерком.
/// </para>
/// <para>
/// <b>HOTRELOAD.</b> Это единственный файл, который открывают, чтобы подогнать вид плиток глазами:
/// величины отсюда читают и <see cref="TilesScreen"/>, и <see cref="MetricTileView"/>, своих чисел
/// у них нет. Почему свойства, а не константы, и как снять — в блоке ручек ниже.
/// </para>
/// </summary>
public static class TilesLayout
{
    // ---- HOTRELOAD: ручки вида ----------------------------------------------------------------
    //
    // Свойство с телом вместо константы — намеренно и временно. Visual Studio при горячей
    // перезагрузке подменяет тела методов, а константу компилятор впечатывает в место вызова: правка
    // `const` до телефона не доедет вовсе. Цена лишнего вызова здесь не считается — решение владельца
    // 04.08.2026: на подгонке вида перф не важен.
    //
    // Собранную иерархию View горячая перезагрузка не перестроит: числа читаются в конструкторе
    // плитки. Экран пересобирается кнопкой «♻» на стенде, командой
    // `am start -n com.wheeltalk.lab.droid/.LabActivity --es rebuild screen` либо сам — по событию
    // перезагрузки (LabHotReload).
    //
    // СНЯТЬ ПРАВКУ: найти по слову HOTRELOAD (этот файл, MetricTileView, LabActivity, LabHotReload) и
    // вернуть числа константами.

    /// <summary>Шесть — НОК для одной, двух и трёх плиток в ряд (план 23 §3.3).</summary>
    public static int Columns => 6;

    /// <summary>
    /// Строка сетки — <b>одна мера на всю сетку</b>, а не высота по содержимому: иначе ряд из узкой
    /// плитки и широкой разъехался бы, а высота плитки перестала бы зависеть только от её ширины.
    /// <c>GridLayoutManager</c> даёт лишь ширину, высоту ставит сама плитка.
    /// </summary>
    public static int RowHeightDp => 64;

    /// <summary>
    /// Просвет между плитками — по нему же считается высота двухстрочной: две строки плюс просвет,
    /// который был бы между двумя однострочными. Тогда широкая плитка встаёт вровень с парой узких.
    /// </summary>
    public static int GapDp => 3;

    /// <summary>Поля внутри плитки — от её края до подписи и числа.</summary>
    public static int PaddingDp => 9;

    /// <summary>Скругление подложки плитки.</summary>
    public static int CornerRadiusDp => 12;

    /// <summary>
    /// Насколько густа подложка плитки (0…255 от приглушённой краски палитры). Второго набора цветов
    /// не заводим: почти прозрачная <c>Dim</c> видна при любой палитре.
    /// </summary>
    public static int BackgroundAlpha => 28;

    /// <summary>Кегль подписи величины, sp.</summary>
    public static int LabelSp => 11;

    /// <summary>Кегль единицы измерения, sp.</summary>
    public static int UnitSp => 11;

    /// <summary>Просвет между числом и единицей.</summary>
    public static int UnitGapDp => 3;

    /// <summary>Отступ строки с числом от подписи.</summary>
    public static int ValueTopMarginDp => 2;

    /// <summary>
    /// Границы, в которых платформа сама подбирает кегль числа (API 26+, «Autosizing TextView»):
    /// число занимает плитку целиком, и в узкой однострочной оно того же вида, что в широкой
    /// двухстрочной, но другого размера.
    /// </summary>
    public static int ValueMinSp => 12;

    /// <inheritdoc cref="ValueMinSp"/>
    public static int ValueMaxSp => 64;

    /// <summary>Шаг подбора кегля, sp. Мельче шаг — точнее посадка числа и дороже замер.</summary>
    public static int ValueStepSp => 1;

    /// <summary>
    /// Сама раскладка. Свойство с телом по той же причине, что и величины выше: <c>static readonly</c>
    /// поле считается один раз при загрузке типа, и правка списка при горячей перезагрузке не
    /// доехала бы. Список собирается заново на каждую пересборку экрана — это раз в правку, а не
    /// раз в кадр.
    /// </summary>
    public static IReadOnlyList<MetricTile> Fixed =>
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
