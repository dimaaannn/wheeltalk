using System.Xml.Linq;
using WheelTalk.Core.Dashboard;
using WheelTalk.Core.Tiles;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Dashboard;

/// <summary>
/// Справочный блок центра главного экрана: состав собирает человек, кегль подбирается под число
/// строк и место (решение владельца 12.08.2026 — «взять подход табличек; два элемента — большие,
/// пять — меньше»).
/// <para>
/// Проверяется то, что можно проверить без телефона: арифметика подбора, пол читаемости, круг
/// «запись — чтение» состава и умолчание. Само рисование — канва, и его меряет глаз на стенде.
/// </para>
/// </summary>
public class CentrePanelTests
{
    /// <summary>Центр эталонного экрана: 152 dp высоты блока при плотности 2 — те самые ~304 px.</summary>
    private const float Room = 304;

    private const float Width = 500;

    /// <summary>
    /// Пол читаемости ISO 15008 при плотности 2: 12 угловых минут на вытянутой руке — 16 dp, то
    /// есть 32 px. Тем же числом живёт панель (<c>SpeedBlockDrawable.FloorDp</c>).
    /// </summary>
    private const float Floor = 32;

    private const float Ceiling = 96;

    /// <summary>Линейка вроде шрифта: знак обычного начертания — половина кегля.</summary>
    private sealed class Ruler : ITextRuler
    {
        public float Width(string text, float sizeSp, bool mono) => text.Length * sizeSp * 0.5f;

        public float Height(float sizeSp) => sizeSp * 1.25f;
    }

    private static CenterMetrics Metrics() => new(FloorPx: Floor, CeilingPx: Ceiling);

    /// <summary>
    /// Главное обещание владельца: <b>меньше строк — крупнее строка</b>. Не «примерно», а строго:
    /// два элемента обязаны быть крупнее пяти, иначе автомасштаба нет вовсе.
    /// </summary>
    [Fact]
    public void Fewer_rows_are_drawn_bigger()
    {
        var two = CenterTypography.Fit("888.8", "ШИМ % ▲", 2, Room, Width, new Ruler(), Metrics());
        var four = CenterTypography.Fit("888.8", "ШИМ % ▲", 4, Room, Width, new Ruler(), Metrics());
        var six = CenterTypography.Fit("888.8", "ШИМ % ▲", 6, Room, Width, new Ruler(), Metrics());

        Assert.True(two.FontPx > four.FontPx, $"два элемента {two.FontPx}, четыре {four.FontPx}");
        Assert.True(four.FontPx > six.FontPx, $"четыре элемента {four.FontPx}, шесть {six.FontPx}");
    }

    /// <summary>
    /// Пол читаемости не уступает месту: не влезло — показывается <b>меньше строк</b>, а не мельче.
    /// Нечитаемая строка не показание, а помеха; ISO 15008 требует не мельче 12 угловых минут, и
    /// прежние справочные (11 pt ≈ 6′) этой нормы не держали вовсе.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void The_floor_of_readability_is_never_broken(int rows)
    {
        var fit = CenterTypography.Fit("888.8", "ШИМ % ▲", rows, Room, Width, new Ruler(), Metrics());

        Assert.True(fit.FontPx >= Floor, $"{rows} строк набраны {fit.FontPx} px — ниже пола {Floor}");
        Assert.InRange(fit.Rows, 0, rows);
    }

    /// <summary>
    /// Тесное место — часть строк снимается, и снимаются <b>последние</b>: порядок собран человеком,
    /// и первое в нём для него важнее. Прежняя панель делала то же самое («четыре или две»), только
    /// выбор за райдера делали мы.
    /// </summary>
    [Fact]
    public void A_tight_centre_shows_fewer_rows_rather_than_smaller_ones()
    {
        // Места ровно на две строки по полу читаемости: 2 · 32 · 1,75 = 112 px.
        var fit = CenterTypography.Fit("888.8", "ШИМ % ▲", 6, 120, Width, new Ruler(), Metrics());

        Assert.Equal(2, fit.Rows);
        Assert.True(fit.FontPx >= Floor);
    }

