using WheelTalk.Core.Settings;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Diagnostics;

/// <summary>
/// Отличия настроек от заводских — раздел отчёта диагностики (план 11 §4.2). Проверяется то, ради
/// чего он заведён: на чужом телефоне забытый отладочный порог должен быть виден с первого взгляда,
/// а пароль колеса в файл, который человек отдаёт системным диалогом, попасть не должен никогда.
/// </summary>
public class ChangedSettingsTests
{
    private const string Sherman = "88:25:83:F5:75:4A";

    [Fact]
    public void A_phone_with_nothing_touched_says_so()
    {
        var (binder, _) = Build();

        Assert.Empty(ChangedSettings.Lines(binder));
        Assert.Equal("настройки: всё заводское", ChangedSettings.Describe(binder));
    }

    [Fact]
    public void Only_what_differs_from_the_factory_gets_into_the_report()
    {
        var (binder, settings) = Build();
        settings.Scope = Sherman;
        binder.Set(binder.Descriptors.First(d => d.Key == "Alerts:PwmWarning"), "70", Sherman);

        var lines = ChangedSettings.Lines(binder);

        Assert.Equal(["Alerts:PwmWarning = 70 (колесо)"], lines);
    }

    /// <summary>Слой назван словом: разбор начинается с вопроса «это у всех колёс или у одного».</summary>
    [Fact]
    public void The_layer_a_value_came_from_is_named()
    {
        var (binder, settings) = Build();
        settings.Scope = Sherman;
        binder.Set(binder.Descriptors.First(d => d.Key == "AlertSignals:Sound"), "False", Sherman);

        Assert.Equal(["AlertSignals:Sound = False (общее)"], ChangedSettings.Lines(binder));
    }

    /// <summary>
    /// Пароль колеса — тот случай, ради которого заведён признак <see cref="SettingDescriptor.Secret"/>:
    /// в отчёте виден факт «задан», и ни одной цифры.
    /// </summary>
    [Fact]
    public void A_secret_setting_shows_that_it_is_set_and_never_what_it_is()
    {
        var (binder, settings) = Build();
        settings.Scope = Sherman;
        binder.Set(binder.Descriptors.First(d => d.Key == "WheelConfig:InMotionPassword"), "123456", Sherman);

        var lines = ChangedSettings.Lines(binder);

        Assert.Equal(["WheelConfig:InMotionPassword = задан (колесо)"], lines);
        Assert.DoesNotContain("123456", ChangedSettings.Describe(binder));
    }

    /// <summary>
    /// Сообщённое колесом, сеансовое, действия и справки в отличиях не участвуют: это не выбор
    /// человека, и за таким шумом потерялась бы единственная важная строка.
    /// </summary>
    [Fact]
    public void What_is_not_a_human_choice_stays_out()
    {
        var options = new Live();
        var (binder, settings) = Build(options);
        settings.Scope = Sherman;

        // Декодер сказал своё, страница пошумела ползунком — ни то, ни другое настройкой человека
        // не является.
        options.HwPwm = true;
        binder.Set(binder.Descriptors.First(d => d.Key == "AlertSignals:Preview"), "40", Sherman);

        Assert.Empty(ChangedSettings.Lines(binder));
    }

    private sealed class Live
    {
        public int PwmWarning { get; set; } = 80;
        public bool Sound { get; set; } = true;
        public bool HwPwm { get; set; }
        public string Password { get; set; } = "";
        public double Preview { get; set; }
    }

    private static (SettingsBinder Binder, LayeredSettings Settings) Build(Live? live = null)
    {
        var options = live ?? new Live();
        var descriptors = Describe(options);
        var settings = new LayeredSettings(
            new InMemorySettingsStore(), SettingsBinder.FactoryDefaults(descriptors));
        return (new SettingsBinder(settings, descriptors), settings);
    }

    private static IReadOnlyList<SettingDescriptor> Describe(Live options) =>
    [
        new SettingDescriptor
        {
            Key = "Alerts:PwmWarning",
            Kind = SettingKind.Number,
            Page = SettingsPage.Warnings,
            SectionKey = "SectionAlarms",
            LabelKey = "PwmWarning",
            Current = () => options.PwmWarning.ToString(),
            Apply = text => options.PwmWarning = int.Parse(text),
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
            Key = "WheelConfig:InMotionPassword",
            Kind = SettingKind.Text,
            Page = SettingsPage.Wheel,
            SectionKey = "SectionInMotion",
            LabelKey = "Password",
            Secret = true,
            Current = () => options.Password,
            Apply = text => options.Password = text,
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
        new SettingDescriptor
        {
            Key = "AlertSignals:Preview",
            Kind = SettingKind.Slider,
            Page = SettingsPage.Experimental,
            SectionKey = "SectionAlarmSignal",
            LabelKey = "Preview",
            Transient = true,
            Current = () => "0",
            Apply = text => options.Preview = double.Parse(text),
        },
    ];
}
