using System.Globalization;
using WheelTalk.Core.Settings;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Settings;

/// <summary>
/// Связка слоёв с живыми объектами настроек — самая тяжёлая часть плана 6 и единственное место,
/// где ошибка не видна глазом: объект нельзя подменять при смене колеса, потому что в него пишут
/// декодеры, а движок тревог из него читает.
/// </summary>
public class SettingsBinderTests
{
    private const string Sherman = "88:25:83:F5:75:4A";
    private const string MTen3 = "88:25:83:F2:1A:98";
    private const string Global = LayeredSettings.GlobalScope;

    /// <summary>Стоит вместо AppWheelConfig: важно, что это один и тот же экземпляр от начала до конца.</summary>
    private sealed class LiveOptions
    {
        public int PwmWarning { get; set; } = 80;
        public bool HwPwm { get; set; }
        public bool AlarmsEnabled { get; set; } = true;
        public bool Sound { get; set; } = true;
        public int CellsInSeries { get; set; }

        /// <summary>Сколько раз сработал крючок «правил человек» — см. <see cref="SettingDescriptor.AfterEdit"/>.</summary>
        public int Edits { get; set; }
    }

    [Fact]
    public void The_factory_layer_is_whatever_the_objects_said_before_anyone_edited_them()
    {
        var options = new LiveOptions();

        var factory = SettingsBinder.FactoryDefaults(Describe(options));

        Assert.Equal("80", factory["Alerts:PwmWarning"]);

        // Сообщённого колесом нет ни в одном слое, включая нижний: до первого кадра про него
        // нечего сказать, и заводское значение здесь было бы выдумкой.
        Assert.DoesNotContain("WheelConfig:HwPwm", factory.Keys);
    }

    [Fact]
    public void An_edit_reaches_the_live_object_without_anyone_asking_it_to()
    {
        var options = new LiveOptions();
        var (binder, _) = Build(options);
        var descriptor = binder.Descriptors.First(d => d.Key == "Alerts:PwmWarning");

        binder.Set(descriptor, "70", Global);

        Assert.Equal(70, options.PwmWarning);
    }

    /// <summary>
    /// Ради чего всё: у двух колёс свои пороги, и переключение обновляет **тот же** объект, а не
    /// подсовывает новый. Проверяется тождеством ссылки, потому что именно на нём всё и держится.
    /// </summary>
    [Fact]
    public void Switching_wheels_updates_the_same_object_in_place()
    {
        var options = new LiveOptions();
        var (binder, settings) = Build(options);
        var descriptor = binder.Descriptors.First(d => d.Key == "Alerts:PwmWarning");

        settings.Scope = Sherman;
        binder.Set(descriptor, "75", Sherman);
        Assert.Equal(75, options.PwmWarning);

        settings.Scope = MTen3;
        Assert.Equal(80, options.PwmWarning);   // своего значения нет — вернулось заводское

        binder.Set(descriptor, "85", MTen3);
        settings.Scope = Sherman;

        Assert.Equal(75, options.PwmWarning);
    }

    /// <summary>
    /// `HwPwm` пишет декодер на первом же кадре. Сохранить его как переопределение колеса значит
    /// показать пользователю правку, которой он не делал, — при следующем подключении она придёт
    /// снова.
    /// </summary>
    [Fact]
    public void What_the_wheel_reports_is_never_stored_and_never_written_back()
    {
        var options = new LiveOptions();
        var (binder, settings) = Build(options);
        var reported = binder.Descriptors.First(d => d.Key == "WheelConfig:HwPwm");

        binder.Set(reported, "True", Sherman);
        Assert.Null(settings.Get(Sherman, "WheelConfig:HwPwm").Value);

        // Декодер сообщил своё — перечитывание слоёв не должно это затирать.
        options.HwPwm = true;
        settings.Scope = Sherman;

        Assert.True(options.HwPwm);
        Assert.Equal("True", binder.Read(reported, Sherman).Value);
    }

