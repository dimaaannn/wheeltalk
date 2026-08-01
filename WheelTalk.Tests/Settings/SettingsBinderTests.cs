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

    /// <summary>Стоит вместо AppWheelConfig: важно, что это один и тот же экземпляр от начала до конца.</summary>
    private sealed class LiveOptions
    {
        public int PwmWarning { get; set; } = 80;
        public bool HwPwm { get; set; }
        public bool AlarmsEnabled { get; set; } = true;
        public bool Sound { get; set; } = true;
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

        binder.Set(descriptor, "70");

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
        binder.Set(descriptor, "75");
        Assert.Equal(75, options.PwmWarning);

        settings.Scope = MTen3;
        Assert.Equal(80, options.PwmWarning);   // своего значения нет — вернулось заводское

        binder.Set(descriptor, "85");
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

        binder.Set(reported, "True");
        Assert.Null(settings.Get("WheelConfig:HwPwm").Value);

        // Декодер сообщил своё — перечитывание слоёв не должно это затирать.
        options.HwPwm = true;
        settings.Scope = Sherman;

        Assert.True(options.HwPwm);
        Assert.Equal("True", binder.Read(reported).Value);
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

        binder.Set(sound, "False");

        Assert.Equal(SettingOrigin.Global, binder.Read(sound).Origin);
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
        binder.Set(descriptor, "70");

        binder.ClearGlobal(descriptor);

        Assert.Equal(80, options.PwmWarning);
        Assert.Equal(SettingOrigin.Factory, binder.Read(descriptor).Origin);
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
            binder.Page(SettingsPage.Warnings).SelectMany(g => g),
            d => d.Key == "Alerts:PwmWarning");

        options.AlarmsEnabled = false;

        Assert.DoesNotContain(
            binder.Page(SettingsPage.Warnings).SelectMany(g => g),
            d => d.Key == "Alerts:PwmWarning");
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
