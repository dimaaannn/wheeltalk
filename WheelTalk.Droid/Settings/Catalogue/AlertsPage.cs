using WheelTalk.Core.Alerts;
using WheelTalk.Core.Settings;
using WheelTalk.Droid.Configuration;

namespace WheelTalk.Droid.Settings.Catalogue;

/// <summary>
/// Descriptors of <see cref="SettingsPage.Warnings"/> — split out of <c>SettingsCatalogue.Build</c>
/// (plan 14, А2.1), body moved as-is.
/// </summary>
internal static class AlertsPage
{
    public static IReadOnlyList<SettingDescriptor> Build(AlertOptions alerts, AlertSignalOptions channels,
        Action<double> previewAlarm)
    {
        return
        [
            // ---- Warnings ----------------------------------------------------------------
            new()
            {
                Key = "Alerts:PwmWarning",
                Kind = SettingKind.Number,
                Page = SettingsPage.Warnings,
                SectionKey = "SectionPwmAlarm",
                LabelKey = "SettingPwmWarning",
                HintKey = "SettingPwmWarningHint",
                UnitKey = "UnitPercent",
                Maximum = 99,
                Current = () => SettingsCatalogue.Fixed(alerts.PwmWarning, 0),
                Apply = text => alerts.PwmWarning = SettingsCatalogue.ParseNumber(text),
            },
            new()
            {
                Key = "Alerts:PwmCritical",
                Kind = SettingKind.Number,
                Page = SettingsPage.Warnings,
                SectionKey = "SectionPwmAlarm",
                LabelKey = "SettingPwmCritical",
                HintKey = "SettingPwmCriticalHint",
                UnitKey = "UnitPercent",
                Maximum = 99,
                Current = () => SettingsCatalogue.Fixed(alerts.PwmCritical, 0),
                Apply = text => alerts.PwmCritical = SettingsCatalogue.ParseNumber(text),
            },
            new()
            {
                Key = "Alerts:MaxBorderCoverage",
                Kind = SettingKind.Number,
                Page = SettingsPage.Warnings,
                SectionKey = "SectionPwmAlarm",
                LabelKey = "SettingBorderCoverage",
                HintKey = "SettingBorderCoverageHint",
                UnitKey = "UnitPercent",
                // Свойство экрана, на который смотрят, а не колеса, под которым едут.
                GlobalOnly = true,
                Minimum = 1,
                Maximum = 20,
                Current = () => SettingsCatalogue.Fixed(alerts.MaxBorderCoverage * 100, 0),
                Apply = text => alerts.MaxBorderCoverage = SettingsCatalogue.ParseNumber(text) / 100,
            },

            new()
            {
                Key = "Alerts:SpeedThreshold",
                Kind = SettingKind.Number,
                Page = SettingsPage.Warnings,
                SectionKey = "SectionSpeedWarning",
                LabelKey = "SettingSpeedThreshold",
                HintKey = "SettingSpeedThresholdHint",
                UnitKey = "UnitKmh",
                Maximum = 120,
                Current = () => SettingsCatalogue.Fixed(alerts.SpeedThreshold, 0),
                Apply = text => alerts.SpeedThreshold = SettingsCatalogue.ParseNumber(text),
            },
            new()
            {
                Key = "Alerts:SpeedRepeatInterval",
                Kind = SettingKind.Number,
                Page = SettingsPage.Warnings,
                SectionKey = "SectionSpeedWarning",
                LabelKey = "SettingSpeedRepeat",
                HintKey = "SettingSpeedRepeatHint",
                UnitKey = "UnitSeconds",
                // Nothing to repeat while the warning itself is off.
                IsVisible = () => alerts.SpeedThreshold > 0,
                Minimum = 1,
                Maximum = 60,
                Current = () => SettingsCatalogue.Fixed(alerts.SpeedRepeatInterval.TotalSeconds, 0),
                Apply = text => alerts.SpeedRepeatInterval = TimeSpan.FromSeconds(SettingsCatalogue.ParseNumber(text)),
            },
            new()
            {
                Key = "Alerts:SuppressSpeedWhilePwmAlert",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Warnings,
                SectionKey = "SectionSpeedWarning",
                LabelKey = "SettingSuppressSpeed",
                HintKey = "SettingSuppressSpeedHint",
                IsVisible = () => alerts.SpeedThreshold > 0,
                Current = () => alerts.SuppressSpeedWhilePwmAlert.ToString(),
                Apply = text => alerts.SuppressSpeedWhilePwmAlert = SettingsCatalogue.ParseBool(text),
            },

            // Чем предупреждать — свойство телефона и райдера, а не колеса: беззвучный режим,
            // шлем и карман одни и те же, на чём бы человек ни ехал. Отсюда GlobalOnly у всех трёх.
            new()
            {
                Key = "AlertSignals:Sound",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Warnings,
                SectionKey = "SectionChannels",
                LabelKey = "SettingChannelSound",
                GlobalOnly = true,
                Current = () => channels.Sound.ToString(),
                Apply = text => channels.Sound = SettingsCatalogue.ParseBool(text),
            },
            // Оба варианта отобраны владельцем на слух из восьми (план 26); заводской — тот, что он
            // назвал первым. Выбирается ушами, поэтому подписи называют рисунок, а не частоты.
            new()
            {
                Key = "AlertSignals:Wave",
                Kind = SettingKind.Choice,
                Page = SettingsPage.Warnings,
                SectionKey = "SectionChannels",
                LabelKey = "SettingAlarmWave",
                GlobalOnly = true,
                // Выбирать нечего, пока звук выключен.
                IsVisible = () => channels.Sound,
                Choices = [nameof(AlarmWave.TwoToneStack), nameof(AlarmWave.Stack)],
                ChoiceLabelKeys = ["SettingAlarmWaveTwoTone", "SettingAlarmWaveStack"],
                Current = () => channels.Wave.ToString(),
                Apply = text => channels.Wave = Enum.TryParse(text, out AlarmWave wave) ? wave : AlarmWave.TwoToneStack,
            },
            // Прослушивание, а не настройка: хранить нечего, а звук, переживший страницу, играл бы в
            // кармане. Ползунок ведёт ту же интенсивность, что приходит от движка тревог, поэтому
            // слышно и редкий писк у порога, и сплошной сигнал у предела.
            new()
            {
                Key = "AlertSignals:Preview",
                Kind = SettingKind.Slider,
                Page = SettingsPage.Warnings,
                SectionKey = "SectionChannels",
                LabelKey = "SettingAlarmPreview",
                HintKey = "SettingAlarmPreviewHint",
                Transient = true,
                IsVisible = () => channels.Sound,
                UnitKey = "UnitPercent",
                Maximum = 100,
                Current = () => "0",
                Apply = text => previewAlarm(SettingsCatalogue.ParseNumber(text) / 100),
            },
            new()
            {
                Key = "AlertSignals:Vibration",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Warnings,
                SectionKey = "SectionChannels",
                LabelKey = "SettingChannelVibration",
                GlobalOnly = true,
                Current = () => channels.Vibration.ToString(),
                Apply = text => channels.Vibration = SettingsCatalogue.ParseBool(text),
            },
            new()
            {
                Key = "AlertSignals:Torch",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Warnings,
                SectionKey = "SectionChannels",
                LabelKey = "SettingChannelTorch",
                HintKey = "SettingChannelTorchHint",
                GlobalOnly = true,
                Current = () => channels.Torch.ToString(),
                Apply = text => channels.Torch = SettingsCatalogue.ParseBool(text),
            },
        ];
    }
}