    /// <summary>
    /// Звук тревоги — свойство телефона и райдера: беззвучный режим, шлем и карман одни и те же, на
    /// чём бы человек ни ехал. Правка при выбранном колесе не должна заводить переопределение,
    /// которого физически не может быть, — иначе на настройке появятся рамка и меню, объясняющие
    /// разницу между двумя колёсами, а разницы нет.
    /// </summary>
    [Fact]
    public void A_setting_that_cannot_differ_between_wheels_never_becomes_an_override()
    {
        var options = new LiveOptions();
        var (binder, settings) = Build(options);
        var sound = binder.Descriptors.First(d => d.Key == "AlertSignals:Sound");
        settings.Scope = Sherman;

        binder.Set(sound, "False", Sherman);

        Assert.Equal(SettingOrigin.Global, binder.Read(sound, Sherman).Origin);
        Assert.False(options.Sound);

        settings.Scope = MTen3;
        Assert.False(options.Sound);
    }

    /// <summary>
    /// Возврат к заводскому — то, чем чинится запутанная настройка. Пока его не было, любая правка
    /// без выбранного колеса оставалась в общем слое навсегда.
    /// </summary>
    [Fact]
    public void Dropping_the_global_value_puts_the_factory_one_back_into_the_live_object()
    {
        var options = new LiveOptions();
        var (binder, _) = Build(options);
        var descriptor = binder.Descriptors.First(d => d.Key == "Alerts:PwmWarning");
        binder.Set(descriptor, "70", Global);

        binder.ClearGlobal(descriptor);

        Assert.Equal(80, options.PwmWarning);
        Assert.Equal(SettingOrigin.Factory, binder.Read(descriptor, Global).Origin);
    }

    /// <summary>
    /// Каскад «главный выключатель → остальное»: выключены тревоги — под ними не рисуется ничего.
    /// Условие живёт в описании настройки, а не в коде страницы, и закрываться должно сразу.
    /// </summary>
    [Fact]
    public void A_master_switch_closes_the_rows_under_it_immediately()
    {
        var options = new LiveOptions();
        var (binder, _) = Build(options);

        Assert.Contains(
            binder.Page(SettingsPage.Warnings, Global).SelectMany(g => g),
            d => d.Key == "Alerts:PwmWarning");

        options.AlarmsEnabled = false;

        Assert.DoesNotContain(
            binder.Page(SettingsPage.Warnings, Global).SelectMany(g => g),
            d => d.Key == "Alerts:PwmWarning");
    }

    /// <summary>
    /// Крючок «правил человек» — ровно про правку человеком, и ни про что другое. Заведён под
    /// пароль InMotion: его смена обязана начать разговор с колесом заново, а вот старт приложения,
    /// смена слоя, смена колеса и правка соседней строки — не обязаны. Всё это идёт через
    /// <see cref="SettingsBinder.Apply"/>, и повешенное туда действие срабатывало бы, когда человек
    /// ничего не делал.
    /// </summary>
    [Fact]
    public void The_after_edit_hook_fires_for_an_edit_and_for_nothing_else()
    {
        var options = new LiveOptions();
        var (binder, settings) = Build(options);
        var edited = binder.Descriptors.First(d => d.Key == "Alerts:PwmWarning");
        var neighbour = binder.Descriptors.First(d => d.Key == "AlertSignals:Sound");

        binder.Set(edited, "70", Global);
        Assert.Equal(1, options.Edits);

        // Восстановление состояния — не правка. Старт приложения зовёт ровно это.
        binder.Apply();
        // Правка соседней строки прогоняет Apply по всем описаниям, включая наше.
        binder.Set(neighbour, "False", Global);
        // Смена колеса и смена слоя — тоже Apply, только изнутри LayeredSettings.
        settings.Scope = MTen3;
        settings.Scope = LayeredSettings.GlobalScope;

        Assert.Equal(1, options.Edits);
    }

