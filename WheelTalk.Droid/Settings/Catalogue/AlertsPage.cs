using WheelTalk.Core.Alerts;
using WheelTalk.Core.Settings;
using WheelTalk.Droid.Configuration;

namespace WheelTalk.Droid.Settings.Catalogue;

/// <summary>
/// Descriptors of <see cref="SettingsPage.Warnings"/> — split out of <c>SettingsCatalogue.Build</c>
/// (plan 14, А2.1), body moved as-is.
/// <para>
/// Форма сигнала и прослушивание отсюда уехали на <see cref="SettingsPage.Experimental"/> (план 28):
/// они придуманы нами и на дороге не проверены. Сам выключатель звука остался здесь — он в оригинале
/// был. Каскад от этого не пострадал: зависимые строки смотрят на <see cref="AlertSignalOptions.Sound"/>,
/// живой объект, а не на соседнюю строку экрана.
/// </para>
/// </summary>
internal static class AlertsPage
{
    public static IReadOnlyList<SettingDescriptor> Build(AlertOptions alerts, AlertSignalOptions channels)
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
                // Этими порогами живут не только сигналы: по ним же красится лента ШИМ и рисуются
                // полосы тревоги (план 30 §4.4) — второй половиной ответа заведует «Отображение».
                SeeAlso = ["Display:ShowAlertBorder"],
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
            // Ширина полос тревоги уехала на «Отображение», в секцию «Полосы тревоги» (план 30
            // §3.1, Д4): это размер полосы на экране, а не порог, при котором тревожатся. Ключ при
            // переезде не тронут — заданное человеком значение на месте.

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
                // Каким этот звук будет — выбирают на «Тестовых функциях»: форма сигнала придумана
                // нами и на дороге не проверена (план 28), а искать её будут здесь (план 30 §4.4).
                SeeAlso = ["AlertSignals:Wave"],
                Current = () => channels.Sound.ToString(),
                Apply = text => channels.Sound = SettingsCatalogue.ParseBool(text),
            },
            // Форма сигнала и прослушивание стояли здесь же, следом за выключателем звука; обе
            // уехали на страницу «Тестовые функции» (план 28) — вместе с ними уехало и гашение
            // ползунка при уходе со страницы.
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
            // Выключено по умолчанию и просит системное разрешение — единственная ручка здесь с
            // побочным эффектом на весь телефон, а не только на это приложение (решение владельца
            // 11.08.2026). Запрос разрешения — в SettingsCategoryActivity.Commit, по этому же ключу.
            new()
            {
                Key = "AlertSignals:OverlayOtherApps",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Warnings,
                SectionKey = "SectionChannels",
                LabelKey = "SettingChannelOverlay",
                HintKey = "SettingChannelOverlayHint",
                GlobalOnly = true,
                Current = () => channels.OverlayOtherApps.ToString(),
                Apply = text => channels.OverlayOtherApps = SettingsCatalogue.ParseBool(text),
            },
        ];
    }
}
