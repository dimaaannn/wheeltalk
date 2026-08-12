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
        var two = CenterTypography.Fit("888.8", "ШИМ макс", 2, Room, Width, new Ruler(), Metrics());
        var four = CenterTypography.Fit("888.8", "ШИМ макс", 4, Room, Width, new Ruler(), Metrics());
        var six = CenterTypography.Fit("888.8", "ШИМ макс", 6, Room, Width, new Ruler(), Metrics());

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
        var fit = CenterTypography.Fit("888.8", "ШИМ макс", rows, Room, Width, new Ruler(), Metrics());

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
        var fit = CenterTypography.Fit("888.8", "ШИМ макс", 6, 120, Width, new Ruler(), Metrics());

        Assert.Equal(2, fit.Rows);
        Assert.True(fit.FontPx >= Floor);
    }

    /// <summary>Места нет вовсе — не показываем ничего: одна нечитаемая строка хуже пустоты.</summary>
    [Fact]
    public void No_room_means_no_rows()
    {
        Assert.Equal(0, CenterTypography.Fit("888.8", "ШИМ макс", 4, 40, Width, new Ruler(), Metrics()).Rows);
    }

    /// <summary>Узкая полоса ужимает кегль шириной, а не только высотой: строка не вправе вылезти вбок.</summary>
    [Fact]
    public void A_narrow_centre_shrinks_the_line_too()
    {
        var wide = CenterTypography.Fit("888.8", "ШИМ макс", 2, Room, 500, new Ruler(), Metrics());
        var narrow = CenterTypography.Fit("888.8", "ШИМ макс", 2, Room, 200, new Ruler(), Metrics());

        Assert.True(narrow.FontPx < wide.FontPx);
    }

    /// <summary>
    /// Умолчание — <b>те же четыре смысла</b>, что стояли в центре жёстко: макс ШИМ, температура
    /// «тек / макс», пробег поездки и «заряд % / мин В». Совместимость глаза: человек смотрит сюда
    /// каждый выезд, и свежая установка не должна показывать ему другой набор.
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
}