    /// <summary>
    /// Та же цифра, введённая второй раз, — по-прежнему правка. Для настройки это ничего не
    /// меняет, а для человека меняет всё: повторить ввод — самый частый жест того, кто не уверен,
    /// что нажатие засчиталось, и промолчать на него значит оставить его в тупике.
    /// </summary>
    [Fact]
    public void The_same_value_typed_again_is_still_an_edit()
    {
        var options = new LiveOptions();
        var (binder, _) = Build(options);
        var descriptor = binder.Descriptors.First(d => d.Key == "Alerts:PwmWarning");

        binder.Set(descriptor, "70", Global);
        binder.Set(descriptor, "70", Global);

        Assert.Equal(2, options.Edits);
    }

    /// <summary>
    /// Признак «только у колеса» держит биндер, а не разметка. Правка в общей области не пишется
    /// никуда — ни в слои, ни в живой объект: писать её некуда, и молча раздать чужой ряд всем
    /// колёсам хуже, чем не сделать ничего.
    /// </summary>
    [Fact]
    public void A_wheel_only_setting_is_not_edited_while_no_wheel_is_chosen()
    {
        var options = new LiveOptions();
        var (binder, settings) = Build(options);
        var descriptor = binder.Descriptors.First(d => d.Key == "WheelConfig:CellsInSeries");

        binder.Set(descriptor, "20", Global);

        // Заводское «не задано» на месте: ни один слой правку не принял, живой объект не тронут.
        Assert.Equal(new ResolvedSetting("0", SettingOrigin.Factory), settings.Get(Global, descriptor.Key));
        Assert.Equal(0, options.CellsInSeries);
    }

    /// <summary>Вторая дверь: перенести своё число колеса в общее нельзя даже командой строки.</summary>
    [Fact]
    public void A_wheel_only_setting_is_not_promoted_to_the_global_layer()
    {
        var options = new LiveOptions();
        var (binder, settings) = Build(options);
        var descriptor = binder.Descriptors.First(d => d.Key == "WheelConfig:CellsInSeries");
        settings.Scope = Sherman;
        binder.Set(descriptor, "20", Sherman);

        binder.PromoteToGlobal(descriptor, Sherman);

        settings.Scope = MTen3;
        Assert.Equal(0, options.CellsInSeries);
    }

    /// <summary>
    /// Кнопка «рассчитать» правит соседнюю строку по ключу — и через биндер, а не мимо: слой,
    /// крючок правки и признаки у неё те же, что у правки руками. Область — боевая: ряд считается
    /// по кадру живого колеса и принадлежит ему, куда бы ни смотрел переключатель страницы.
    /// </summary>
    [Fact]
    public void An_action_edits_its_neighbour_by_key()
    {
        var options = new LiveOptions();
        var (binder, settings) = Build(options);
        settings.Scope = Sherman;

        binder.Set("WheelConfig:CellsInSeries", "24");

        Assert.Equal(24, options.CellsInSeries);
        Assert.Equal(new ResolvedSetting("24", SettingOrigin.Wheel),
            settings.Get(Sherman, "WheelConfig:CellsInSeries"));
    }

    /// <summary>
    /// Гейт плана 29 §29.3, первая половина. Правка в смотровом «Общем» меняет общий слой и <b>не
    /// трогает живые объекты</b>, пока у колеса своё значение: райдер едет по своему колесу, что бы
    /// ни было открыто в настройках. Рамка «переопределено» на строке — тот самый ответ на вопрос
    /// «почему число на экране не дрогнуло».
    /// </summary>
    [Fact]
    public void An_edit_in_the_global_view_does_not_reach_a_wheel_that_has_its_own_value()
    {
        var options = new LiveOptions();
        var (binder, settings) = Build(options);
        var descriptor = binder.Descriptors.First(d => d.Key == "Alerts:PwmWarning");
        settings.Scope = Sherman;
        binder.Set(descriptor, "75", Sherman);

        binder.Set(descriptor, "70", Global);

        Assert.Equal(75, options.PwmWarning);
        Assert.Equal(new ResolvedSetting("70", SettingOrigin.Global), binder.Read(descriptor, Global));
        Assert.Equal(new ResolvedSetting("75", SettingOrigin.Wheel), binder.Read(descriptor, Sherman));

        // И боевая область осталась на колесе: страница её не двигает вовсе.
        Assert.Equal(Sherman, settings.Scope);
    }

