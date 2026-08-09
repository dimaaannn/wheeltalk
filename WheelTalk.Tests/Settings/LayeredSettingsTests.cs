using WheelTalk.Core.Settings;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Settings;

/// <summary>
/// Три слоя и две команды над ними. Проверяется не «сохранилось ли», а то, из-за чего слои вообще
/// заводились: два колеса с разными значениями не путаются, снятие переопределения возвращает
/// общее, а перезапись общего меняет его и для того колеса, у которого своего значения нет.
/// <para>
/// Область у каждого действия — аргументом (план 29 §29.3), поэтому колесо тут не «выбирают», а
/// называют на месте. Свойство <see cref="LayeredSettings.Scope"/> осталось с одним смыслом —
/// боевым: по нему живут декодер, пороги и лента, и трогает его только выбор колеса.
/// </para>
/// </summary>
public class LayeredSettingsTests
{
    private const string Sherman = "88:25:83:F5:75:4A";
    private const string MTen3 = "88:25:83:F2:1A:98";
    private const string Global = LayeredSettings.GlobalScope;

    private static readonly Dictionary<string, string> Factory = new()
    {
        ["GotwayVoltage"] = "1",
        ["PwmWarning"] = "80",
    };

    [Fact]
    public void An_untouched_setting_comes_from_the_factory()
    {
        var settings = Build();

        var value = settings.Get(Global, "PwmWarning");

        Assert.Equal("80", value.Value);
        Assert.Equal(SettingOrigin.Factory, value.Origin);
        Assert.False(value.IsOverridden);
    }

    [Fact]
    public void A_setting_nobody_has_heard_of_reads_as_nothing_rather_than_empty()
    {
        var settings = Build();

        Assert.Null(settings.Get(Global, "NoSuchSetting").Value);
    }

    /// <summary>
    /// Смысл слоёв целиком: у Sherman L и MTen3 разные паки, разный знак тока и разные пороги, а
    /// соседство между ними не должно ничего перемешивать.
    /// </summary>
    [Fact]
    public void Two_wheels_keep_their_own_values_and_do_not_leak_into_each_other()
    {
        var settings = Build();

        settings.Set(Sherman, "PwmWarning", "75");
        settings.Set(MTen3, "PwmWarning", "85");

        Assert.Equal("75", settings.Get(Sherman, "PwmWarning").Value);
        Assert.Equal("85", settings.Get(MTen3, "PwmWarning").Value);
    }

    [Fact]
    public void An_edit_with_a_wheel_named_becomes_that_wheel_s_override()
    {
        var settings = Build();

        settings.Set(Sherman, "PwmWarning", "75");

        Assert.Equal(new ResolvedSetting("75", SettingOrigin.Wheel), settings.Get(Sherman, "PwmWarning"));
    }

    /// <summary>Колеса ещё нет — правка может означать только общее значение.</summary>
    [Fact]
    public void An_edit_without_a_wheel_becomes_the_global_value()
    {
        var settings = Build();

        settings.Set(Global, "PwmWarning", "70");

        Assert.Equal(new ResolvedSetting("70", SettingOrigin.Global), settings.Get(Global, "PwmWarning"));
    }

    [Fact]
    public void Clearing_an_override_brings_the_global_value_back()
    {
        var settings = Build();
        settings.Set(Global, "PwmWarning", "70");        // общее
        settings.Set(Sherman, "PwmWarning", "75");       // поверх него

        settings.ClearOverride(Sherman, "PwmWarning");

        Assert.Equal(new ResolvedSetting("70", SettingOrigin.Global), settings.Get(Sherman, "PwmWarning"));
    }

    [Fact]
    public void Clearing_an_override_that_was_never_there_falls_all_the_way_to_the_factory()
    {
        var settings = Build();

        settings.ClearOverride(Sherman, "PwmWarning");

        Assert.Equal(new ResolvedSetting("80", SettingOrigin.Factory), settings.Get(Sherman, "PwmWarning"));
    }

    /// <summary>
    /// Без этого заводской слой был бы слоем только на бумаге: правка без выбранного колеса
    /// навсегда заводила запись в общем, и вернуться к тому, «к чему можно вернуться, когда всё
    /// запутано» (план 6 §2.1), было нечем.
    /// </summary>
    [Fact]
    public void Clearing_the_global_value_brings_the_factory_one_back()
    {
        var settings = Build();
        settings.Set(Global, "PwmWarning", "70");

        settings.ClearGlobal("PwmWarning");

        Assert.Equal(new ResolvedSetting("80", SettingOrigin.Factory), settings.Get(Global, "PwmWarning"));
    }