    /// <summary>Места нет вовсе — не показываем ничего: одна нечитаемая строка хуже пустоты.</summary>
    [Fact]
    public void No_room_means_no_rows()
    {
        Assert.Equal(0, CenterTypography.Fit("888.8", "ШИМ % ▲", 4, 40, Width, new Ruler(), Metrics()).Rows);
    }

    /// <summary>Узкая полоса ужимает кегль шириной, а не только высотой: строка не вправе вылезти вбок.</summary>
    [Fact]
    public void A_narrow_centre_shrinks_the_line_too()
    {
        var wide = CenterTypography.Fit("888.8", "ШИМ % ▲", 2, Room, 500, new Ruler(), Metrics());
        var narrow = CenterTypography.Fit("888.8", "ШИМ % ▲", 2, Room, 200, new Ruler(), Metrics());

        Assert.True(narrow.FontPx < wide.FontPx);
    }

    /// <summary>
    /// Умолчание — <b>те же четыре смысла</b>, что стояли в центре жёстко: макс ШИМ, температура
    /// «сейчас и максимум», пробег поездки и «заряд с минимумом напряжения». Совместимость глаза:
    /// человек смотрит сюда каждый выезд, и свежая установка не должна показывать ему другой набор.
    /// <para>
    /// Подписи с 12.08.2026 иные (см. <see cref="The_captions_are_signs_and_marks_rather_than_words"/>),
    /// а смыслы те же: состав не трогали, трогали слова.
    /// </para>
    /// </summary>
    [Fact]
    public void The_default_is_the_four_meanings_that_stood_there_before()
    {
        var rows = CenterLayout.Default;

        Assert.Equal(4, rows.Count);
        Assert.Equal(new CenterReading("pwm", CenterAspect.Max), rows[0].First);
        Assert.Equal(new CenterReading("system_temp", CenterAspect.Current), rows[1].First);
        Assert.Equal(new CenterReading("system_temp", CenterAspect.Max), rows[1].Second);
        Assert.Equal(new CenterReading("distance", CenterAspect.Current), rows[2].First);
        Assert.Equal(new CenterReading("battery_level", CenterAspect.Current), rows[3].First);
        Assert.Equal(new CenterReading("voltage", CenterAspect.Min), rows[3].Second);
    }

    /// <summary>Состав переживает круг «запись — чтение» целиком: и порядок, и стороны, и пары.</summary>
    [Fact]
    public void A_layout_survives_the_round_trip()
    {
        var saved = CenterLayoutJson.Read(CenterLayoutJson.Write(CenterLayout.Default));

        Assert.NotNull(saved);
        Assert.Equal(CenterLayout.Default, saved);
    }

    /// <summary>
    /// Битая строка не стоит всего состава: негодная запись выбрасывается, негодный файл читается
    /// как «состава нет» — и человек видит умолчание, а не пустой центр.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("не json вовсе")]
    public void A_broken_record_reads_as_no_layout_at_all(string? json)
    {
        Assert.Null(CenterLayoutJson.Read(json));
        Assert.Equal(CenterLayout.Default, CenterLayout.Sane(CenterLayoutJson.Read(json)));
    }

    /// <summary>Незнакомая сторона — не потеря строки: читается как «текущее», это её умолчание.</summary>
    [Fact]
    public void An_unknown_side_reads_as_the_current_one()
    {
        var rows = CenterLayoutJson.Read("""[{"metric":"pwm","aspect":"позавчерашнее"}]""");

        Assert.NotNull(rows);
        Assert.Equal(new CenterReading("pwm", CenterAspect.Current), rows[0].First);
    }

    /// <summary>
    /// Потолок состава — шесть строк, и он держится на чтении тоже: файл с чужого телефона (или из
    /// сборки, где потолок был выше) не заставит панель считать невлезающее.
    /// </summary>
    [Fact]
    public void More_rows_than_the_ceiling_are_cut_on_reading()
    {
        string many = CenterLayoutJson.Write(Enumerable.Repeat(new CenterRow("pwm"), 9));

        Assert.Equal(CenterLayout.MaxRows, CenterLayoutJson.Read(many)!.Count);
        Assert.Equal(CenterLayout.MaxRows, CenterLayout.Sane(Enumerable.Repeat(new CenterRow("pwm"), 9)).Count);
    }