    /// <summary>
    /// Вторая половина того же гейта: правка в «Колесе» видна живым объектам сразу — без ухода со
    /// страницы, без перезапуска.
    /// </summary>
    [Fact]
    public void An_edit_in_the_wheel_view_reaches_the_live_object_at_once()
    {
        var options = new LiveOptions();
        var (binder, settings) = Build(options);
        var descriptor = binder.Descriptors.First(d => d.Key == "Alerts:PwmWarning");
        settings.Scope = Sherman;

        binder.Set(descriptor, "72", Sherman);

        Assert.Equal(72, options.PwmWarning);
    }

    /// <summary>
    /// Строку, которую в этой области писать некуда, в ней и не показывают: у ряда ячеек общего
    /// значения не бывает. Раньше это решал делегат, спрашивавший боевую область у слоёв (план 27
    /// §27.4); теперь ответ следует из области, которую называет сама страница.
    /// </summary>
    [Fact]
    public void A_wheel_only_row_is_hidden_in_the_global_view()
    {
        var options = new LiveOptions();
        var (binder, _) = Build(options);

        Assert.DoesNotContain(
            binder.Page(SettingsPage.Wheel, Global).SelectMany(g => g),
            d => d.Key == "WheelConfig:CellsInSeries");

        Assert.Contains(
            binder.Page(SettingsPage.Wheel, Sherman).SelectMany(g => g),
            d => d.Key == "WheelConfig:CellsInSeries");
    }

    private static (SettingsBinder Binder, LayeredSettings Settings) Build(LiveOptions options)
    {
        var descriptors = Describe(options);
        var settings = new LayeredSettings(
            new InMemorySettingsStore(), SettingsBinder.FactoryDefaults(descriptors));
        return (new SettingsBinder(settings, descriptors), settings);
    }

    private static IReadOnlyList<SettingDescriptor> Describe(LiveOptions options) =>
    [
        new SettingDescriptor
        {
            Key = "Alerts:AlarmsEnabled",
            Kind = SettingKind.Toggle,
            Page = SettingsPage.Warnings,
            SectionKey = "SectionAlarms",
            LabelKey = "AlarmsEnabled",
            Current = () => options.AlarmsEnabled.ToString(),
            Apply = text => options.AlarmsEnabled = bool.Parse(text),
        },
        new SettingDescriptor
        {
            Key = "Alerts:PwmWarning",
            Kind = SettingKind.Number,
            Page = SettingsPage.Warnings,
            SectionKey = "SectionAlarms",
            LabelKey = "PwmWarning",
            IsVisible = () => options.AlarmsEnabled,
            Current = () => options.PwmWarning.ToString(CultureInfo.InvariantCulture),
            Apply = text => options.PwmWarning = int.Parse(text, CultureInfo.InvariantCulture),
            AfterEdit = () => options.Edits++,
        },
        new SettingDescriptor
        {
            Key = "AlertSignals:Sound",
            Kind = SettingKind.Toggle,
            Page = SettingsPage.Warnings,
            SectionKey = "SectionChannels",
            LabelKey = "Sound",
            GlobalOnly = true,
            Current = () => options.Sound.ToString(),
            Apply = text => options.Sound = bool.Parse(text),
        },
        new SettingDescriptor
        {
            Key = "WheelConfig:CellsInSeries",
            Kind = SettingKind.Number,
            Page = SettingsPage.Wheel,
            SectionKey = "SectionBattery",
            LabelKey = "CellsInSeries",
            WheelOnly = true,
            Current = () => options.CellsInSeries.ToString(CultureInfo.InvariantCulture),
            Apply = text => options.CellsInSeries = int.Parse(text, CultureInfo.InvariantCulture),
        },
        new SettingDescriptor
        {
            Key = "WheelConfig:HwPwm",
            Kind = SettingKind.Toggle,
            Page = SettingsPage.Wheel,
            SectionKey = "SectionReported",
            LabelKey = "HwPwm",
            ReportedByWheel = true,
            Current = () => options.HwPwm.ToString(),
            Apply = text => options.HwPwm = bool.Parse(text),
        },
    ];
}
