namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Раскладка плиток и все их размерные величины, зашитые в коде (план 23 §8, шаг 4). Переносить
/// плитки и менять им ширину руками уже можно — правка ложится в <see cref="TileLayoutDraft"/>, а
/// этот список остаётся тем, с чего начинается новая установка. Добавление, убирание и хранение
/// раскладки в настройке — шаг 6.
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

    /// <summary>Двенадцать колонок в ряду: четверть — 3, половина — 6, весь ряд — 12 (план 23 §3.3).</summary>
    public static int Columns => 12;

    /// <summary>
    /// Каким укладчиком собирать сетку. <c>true</c> — свой <see cref="TileGridLayoutManager"/>: место
    /// ищется в клетках, и дырки заполняются следующими подходящими плитками.
    /// <para>
    /// <c>false</c> — <c>GridLayoutManager</c> из плана, и <b>заданной высоты он не даёт</b>: измерив
    /// ряд, он пере-меряет всех в нём по высоте самого высокого. Низкая плитка рядом с двухстрочной
    /// растягивается до неё — проверено глазами 04.08.2026 (мощность 1/4 встала высотой ШИМа).
    /// Оставлен для сверки, а не как выбор.
    /// </para>
    /// <para>
    /// HOTRELOAD: переключается на ходу, но экран после правки надо пересобрать — укладчик ставится
    /// при сборке списка.
    /// </para>
    /// </summary>
    public static bool PackTiles => true;

    /// <summary>
    /// Строка сетки — <b>одна мера на всю сетку</b>, а не высота по содержимому: иначе ряд из низкой
    /// плитки и высокой разъехался бы, а размер плитки перестал бы быть тем, что задали руками.
    /// </summary>
    public static int RowHeightDp => 68;

    /// <summary>
    /// Просвет между плитками — по нему же считается высота многострочной: строки плюс просветы,
    /// которые были бы между однострочными. Тогда высокая плитка встаёт вровень со столбиком низких.
    /// </summary>
    public static int GapDp => 3;

    /// <summary>Поля внутри плитки — от её края до подписи и числа.</summary>
    public static int PaddingDp => 8;

    /// <summary>Скругление подложки плитки.</summary>
    public static int CornerRadiusDp => 12;

    /// <summary>
    /// Насколько густа подложка плитки (0…255 от приглушённой краски палитры). Второго набора цветов
    /// не заводим: почти прозрачная <c>Dim</c> видна при любой палитре.
    /// </summary>
    public static int BackgroundAlpha => 40;

    /// <summary>Кегль подписи величины, sp.</summary>
    public static int LabelSp => 11;

    /// <summary>
    /// Кегль единицы — долей от кегля числа, а не своим размером: число тянется автоподбором, и
    /// единица при своём размере то тонула бы в нём, то спорила с ним.
    /// </summary>
    public static float UnitScale => 0.45f;

    /// <summary>Отступ строки с числом от подписи.</summary>
    public static int ValueTopMarginDp => 2;

    /// <summary>
    /// Границы, в которых платформа сама подбирает кегль числа (API 26+, «Autosizing TextView»):
    /// число занимает плитку целиком, и в узкой однострочной оно того же вида, что в широкой
    /// двухстрочной, но другого размера.
    /// </summary>
    public static int ValueMinSp => 12;

    /// <inheritdoc cref="ValueMinSp"/>
    public static int ValueMaxSp => 48;

    /// <summary>Шаг подбора кегля, sp. Мельче шаг — точнее посадка числа и дороже замер.</summary>
    public static int ValueStepSp => 1;

    /// <summary>Сторона уголка-ручки, видного только в режиме правки.</summary>
    public static int HandleSizeDp => 14;

    /// <summary>
    /// Сторона зоны касания уголка. Шире рисунка намеренно: попадание пальцем меряется не тем, что
    /// видно, а тем, куда он ложится.
    /// </summary>
    public static int HandleTouchDp => 28;

    /// <summary>Густота уголка-ручки (0…255 от основной краски).</summary>
    public static int HandleAlpha => 110;

    /// <summary>Толщина контура, которым в режиме правки обведено пустое место.</summary>
    public static int OutlineDp => 1;

    /// <summary>Толщина линии графика.</summary>
    public static int ChartStrokeDp => 2;

    /// <summary>
    /// Густота заливки под линией (0…255 от основной краски). Ноль — заливки нет, одна линия.
    /// </summary>
    public static int ChartFillAlpha => 72;


    /// <summary>
    /// Потолок кегля числа, лежащего поверх графика: у плитки значения число — главное, здесь —
    /// подпись к линии, и заслонять её собой оно не должно.
    /// </summary>
    public static int ChartValueMaxSp => 34;

    /// <summary>
    /// Как часто перечитывается история, мс. Раз в секунду-две, а не на кадр (план 23 §5.6): запрос
    /// к базе идёт вне потока отрисовки, но и лишним он быть не должен — за секунду приходит пять
    /// отсчётов, а на плитке шириной в палец это меньше пикселя.
    /// </summary>
    public static int ChartPollMs => 1500;

    /// <summary>Кегль подписей шкалы на плитке-графике, sp.</summary>
    public static float ChartAxisSp => 8f;

    /// <summary>Сколько делений на шкале плитки. Меньше — чище, но по двум крайним трудно судить о середине.</summary>
    public static int ChartAxisLabels => 3;

    /// <summary>Кегль подписи «за какое время», sp.</summary>
    public static int ChartRangeSp => 8;

    /// <summary>Кегль подписи величины в полноэкранном просмотре, sp.</summary>
    public static int ViewerTitleSp => 20;

    /// <summary>Кегль строки с выбранной точкой, sp.</summary>
    public static int ViewerPickedSp => 14;

    /// <summary>Поля полноэкранного просмотра.</summary>
    public static int ViewerPaddingDp => 12;

    /// <summary>
    /// Сколько точек просить у истории для полноэкранного графика. Больше ширины экрана в пикселях
    /// смысла не имеет: зум библиотека делает по уже прочитанным точкам.
    /// </summary>
    public static int ViewerPoints => 1500;

    /// <summary>
    /// Окна графика, которые предлагает меню плитки. Порядок здесь и есть порядок в списке выбора;
    /// потолок — суточный срок хранения телеметрии (решение владельца 03.08.2026).
    /// </summary>
    public static IReadOnlyList<TimeSpan> ChartWindows =>
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(24),
    ];

    /// <summary>Кегль надписи на кнопках режима правки, sp.</summary>
    public static int ButtonSp => 15;

    /// <summary>Поля внутри кнопки режима правки — от края до надписи.</summary>
    public static int ButtonPaddingDp => 12;

    /// <summary>Просвет между кнопками и от края экрана.</summary>
    public static int ButtonGapDp => 6;

    /// <summary>
    /// Размеры, которые предлагает меню плитки. Порядок здесь и есть порядок в списке выбора;
    /// строка ряда — двенадцать колонок, поэтому 3 — четверть, 6 — половина, 12 — весь ряд.
    /// <para>
    /// <b>Трети нет</b> (владелец 04.08.2026): она плохо стыкуется с половиной и четвертью — в ряду
    /// с ними всегда остаётся обрезок, который нечем закрыть. Двенадцать колонок оставлены как
    /// были: доли считаются от них, и вернуть треть — одна строка.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TileSize> Sizes =>
    [
        new(3, 1),
        new(6, 1),
        new(6, 2),
        new(12, 2),
        new(12, 3),
    ];

    /// <summary>
    /// Сама раскладка. Свойство с телом по той же причине, что и величины выше: <c>static readonly</c>
    /// поле считается один раз при загрузке типа, и правка списка при горячей перезагрузке не
    /// доехала бы. Список собирается заново на каждую пересборку экрана — это раз в правку, а не
    /// раз в кадр.
    /// </summary>
    public static IReadOnlyList<MetricTile> Fixed =>
    [
        new("speed", TileKind.Value, new(12, 2)),

        new("pwm", TileKind.Value, new(6, 2)),
        new("battery_level", TileKind.Value, new(6, 2)),

        // Половина и четыре четвертных двумя столбиками — ряд, ради которого высота стала своей
        // мерой. Укладчик кладёт их сам: список идёт слева направо и сверху вниз, а четвертные
        // ложатся в остаток ряда рядом с двухстрочным напряжением.
        new("voltage", TileKind.Value, new(6, 2)),
        new("current", TileKind.Value, new(3, 1)),
        new("power", TileKind.Value, new(3, 1)),
        new("phase_current", TileKind.Value, new(3, 1)),
        new("max_pwm", TileKind.Value, new(3, 1)),

        new("system_temp", TileKind.Value, new(3, 1)),
        new("temp2", TileKind.Value, new(3, 1)),
        new("tilt", TileKind.Value, new(3, 1)),
        new("top_speed", TileKind.Value, new(3, 1)),

        new("distance", TileKind.Value, new(6, 1)),
        new("totaldistance", TileKind.Value, new(6, 1)),

        // График ШИМ с числом поверх — то, ради чего экран и задуман: пик видно на линии, а текущее
        // значение читается не отходя.
        new("pwm", TileKind.Chart, new(12, 2), ShowLabel: true,
            new TileChart(TimeSpan.FromMinutes(15), ShowValue: true, Zoom: false)),
    ];
}