    /// <summary>
    /// Правка — <b>окном с хозяином</b>, а не перестройкой панели: правило панели старше этой задачи
    /// (прогон 3) — индикаторы независимы, всё рисуется канвой, разметка после сборки не трогается.
    /// Отсюда и редактор отдельным окном, и то, что открывает его активность, у которой есть
    /// <c>OwnedWindow</c>; «у окна есть хозяин» стережёт <c>Architecture/WindowOwnershipTests</c>.
    /// </summary>
    [Fact]
    public void The_editor_is_a_window_handed_to_an_owner()
    {
        string editor = RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/CentreEditor.cs");

        Assert.Contains("public static Dialog Show(", editor);

        string activity = RepoFiles.Read("WheelTalk.Droid/Main/MainActivity.cs");

        Assert.Contains("_windows.Own(CentreEditor.Show(", activity);

        // Ни одного вида разметки внутри самой панели: редактор живёт в окне, а панель — канва.
        foreach (string panel in
                 (string[])["WheelTalk.Dashboard.Droid/Layouts/TwinTapesDashboard.cs",
                     "WheelTalk.Dashboard.Droid/Widgets/SpeedBlockDrawable.cs"])
        {
            string source = RepoFiles.Read(panel);

            Assert.DoesNotContain("AddView(", source);
            Assert.DoesNotContain("new TextView(", source);
        }
    }