    /// <summary>
    /// Вернул значение колеса к общему руками — переопределения быть не должно: иначе рамка горит
    /// вечно, а «вернуть общее» визуально ничего не меняет. Довод тот же, по которому переопределение
    /// снимает <see cref="LayeredSettings.PromoteToGlobal"/>.
    /// </summary>
    [Fact]
    public void A_wheel_value_typed_back_to_the_global_one_stops_being_an_override()
    {
        var settings = Build();
        settings.Set(Global, "PwmWarning", "70");
        settings.Set(Sherman, "PwmWarning", "75");

        settings.Set(Sherman, "PwmWarning", "70");

        Assert.Equal(new ResolvedSetting("70", SettingOrigin.Global), settings.Get(Sherman, "PwmWarning"));
    }

    /// <summary>То же самое этажом ниже: общее значение, совпавшее с заводским, — не общее значение.</summary>
    [Fact]
    public void A_global_value_typed_back_to_the_factory_one_stops_being_a_layer()
    {
        var settings = Build();
        settings.Set(Global, "PwmWarning", "70");

        settings.Set(Global, "PwmWarning", "80");

        Assert.Equal(new ResolvedSetting("80", SettingOrigin.Factory), settings.Get(Global, "PwmWarning"));
    }

    /// <summary>
    /// Настройка, у которой не бывает «этого колеса»: звук тревоги — свойство телефона и райдера.
    /// Правка при названном колесе не должна заводить переопределение, которого физически не может
    /// быть, — вместе с рамкой и меню, объясняющими несуществующую разницу.
    /// </summary>
    [Fact]
    public void A_setting_that_cannot_differ_between_wheels_is_edited_globally_anyway()
    {
        var settings = Build();

        settings.Set(Sherman, "AlertSound", "False", SettingLayer.GlobalOnly);

        Assert.Equal(new ResolvedSetting("False", SettingOrigin.Global), settings.Get(Sherman, "AlertSound"));
        Assert.Equal(new ResolvedSetting("False", SettingOrigin.Global), settings.Get(MTen3, "AlertSound"));
    }

    /// <summary>
    /// Переопределение у такой настройки в базе всё же бывает — заведённое до того, как признак
    /// появился. Правка, которая пишет под ним, выглядела бы не сработавшей.
    /// </summary>
    [Fact]
    public void An_override_left_over_from_before_gives_way_to_the_global_edit()
    {
        var settings = Build();
        settings.Set(Sherman, "AlertSound", "True");

        settings.Set(Sherman, "AlertSound", "False", SettingLayer.GlobalOnly);

        Assert.Equal(new ResolvedSetting("False", SettingOrigin.Global), settings.Get(Sherman, "AlertSound"));
    }

    /// <summary>
    /// «Перезаписать значение по умолчанию» — и переопределение при этом снимается: значение и так
    /// стало общим, а оставить его вторым экземпляром значит завести расхождение на ровном месте.
    /// </summary>
    [Fact]
    public void Promoting_a_wheel_value_makes_it_global_and_stops_being_an_override()
    {
        var settings = Build();
        settings.Set(Sherman, "PwmWarning", "75");

        settings.PromoteToGlobal(Sherman, "PwmWarning");

        Assert.Equal(new ResolvedSetting("75", SettingOrigin.Global), settings.Get(Sherman, "PwmWarning"));

        // И для второго колеса, у которого своего значения нет.
        Assert.Equal(new ResolvedSetting("75", SettingOrigin.Global), settings.Get(MTen3, "PwmWarning"));
    }

    /// <summary>Второе колесо со своим значением перезапись общего не трогает — на то оно и своё.</summary>
    [Fact]
    public void Promoting_does_not_touch_a_wheel_that_has_a_value_of_its_own()
    {
        var settings = Build();
        settings.Set(MTen3, "PwmWarning", "85");
        settings.Set(Sherman, "PwmWarning", "75");

        settings.PromoteToGlobal(Sherman, "PwmWarning");

        Assert.Equal(new ResolvedSetting("85", SettingOrigin.Wheel), settings.Get(MTen3, "PwmWarning"));
    }

    /// <summary>
    /// Обратный край: у ряда ячеек не бывает общего значения — 20S у одного колеса и 16S у другого.
    /// В общей области писать некуда, и правка не делается вовсе; молчаливая запись в общий слой
    /// раздала бы чужой ряд каждому колесу, у которого своего числа нет.
    /// </summary>
    [Fact]
    public void A_setting_that_belongs_to_a_wheel_is_not_written_in_the_global_area()
    {
        var settings = Build();

        settings.Set(Global, "CellsInSeries", "20", SettingLayer.WheelOnly);

        Assert.Null(settings.Get(Global, "CellsInSeries", SettingLayer.WheelOnly).Value);
        Assert.Null(settings.Get(Sherman, "CellsInSeries", SettingLayer.WheelOnly).Value);
    }

