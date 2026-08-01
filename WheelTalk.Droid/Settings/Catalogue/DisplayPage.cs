using WheelTalk.Core.Settings;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Droid.Configuration;

namespace WheelTalk.Droid.Settings.Catalogue;

/// <summary>
/// Descriptors of <see cref="SettingsPage.Display"/> — split out of <c>SettingsCatalogue.Build</c>
/// (plan 14, А2.1), body moved as-is.
/// </summary>
internal static class DisplayPage
{
    public static IReadOnlyList<SettingDescriptor> Build(DashboardOptions dashboard, ScreenOptions screen)
    {
        return
        [
            // ---- Display -----------------------------------------------------------------
            //
            // Ручки приборной панели, отобранные на стенде. Их там больше: часть принадлежит
            // вариантам, которые остались историей, и в приложении описывать нечего — того, на что
            // они влияют, на экране нет.
            //
            // Порогов раскраски 78/92 здесь тоже нет, хотя на стенде они были ручками. В
            // приложении цвет ленты берёт `Alerts:PwmWarning` и `Alerts:PwmCritical`: две
            // независимые пары порогов расходились бы на глазах — лента желтела бы там, где сигнал
            // молчит. Ниже осталась только третья граница, у которой пары в тревогах нет.
            // Серая, а не обрезанная: обрыв разметки читался бы как поломка прибора, а цветом
            // сказано то же самое — смотреть там не на что.
            new()
            {
                Key = "Display:PwmGreyBelow",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionPwmTape",
                LabelKey = "SettingPwmGreyBelow",
                HintKey = "SettingPwmGreyBelowHint",
                UnitKey = "UnitPercent",
                GlobalOnly = true,
                Maximum = 100,
                Current = () => SettingsCatalogue.Fixed(dashboard.PwmGreyBelow, 0),
                Apply = text => dashboard.PwmGreyBelow = SettingsCatalogue.ParseNumber(text),
            },
            // Приём авиационных лент: третья граница помимо порогов тревоги. Те говорят
            // «сигналить», эта — «сюда уже не заезжают».
            new()
            {
                Key = "Display:BarberPolePwm",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionPwmTape",
                LabelKey = "SettingBarberPolePwm",
                HintKey = "SettingBarberPolePwmHint",
                UnitKey = "UnitPercent",
                GlobalOnly = true,
                Minimum = 50,
                Maximum = 150,
                Current = () => SettingsCatalogue.Fixed(dashboard.BarberPolePwm, 0),
                Apply = text => dashboard.BarberPolePwm = SettingsCatalogue.ParseNumber(text),
            },
            new()
            {
                Key = "Display:PwmDpPerUnit",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionPwmTape",
                LabelKey = "SettingPwmDpPerUnit",
                HintKey = "SettingPwmDpPerUnitHint",
                GlobalOnly = true,
                Advanced = true,
                Minimum = 4,
                Maximum = 30,
                Current = () => SettingsCatalogue.Fixed(dashboard.PwmDpPerUnit, 0),
                Apply = text => dashboard.PwmDpPerUnit = SettingsCatalogue.ParseNumber(text),
            },

            // Всё на этой шкале — в вольтах пакета, тех самых, что видны на её делениях, и всё
            // задаётся на колесо. На банку было бы одним числом на оба колеса, но за это пришлось
            // бы знать число банок, а колесо его не сообщает; в приложении у каждого колеса свой
            // слой, и угадывать незачем.
            new()
            {
                Key = "Display:SagWindowVolts",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionVoltageTape",
                LabelKey = "SettingSagWindow",
                HintKey = "SettingSagWindowHint",
                UnitKey = "UnitVolts",
                Minimum = 2,
                Maximum = 40,
                Current = () => SettingsCatalogue.Fixed(dashboard.SagWindowVolts, 0),
                Apply = text => dashboard.SagWindowVolts = SettingsCatalogue.ParseNumber(text),
            },
            new()
            {
                Key = "Display:WarnVolts",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionVoltageTape",
                LabelKey = "SettingWarnVolts",
                HintKey = "SettingWarnVoltsHint",
                UnitKey = "UnitVolts",
                Maximum = 160,
                Step = 0.5,
                Decimals = 1,
                Current = () => SettingsCatalogue.Fixed(dashboard.WarnVolts, 1),
                Apply = text => dashboard.WarnVolts = SettingsCatalogue.ParseNumber(text),
            },
            new()
            {
                Key = "Display:DangerVolts",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionVoltageTape",
                LabelKey = "SettingDangerVolts",
                UnitKey = "UnitVolts",
                Maximum = 160,
                Step = 0.5,
                Decimals = 1,
                Current = () => SettingsCatalogue.Fixed(dashboard.DangerVolts, 1),
                Apply = text => dashboard.DangerVolts = SettingsCatalogue.ParseNumber(text),
            },
            // Абсолютный пол — четвёртая зона поверх трёх относительных: те работают каждый
            // разгон, эта всплывает раз в поездку, в конце, и тогда должна перебивать всё. Ноль
            // выключает потому, что неверно угаданный пол красит всю шкалу на всю поездку.
            new()
            {
                Key = "Display:EmptyVolts",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionVoltageTape",
                LabelKey = "SettingEmptyVolts",
                HintKey = "SettingEmptyVoltsHint",
                UnitKey = "UnitVolts",
                Maximum = 160,
                Current = () => SettingsCatalogue.Fixed(dashboard.EmptyVolts, 0),
                Apply = text => dashboard.EmptyVolts = SettingsCatalogue.ParseNumber(text),
            },
            new()
            {
                Key = "Screen:KeepOn",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Display,
                SectionKey = "SectionScreen",
                LabelKey = "SettingKeepScreenOn",
                HintKey = "SettingKeepScreenOnHint",
                GlobalOnly = true,
                Transient = true,
                Current = () => screen.KeepOn.ToString(),
                Apply = text => screen.KeepOn = SettingsCatalogue.ParseBool(text),
            },
            new()
            {
                Key = "Screen:ShowOverLock",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Display,
                SectionKey = "SectionScreen",
                LabelKey = "SettingShowOverLock",
                HintKey = "SettingShowOverLockHint",
                GlobalOnly = true,
                Transient = true,
                Current = () => screen.ShowOverLock.ToString(),
                Apply = text => screen.ShowOverLock = SettingsCatalogue.ParseBool(text),
            },

            new()
            {
                Key = "Display:SagAutoScale",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Display,
                SectionKey = "SectionVoltageTape",
                LabelKey = "SettingSagAutoScale",
                HintKey = "SettingSagAutoScaleHint",
                GlobalOnly = true,
                Advanced = true,
                Current = () => dashboard.SagAutoScale.ToString(),
                Apply = text => dashboard.SagAutoScale = SettingsCatalogue.ParseBool(text),
            },

            new()
            {
                Key = "Display:ShowTrend",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Display,
                SectionKey = "SectionTechniques",
                LabelKey = "SettingShowTrend",
                HintKey = "SettingShowTrendHint",
                GlobalOnly = true,
                Current = () => dashboard.ShowTrend.ToString(),
                Apply = text => dashboard.ShowTrend = SettingsCatalogue.ParseBool(text),
            },
            new()
            {
                Key = "Display:TrendSeconds",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionTechniques",
                LabelKey = "SettingTrendSeconds",
                HintKey = "SettingTrendSecondsHint",
                UnitKey = "UnitSeconds",
                GlobalOnly = true,
                // Стрелки нет — нечего и загадывать.
                IsVisible = () => dashboard.ShowTrend,
                Minimum = 0.5,
                Maximum = 6,
                Step = 0.5,
                Decimals = 1,
                Current = () => SettingsCatalogue.Fixed(dashboard.TrendSeconds, 1),
                Apply = text => dashboard.TrendSeconds = SettingsCatalogue.ParseNumber(text),
            },
            new()
            {
                Key = "Display:ShowBug",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Display,
                SectionKey = "SectionTechniques",
                LabelKey = "SettingShowBug",
                HintKey = "SettingShowBugHint",
                GlobalOnly = true,
                Current = () => dashboard.ShowBug.ToString(),
                Apply = text => dashboard.ShowBug = SettingsCatalogue.ParseBool(text),
            },
            new()
            {
                Key = "Display:ShowBarberPole",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Display,
                SectionKey = "SectionTechniques",
                LabelKey = "SettingShowBarberPole",
                GlobalOnly = true,
                Current = () => dashboard.ShowBarberPole.ToString(),
                Apply = text => dashboard.ShowBarberPole = SettingsCatalogue.ParseBool(text),
            },

            new()
            {
                Key = "Display:HideTenthsAbove",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionDigits",
                LabelKey = "SettingHideTenths",
                HintKey = "SettingHideTenthsHint",
                UnitKey = "UnitKmh",
                GlobalOnly = true,
                Maximum = 80,
                Current = () => SettingsCatalogue.Fixed(dashboard.HideTenthsAbove, 0),
                Apply = text => dashboard.HideTenthsAbove = SettingsCatalogue.ParseNumber(text),
            },
            new()
            {
                Key = "Display:HideExtrasAbove",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionDigits",
                LabelKey = "SettingHideExtras",
                HintKey = "SettingHideExtrasHint",
                UnitKey = "UnitKmh",
                GlobalOnly = true,
                Maximum = 80,
                Current = () => SettingsCatalogue.Fixed(dashboard.HideExtrasAbove, 0),
                Apply = text => dashboard.HideExtrasAbove = SettingsCatalogue.ParseNumber(text),
            },

            new()
            {
                Key = "Display:ShowAlertBorder",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Display,
                SectionKey = "SectionAlertBars",
                // Полосы, а не рамка по кругу: рамка накрывала бы цветные полосы шкал и гасила оба
                // прибора ровно тогда, когда они нужнее всего (разбор — AGENTS.md, отклонения).
                LabelKey = "SettingShowAlertBars",
                GlobalOnly = true,
                Current = () => dashboard.ShowAlertBorder.ToString(),
                Apply = text => dashboard.ShowAlertBorder = SettingsCatalogue.ParseBool(text),
            },
            // Вторая фаза прозрачная, а не тёмная: приборы под полосами видны половину времени.
            // Потолок в пять — с запасом выше трёх вспышек в секунду, верхней границы WCAG.
            new()
            {
                Key = "Display:BlinkHz",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionAlertBars",
                LabelKey = "SettingBlinkHz",
                HintKey = "SettingBlinkHzHint",
                UnitKey = "UnitHertz",
                GlobalOnly = true,
                IsVisible = () => dashboard.ShowAlertBorder,
                Minimum = 1,
                Maximum = 5,
                Current = () => SettingsCatalogue.Fixed(dashboard.BlinkHz, 0),
                Apply = text => dashboard.BlinkHz = SettingsCatalogue.ParseNumber(text),
            },

            // «Ванг» (синий / оранжевый / киноварь) различима при дейтеранопии, а это порядка 8 %
            // мужчин; поэтому она заводская, а цвета оригинала — вторым пунктом.
            new()
            {
                Key = "Display:Palette",
                Kind = SettingKind.Choice,
                Page = SettingsPage.Display,
                SectionKey = "SectionLook",
                LabelKey = "SettingPalette",
                HintKey = "SettingPaletteHint",
                GlobalOnly = true,
                Choices = [.. DashboardPalette.All.Select(p => p.Name)],
                Current = () => dashboard.Palette.Name,
                Apply = text => dashboard.Palette =
                    DashboardPalette.All.FirstOrDefault(p => p.Name == text) ?? DashboardPalette.Wong,
            },
            new()
            {
                Key = "Display:Tilt",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionLook",
                LabelKey = "SettingTilt",
                HintKey = "SettingTiltHint",
                UnitKey = "UnitDegrees",
                GlobalOnly = true,
                Advanced = true,
                Minimum = -90,
                Maximum = 90,
                Step = 15,
                Current = () => SettingsCatalogue.Fixed(dashboard.Tilt, 0),
                Apply = text => dashboard.Tilt = SettingsCatalogue.ParseNumber(text),
            },

            // Сглаживания — в конце и оба «дополнительные»: их цена в задержке, и трогать их стоит,
            // только когда есть с чем сравнивать. Складываются, и второе гораздо сильнее.
            // Первое двигает только разметку и не быстрее, чем приходят кадры телеметрии; второе
            // фильтрует сами данные, а значит задерживает и цифру, и цвет, и стрелку тренда.
            new()
            {
                Key = "Display:TapeSmoothSeconds",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionMotion",
                LabelKey = "SettingTapeSmooth",
                HintKey = "SettingTapeSmoothHint",
                UnitKey = "UnitSeconds",
                GlobalOnly = true,
                Advanced = true,
                Maximum = 0.5,
                Step = 0.01,
                Decimals = 2,
                Current = () => SettingsCatalogue.Fixed(dashboard.TapeSmoothSeconds, 2),
                Apply = text => dashboard.TapeSmoothSeconds = SettingsCatalogue.ParseNumber(text),
            },
            new()
            {
                Key = "Display:SmoothingSeconds",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionMotion",
                LabelKey = "SettingPwmSmooth",
                HintKey = "SettingPwmSmoothHint",
                UnitKey = "UnitSeconds",
                GlobalOnly = true,
                Advanced = true,
                Maximum = 1,
                Step = 0.05,
                Decimals = 2,
                Current = () => SettingsCatalogue.Fixed(dashboard.SmoothingSeconds, 2),
                Apply = text => dashboard.SmoothingSeconds = SettingsCatalogue.ParseNumber(text),
            },
        ];
    }
}