    /// <summary>
    /// Долгий тап не спорит с жестами панели: коротким тапом ловятся плашка связи, точка записи и
    /// галочка шторки (<c>OnSingleTapConfirmed</c> — подтверждённый), свайпом от нижней кромки —
    /// шторка, а на долгом тапе до этой задачи не висело ничего.
    /// </summary>
    [Fact]
    public void The_long_press_does_not_fight_the_gestures_the_panel_already_had()
    {
        string activity = RepoFiles.Read("WheelTalk.Droid/Main/MainActivity.cs");

        // Слушатель общий с библиотекой — своей копии у приложения нет (её и не было бы смысла
        // держать: стенд ловит тот же жест тем же классом).
        Assert.Contains("new SingleTapListener(OnTapped, OnLongPressed)", activity);
        Assert.Contains("_screen.Current.LongPress(x, y)", activity);
        Assert.Contains(
            "public override void OnLongPress(MotionEvent? e)",
            RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/SingleTapListener.cs"));

        // Жест уходит экрану, а панель отвечает намерением — координаты хозяину знать нечего.
        string view = RepoFiles.Read("WheelTalk.Dashboard.Droid/DashboardView.cs");

        Assert.Contains("if (CentreExtras.Contains(x, y)) OnIntent?.Invoke(MainScreenIntent.EditCentre);", view);

        // Зона правки — та же доля высоты, которой блок рисуется: два числа разъехались бы при
        // первой подгонке вида.
        Assert.Contains(
            "content.Height() * SpeedBlockDrawable.ExtrasAt",
            RepoFiles.Read("WheelTalk.Dashboard.Droid/Layouts/TwinTapesDashboard.cs"));
    }

    /// <summary>
    /// <b>Строка редактора не сжимается до буквы.</b> Подпись берёт всё, что осталось от кнопок
    /// (ширина 0 + вес 1), а ребёнок списка идёт во всю ширину окна — вес делит лишь то, что есть, и
    /// при <c>WrapContent</c> у строки делить нечего. Ровно это владелец и увидел на телефоне
    /// 12.08.2026: «ШИМ макс» встало столбиком по букве во всю высоту окна.
    /// </summary>
    [Fact]
    public void A_row_of_the_editor_gives_its_width_to_the_caption()
    {
        string editor = RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/CentreEditor.cs");

        // Ребёнок списка — во всю ширину: без этого вес внутри строки нечего делить.
        Assert.Contains(
            "new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)",
            editor);
        Assert.Contains("private static void Add(LinearLayout list, View child)", editor);

        // Подпись — весом, кнопки — по себе; в пикселях здесь не считает никто, кроме цели касания.
        Assert.Contains(
            "row.AddView(label, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));",
            editor);
        Assert.Contains("SetMinimumWidth(context.Dp(44))", editor);

        // Длинная подпись переносится, а не режет кнопки и не растит строку без края.
        Assert.Contains("label.SetMaxLines(2);", editor);
    }

    /// <summary>
    /// <b>Стенд равен боевому и поведением</b> (память владельца 10.08.2026), а не только видом:
    /// намерение правки центра обработано в обоих, и оба открывают <b>один и тот же</b> редактор со
    /// своим хранилищем и своими словами. Стенд, который не умеет того, что умеет приложение,
    /// проверять на нём нечего.
    /// </summary>
    [Fact]
    public void Both_the_app_and_the_stand_answer_the_edit_intent()
    {
        foreach (string owner in
                 (string[])["WheelTalk.Droid/Main/MainActivity.cs", "WheelTalk.Lab.Droid/LabActivity.cs"])
        {
            string source = RepoFiles.Read(owner);

            Assert.Contains("case MainScreenIntent.EditCentre:", source);
            Assert.Contains("CentreEditor.Show(", source);
            Assert.Contains("SingleTapListener(OnTapped, OnLongPressed)", source);
        }

        // Хранилище у стенда своё — файлом, слоёв настроек у него нет, — но интерфейс тот же.
        string stand = RepoFiles.Read("WheelTalk.Lab.Droid/LabCentreLayoutFile.cs");

        Assert.Contains(": ICentreLayoutStore", stand);
        Assert.Contains("CenterLayoutJson.Read", stand);
        Assert.Contains("CenterLayoutJson.Write", stand);

        // Долгий тап — одним слушателем на обоих: две копии разошлись бы порогом удержания.
        Assert.Contains(
            "public override void OnLongPress(MotionEvent? e)",
            RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/SingleTapListener.cs"));
    }

    /// <summary>
    /// <b>В кадре — только краска</b> (урок плана 31, дословно). Подбор кегля меряет строку шрифтом,
    /// а всякий такой замер — JNI: шестьдесят раз в секунду он стоит дороже самой отрисовки, и
    /// владелец это увидел подтормаживанием при открытом редакторе (12.08.2026), когда панель
    /// продолжает рисоваться за окном.
    /// </summary>
    [Fact]
    public void The_drawing_of_the_centre_measures_nothing_and_allocates_nothing()
    {
        string block = RepoFiles.Read("WheelTalk.Dashboard.Droid/Widgets/SpeedBlockDrawable.cs");
        string drawn = RepoFiles.MethodBody(
            block, "private void DrawExtras(Canvas canvas, RectF rect, DashboardPalette palette)");

        // Ни подбора, ни замера шрифтом, ни сборки слов — всё это выше по течению.
        Assert.DoesNotContain("CenterTypography.Fit", drawn);
        Assert.DoesNotContain("MeasureText", drawn);
        Assert.DoesNotContain("CenterReadings.Worst", drawn);
        Assert.DoesNotContain("CenterReadings.Caption", drawn);

        // Ни мусора: ни LINQ, ни склейки списка — они заводят перечислитель и промежуточный массив
        // на каждый кадр.
        Assert.DoesNotContain("string.Join", drawn);
        Assert.DoesNotContain(".Select(", drawn);
        Assert.DoesNotContain("new ", drawn);

        // Кегль скорости тоже снят с кадра: MeasureText — тот же JNI.
        Assert.Contains("if (_speedShown == text && _speedFor.Equals(rect)) return _speedFont;", block);
    }

    /// <summary>
    /// Посадка пересчитывается <b>по сигнатуре входов</b>, а не по догадке «наверное, не менялось»:
    /// состав строк, прямоугольник, плотность и показ десятых. Числами и ссылкой, а не собранной
    /// строкой-ключом — иначе мусор вернулся бы той же дверью, из которой его выгнали (урок якоря).
    /// </summary>
    [Fact]
    public void The_placement_is_kept_until_its_inputs_change()
    {
        string placed = RepoFiles.MethodBody(
            RepoFiles.Read("WheelTalk.Dashboard.Droid/Widgets/SpeedBlockDrawable.cs"),
            "private void Place(RectF rect, IReadOnlyList<CenterRow> rows, bool tenths)");

        Assert.Contains("ReferenceEquals(_placedRows, rows)", placed);
        Assert.Contains("_placedFor.Equals(rect)", placed);
        Assert.Contains("_placedTenths == tenths", placed);
        Assert.Contains("Math.Abs(_placedDensity - Density)", placed);

        // Слова показаний — со снимком, а не с кадром: сравниваются числа, а не собранные строки.
        string told = RepoFiles.MethodBody(
            RepoFiles.Read("WheelTalk.Dashboard.Droid/Widgets/SpeedBlockDrawable.cs"),
            "private void Retell(IReadOnlyList<CenterRow> rows, bool tenths)");

        Assert.Contains("Same(_numbers[index * 2], first)", told);
        Assert.DoesNotContain("string.Join", told);
        Assert.DoesNotContain(".Select(", told);
    }

    /// <summary>
    /// Цена одного пересчёта — в обращениях к шрифту, и она постоянная: два замера, по одному на
    /// худшее значение и худшую подпись, а не перебор кеглей, как у плиток. Вырастет — значит кто-то
    /// вернул сюда цикл, и цена станет зависеть от числа строк.
    /// </summary>
    [Fact]
    public void One_fitting_asks_the_font_twice_no_matter_how_many_rows()
    {
        var ruler = new CountingRuler();

        CenterTypography.Fit("888 / 888.8", "Заряд % / V ▼", 6, Room, Width, ruler, Metrics());

        Assert.Equal(2, ruler.Asked);
    }

    private sealed class CountingRuler : ITextRuler
    {
        public int Asked { get; private set; }

        public float Width(string text, float sizeSp, bool mono)
        {
            Asked++;

            return text.Length * sizeSp * 0.5f;
        }

        public float Height(float sizeSp) => sizeSp * 1.25f;
    }

    /// <summary>
    /// Состав хранится общим слоем: центр — лицо приложения, человек собирает его под свою манеру
    /// ездить, а не под колесо. Молчащая величина рисует прочерк и состава не ломает — тот же довод,
    /// каким общей сделана раскладка плиток.
    /// </summary>
    [Fact]
    public void The_layout_is_kept_for_the_app_and_not_for_a_wheel()
    {
        string store = RepoFiles.Read("WheelTalk.Droid/Main/CentreLayoutSetting.cs");

        Assert.Contains("SettingLayer.GlobalOnly", store);
        Assert.Contains("\"Centre:Layout\"", store);
    }

    /// <summary>
    /// <b>Подпись — знак величины и знак стороны, а не слова</b> (решение владельца 12.08.2026:
    /// «оптимизировать текст, оставив минимально понятным; использовать уже принятые знаки макс и
    /// мин; общепринятые обозначения величин»). Четыре строки умолчания читаются теперь так:
    /// <c>ШИМ % ▲</c>, <c>t° / ▲</c>, <c>Пробег</c>, <c>Заряд % / V ▼</c>; новая величина —
    /// <c>Поездка, км</c>.
    /// <para>
    /// Единица переехала в саму подпись потому, что центр её не рисует вовсе, — отсюда и отдельный
    /// ключ знака (<c>…Sign</c>) рядом с коротким именем (<c>…Short</c>): короткое имя стоит на
    /// четвертной плитке, где единица нарисована рядом с числом, и «V» превратило бы её в «V 78,4 В».
    /// Проценты у ШИМ и заряда, «км/ч» у скорости, «км» у счётчика — та же единица в подписи, только
    /// словом, где знака у величины нет (владелец, 12.08.2026).
    /// </para>
    /// <para>
    /// Сборка подписи живёт в android-библиотеке, потому и проверяется по исходнику — тем же
    /// порядком, что правила плиток (<c>Tiles/TileMarksTests</c>). Слова же лежат в ресурсах и
    /// сверяются как есть.
    /// </para>
    /// </summary>
    [Fact]
    public void The_captions_are_signs_and_marks_rather_than_words()
    {
        var words = AppWords();

        Assert.Equal("км/ч", words["TelemetrySpeedSign"]);
        Assert.Equal("ШИМ %", words["TelemetryPwmSign"]);
        Assert.Equal("V", words["TelemetryVoltageSign"]);
        Assert.Equal("t°", words["TelemetryBoardTempSign"]);
        Assert.Equal("Заряд %", words["TelemetryBatterySign"]);
        Assert.Equal("Пробег", words["TelemetryTripShort"]);
        Assert.Equal("Поездка, км", words["MetricTripCounter"]);

        // Слов сторон в центре не осталось вовсе: «макс» и «мин» стали знаками, «тек» — пустотой.
        foreach (string gone in (string[])["CentreAspectCurrent", "CentreAspectMax", "CentreAspectMin"])
        {
            Assert.False(words.ContainsKey(gone), $"Ключ «{gone}» вернулся: сторона снова стала словом.");
        }

        string readings = RepoFiles.Read(Readings);

        // Знаки — те же, что у плиток: язык знаков на экране один, и вторая копия глифа разошлась бы
        // с первой молча.
        Assert.Contains("CenterAspect.Max => TileView.MarkHighest", readings);
        Assert.Contains("CenterAspect.Min => TileView.MarkLowest", readings);

        // На панели знак величины старше короткого имени, короткое — полного.
        Assert.Contains(
            "? Said(words, label + \"Sign\") ?? Said(words, label + \"Short\") ?? words(label)", readings);

        // Ни висячего пробела у имени без знака, ни косой, за которой пусто: пара сливается в одно
        // имя только тогда, когда вторая сторона знак несёт.
        Assert.Contains("mark.Length > 0 ? bare + \" \" + mark : bare", readings);
        Assert.Contains("second.Metric == row.First.Metric && Mark(second.Aspect).Length > 0", readings);
    }

    /// <summary>
    /// <b>Сокращения — только для панели</b> (решение владельца 12.08.2026). В меню правки места
    /// вдоволь: строка идёт во всю ширину окна и переносится, зато выбирают по ней вслепую — знак
    /// «V» в списке из двенадцати строк узнать труднее, чем слово. Отсюда две меры одного и того же
    /// показания: панель зовёт <c>Caption</c> (знак), меню — <c>Title</c> (имя целиком).
    /// <para>
    /// Двум величинам полного имени <b>не хватило</b>: в ресурсах они названы от раздела экрана
    /// «Данные» («Плата», «За поездку») и в одиночку не читаются — им заведён ключ <c>…Full</c>.
    /// Сами имена не тронуты: на «Данных» они стоят рядом с «Двигателем», «За сеанс» и «Одометром»,
    /// где «Температура» и «Пробег» потеряли бы, о чём речь.
    /// </para>
    /// <para>
    /// Строки меню при умолчании: <c>ШИМ ▲</c>, <c>Температура / ▲</c>, <c>Пробег</c>,
    /// <c>Заряд / Напряжение ▼</c>; в списке «добавить» — те же имена по одному, со знаками сторон.
    /// </para>
    /// </summary>
    [Fact]
    public void The_panel_abbreviates_where_the_menu_spells_the_metric_out()
    {
        var words = AppWords();

        Assert.Equal("Температура", words["TelemetryBoardTempFull"]);
        Assert.Equal("Пробег", words["TelemetryTripFull"]);

        // Полные имена величин на своих местах не тронуты: их читает экран «Данные» и меню плитки.
        Assert.Equal("Плата", words["TelemetryBoardTemp"]);
        Assert.Equal("За поездку", words["TelemetryTrip"]);

        string readings = RepoFiles.Read(Readings);

        // Меню — своя мера: полное имя, а знак величины и короткое имя ему не указ.
        Assert.Contains("public static string Title(CenterRow row, Func<string, string> words)", readings);
        Assert.Contains(": Said(words, label + \"Full\") ?? words(label);", readings);

        // Обе меры собирают строку одним и тем же кодом: разойдись они — разошлись бы и правила
        // склейки пары, и знаки сторон.
        Assert.Contains("Line(row, words, tight: true)", readings);
        Assert.Contains("Line(row, words, tight: false)", readings);

        // Редактор — и список строк, и «+ Добавить» — спрашивает меру меню, а не панели.
        string editor = RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/CentreEditor.cs");

        Assert.Contains("CenterReadings.Title(current[at], words)", editor);
        Assert.Contains("CenterReadings.Title(new CenterRow(choice, null), words)", editor);
        Assert.DoesNotContain("CenterReadings.Caption(", editor);

        // А панель — меру панели: подпись строки и худшая подпись, по которой садится кегль.
        string block = RepoFiles.Read("WheelTalk.Dashboard.Droid/Widgets/SpeedBlockDrawable.cs");

        Assert.Contains("CenterReadings.Caption(rows[index], Options.Words)", block);
        Assert.DoesNotContain("CenterReadings.Title(", block);
    }

    /// <summary>
    /// Счётчик поездки предлагается, но не навязывается: в списке «добавить» он есть, в умолчании
    /// его нет — владелец о том не просил, а свежая установка обязана показывать прежние четыре
    /// строки. Сторона у него одна: он сам себе максимум.
    /// </summary>
    [Fact]
    public void The_trip_counter_is_offered_but_not_imposed()
    {
        string readings = RepoFiles.Read(Readings);

        Assert.Contains("public const string TripCounter = \"trip_counter\";", readings);
        Assert.Contains("new(TripCounter, [CenterAspect.Current])", readings);

        // Число берётся из кадра — панель его считает, а не читает из снимка: в каталоге величин
        // такой нет и быть не может.
        Assert.Contains("(TripCounter, CenterAspect.Current) => frame.TripCounterKm", readings);
        Assert.Contains("[TripCounter] = new(\"MetricTripCounter\", Decimals: 1, Digits: 4)", readings);

        // Умолчание состава живёт в ядре, и счётчика в нём нет ни одним упоминанием.
        Assert.DoesNotContain("trip_counter", RepoFiles.Read("WheelTalk.Core/Dashboard/CenterPanel.cs"));
        Assert.Equal(4, CenterLayout.Default.Count);
    }

    /// <summary>
    /// Путь владельца целиком: <b>нажал сброс — счёт с нуля, перезапустил — счёт продолжился</b>.
    /// Счётчик поездки тем и отличается от пробега, что его не обнуляет ничто, кроме руки: ни новая
    /// поездка, ни перезапуск, ни смена колеса (решение владельца 12.08.2026 — «как „Поездка A/B“ в
    /// машине»). Считается он тем же ядром, что и плитки-дистанции (<see cref="TripBaselines"/>).
    /// </summary>
    [Fact]
    public void The_trip_counter_is_zeroed_by_hand_and_keeps_its_point_across_restarts()
    {
        // Имя счётчика живёт в android-библиотеке — здесь оно тем же словом, а совпадение стережёт
        // проверка ниже.
        const string counter = "centre";
        const string wheel = "AA:BB:CC:DD:EE:FF";

        var points = new TripBaselines();

        // Первая встреча заводит точку: счёт начинается здесь и сейчас, а не с полного одометра.
        Assert.Equal(0, points.Since(wheel, counter, 1200));
        Assert.Equal(15, points.Since(wheel, counter, 1215));

        // Нажата кнопка шторки — путь обнулён.
        points.Reset(wheel, counter, 1215);
        Assert.Equal(0, points.Since(wheel, counter, 1215));
        Assert.Equal(3, points.Since(wheel, counter, 1218));

        // Перезапуск: набор пережил запись и чтение, и счёт продолжился — не начался заново.
        var after = TripBaselines.Read(points.Write());
        Assert.Equal(3, after.Since(wheel, counter, 1218));

        // Другое колесо считает своё, а вернувшийся к прежнему находит прежнее: одометр у каждого свой.
        Assert.Equal(0, after.Since("11:22:33:44:55:66", counter, 9000));
        Assert.Equal(3, after.Since(wheel, counter, 1218));

        Assert.Contains($"public const string Centre = \"{counter}\";", RepoFiles.Read(Points));
    }

    /// <summary>
    /// <b>Сброс висит на кнопке, которая уже есть</b> — «Сброс пиков» в шторке (решение владельца
    /// 12.08.2026): второй кнопки «сбросить» на экране заводить не стали. Молчащий одометр точку не
    /// двигает: поставить её на ноль значило бы показать весь одометр как путь этой поездки.
    /// <para>
    /// Стенд повторяет боевое поведением, а не только словом (память владельца 10.08.2026): там та
    /// же кнопка двигает ту же точку — иначе путь «нажал, перезапустил, посмотрел» нечем пройти
    /// глазами.
    /// </para>
    /// </summary>
    [Fact]
    public void The_counter_is_reset_by_the_quick_sheet_in_both_the_app_and_the_stand()
    {
        string reset = RepoFiles.MethodBody(
            RepoFiles.Read("WheelTalk.Droid/Main/MainActivity.cs"), "private Task ResetPeaksAsync()");

        Assert.Contains("_tripPoints.Reset(wheel, TripPoints.Centre, snapshot.TotalDistanceKm)", reset);
        Assert.Contains("TotalDistanceKm: > 0 }", reset);

        string stand = RepoFiles.Read("WheelTalk.Lab.Droid/LabActivity.cs");

        Assert.Contains("_tripPoints.Reset(LabWheel, TripPoints.Centre, frame.TotalDistanceKm)", stand);
        Assert.Contains("ResetTripCounter();", RepoFiles.MethodBody(
            stand, "private IReadOnlyList<QuickSheetCommand> BuildFakeCommands()"));
    }

    /// <summary>
    /// <b>Точки отсчёта — одним хозяином.</b> Их спрашивают двое: плитки-дистанции и счётчик центра,
    /// а хранилище у них одно, и каждый экземпляр пишет свой набор целиком. Заведи по экземпляру на
    /// брата — и сброс одного затрёт точку другого при первой же записи; поймать это на глаз нельзя,
    /// потому оно и заперто здесь.
    /// </summary>
    [Fact]
    public void The_points_of_the_distances_have_a_single_owner()
    {
        Assert.Contains(
            "services.AddSingleton(sp => new TripPoints(sp.GetRequiredService<ITripBaselineStore>()));",
            RepoFiles.Read("WheelTalk.Droid/App/Composition/DashboardServiceCollectionExtensions.cs"));

        // Экран плиток получает готовые точки, а не хранилище: свои он завёл бы вторым экземпляром.
        Assert.Contains("TripPoints? trips = null", RepoFiles.Read(
            "WheelTalk.Dashboard.Droid/Screen/Tiles/TilesScreen.cs"));

        Assert.Contains("_tripPoints = new(new LabTripBaselineFile())",
            RepoFiles.Read("WheelTalk.Lab.Droid/LabActivity.cs"));

        // Запись — без замыкания: путь спрашивают с кадра панели, а лямбда там мусорит шестьдесят
        // раз в секунду.
        string points = RepoFiles.Read(Points);
        Assert.DoesNotContain("Func<double>", points);
    }

    private const string Readings = "WheelTalk.Dashboard.Droid/Widgets/CenterReadings.cs";

    private const string Points = "WheelTalk.Dashboard.Droid/Screen/Tiles/TripPoints.cs";

    private static Dictionary<string, string> AppWords() =>
        XDocument.Parse(RepoFiles.Read("WheelTalk.Droid/Resources/Strings/AppStrings.resx"))
            .Root!.Elements("data")
            .ToDictionary(data => data.Attribute("name")!.Value, data => data.Element("value")!.Value);
}