    /// <summary>
    /// «Сделать значением по умолчанию» — вторая дверь в общий слой, и для настройки колеса она
    /// заперта той же щеколдой: 20S иначе уехали бы на все колёса разом.
    /// </summary>
    [Fact]
    public void A_setting_that_belongs_to_a_wheel_is_never_promoted_to_the_global_layer()
    {
        var settings = Build();
        settings.Set(Sherman, "CellsInSeries", "20", SettingLayer.WheelOnly);

        settings.PromoteToGlobal(Sherman, "CellsInSeries", SettingLayer.WheelOnly);

        Assert.Equal(new ResolvedSetting("20", SettingOrigin.Wheel),
            settings.Get(Sherman, "CellsInSeries", SettingLayer.WheelOnly));
        Assert.Null(settings.Get(MTen3, "CellsInSeries", SettingLayer.WheelOnly).Value);
    }

    /// <summary>
    /// Беда тихая и потому опаснее двух дверей: значение в общем слое могло оказаться там и не
    /// нашей правкой. Колесо без своего числа обязано увидеть заводское, а не чужой ряд.
    /// </summary>
    [Fact]
    public void A_setting_that_belongs_to_a_wheel_never_reads_the_global_layer()
    {
        var settings = Build();
        settings.Set(Global, "CellsInSeries", "20");   // как если бы туда написали мимо признака

        Assert.Null(settings.Get(MTen3, "CellsInSeries", SettingLayer.WheelOnly).Value);

        // И своё число колеса не теряется оттого, что в общем слое лежит такое же.
        settings.Set(MTen3, "CellsInSeries", "20", SettingLayer.WheelOnly);
        Assert.Equal(new ResolvedSetting("20", SettingOrigin.Wheel),
            settings.Get(MTen3, "CellsInSeries", SettingLayer.WheelOnly));
    }

    /// <summary>
    /// Взгляд на общий слой — это взгляд, а не переезд в него (план 29 §29.3). Пока область была
    /// одна на два вопроса, страница настроек, открытая на «Общем», снимала слой колеса со всего
    /// живого: у стоящего у стены колеса пороги в этот момент были не его.
    /// </summary>
    [Fact]
    public void Looking_at_the_global_layer_leaves_the_wheel_the_app_lives_by_alone()
    {
        var settings = Build();
        settings.Scope = Sherman;                       // боевая область — колесо
        settings.Set(Global, "PwmWarning", "70");
        settings.Set(Sherman, "PwmWarning", "75");

        // Страница смотрит общий слой и правит его — и видит там своё, а не значение колеса.
        Assert.Equal(new ResolvedSetting("70", SettingOrigin.Global), settings.Get(Global, "PwmWarning"));
        settings.Set(Global, "PwmWarning", "65");

        // А живут по-прежнему по колесу: рычаг не сдвинулся, переопределение на месте.
        Assert.Equal(Sherman, settings.Scope);
        Assert.Equal(new ResolvedSetting("75", SettingOrigin.Wheel), settings.Get(settings.Scope, "PwmWarning"));
    }

    /// <summary>
    /// Живые объекты настроек — те, из которых читают декодеры и движок тревог, — перестраиваются
    /// по этому событию. Без него смена колеса оставила бы на экране прошлые значения.
    /// </summary>
    [Fact]
    public void Changing_the_wheel_or_a_value_announces_itself()
    {
        var settings = Build();
        int changes = 0;
        settings.Changed += () => changes++;

        settings.Scope = Sherman;
        settings.Set(Sherman, "PwmWarning", "75");
        settings.ClearOverride(Sherman, "PwmWarning");
        settings.Scope = Sherman;   // то же самое колесо — событию взяться неоткуда

        Assert.Equal(3, changes);
    }

    [Fact]
    public void The_key_list_covers_every_layer()
    {
        var settings = Build();
        settings.Set(Global, "OnlyGlobal", "1");
        settings.Set(Sherman, "OnlyThisWheel", "1");

        Assert.Equal(
            ["GotwayVoltage", "OnlyGlobal", "OnlyThisWheel", "PwmWarning"],
            settings.Keys(Sherman).Order());

        // Взгляд на общий слой чужого ключа не показывает: в нём его нет.
        Assert.Equal(
            ["GotwayVoltage", "OnlyGlobal", "PwmWarning"],
            settings.Keys(Global).Order());
    }

    private static LayeredSettings Build() => new(new InMemorySettingsStore(), Factory);
}
