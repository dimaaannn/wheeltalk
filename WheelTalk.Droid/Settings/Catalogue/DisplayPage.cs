using WheelTalk.Core.Alerts;
using WheelTalk.Core.Settings;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Main;

namespace WheelTalk.Droid.Settings.Catalogue;

/// <summary>
/// Descriptors of <see cref="SettingsPage.Display"/> — split out of <c>SettingsCatalogue.Build</c>
/// (plan 14, А2.1), body moved as-is.
/// <para>
/// Шкала на банку — выбор режима и пять порогов на ячейку — переехала на
/// <see cref="SettingsPage.Experimental"/> (план 28). Пакетные пороги остались здесь.
/// </para>
/// </summary>
internal static class DisplayPage
{
    public static IReadOnlyList<SettingDescriptor> Build(
        DashboardOptions dashboard, AlertOptions alerts, PanelVariants panels)
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
                // Своих порогов у цвета ленты нет — жёлтый и красный она берёт из «Предупреждений»
                // (план 30 §4.4): сказать об этом на экране больше нечем.
                SeeAlso = ["Alerts:PwmWarning"],
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
                SeeAlso = ["Alerts:PwmWarning"],
                Minimum = 50,
                Maximum = 150,
                Current = () => SettingsCatalogue.Fixed(dashboard.BarberPolePwm, 0),
                Apply = text => dashboard.BarberPolePwm = SettingsCatalogue.ParseNumber(text),
            },
            // Включатель штриховки стоит вплотную к её порогу (план 30 §3.1, Д5): раньше он жил в
            // «Поверх шкал», и выключенная штриховка оставляла свой порог висеть в другой секции.
            new()
            {
                Key = "Display:ShowBarberPole",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Display,
                SectionKey = "SectionPwmTape",
                LabelKey = "SettingShowBarberPole",
                GlobalOnly = true,
                Current = () => dashboard.ShowBarberPole.ToString(),
                Apply = text => dashboard.ShowBarberPole = SettingsCatalogue.ParseBool(text),
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
                // Признак «дополнительная» снят: секция смешанной быть не может, а он в ней и так
                // не работал — строка все эти месяцы рисовалась обычной (план 30, Д8).
                Minimum = 4,
                Maximum = 30,
                Current = () => SettingsCatalogue.Fixed(dashboard.PwmDpPerUnit, 0),
                Apply = text => dashboard.PwmDpPerUnit = SettingsCatalogue.ParseNumber(text),
            },

            // Выбор «чем меряет левая шкала» и пять порогов на ячейку уехали на страницу «Тестовые
            // функции» (план 28): шкала на банку придумана нами и на дороге не проверена. Пакетные
            // пороги ниже остались — с ними ездят, и метка о них соврала бы.

            // Всё, что ниже, — в вольтах пакета, тех самых, что видны на делениях в заводском
            // режиме, и всё задаётся на колесо. Окно шкалы общее для всех трёх режимов: на ячейку
            // оно пересчитывается делителем, а не задаётся вторым числом.
            new()
            {
                Key = "Display:SagWindowVolts",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionVoltageTape",
                LabelKey = "SettingSagWindow",
                HintKey = "SettingSagWindowHint",
                UnitKey = "UnitVolts",
                // Пороги этой секции действуют, только пока шкала меряет пакетом: сам режим и
                // пороги на ячейку живут на «Тестовых функциях» (план 30 §4.4).
                SeeAlso = ["Display:VoltageScale"],
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
                // 250 — запас над полным зарядом 50S-пака (около 210 В у EX30, V14 и им подобных).
                Maximum = 250,
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
                // 250 — тот же запас, что у SettingWarnVolts выше: обе ручки одной шкалы.
                Maximum = 250,
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
                // 250 — тот же запас, что у SettingWarnVolts выше: обе ручки одной шкалы.
                Maximum = 250,
                Current = () => SettingsCatalogue.Fixed(dashboard.EmptyVolts, 0),
                Apply = text => dashboard.EmptyVolts = SettingsCatalogue.ParseNumber(text),
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
                // Признак «дополнительная» снят по той же причине, что у «Плотности шкалы»: в
                // смешанной секции он терялся молча (план 30, Д8).

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
                // Сглаживание данных задерживает и стрелку: считается она по тем же числам.
                SeeAlso = ["Display:SmoothingSeconds"],
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
                Key = "Display:HideTenthsAbove",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionDigits",
                LabelKey = "SettingHideTenths",
                HintKey = "SettingHideTenthsHint",
                UnitKey = "UnitKmh",
                GlobalOnly = true,
                // 150 — с запасом выше крейсерской скорости самых быстрых колёс 2026 года, чтобы
                // порог не упирался раньше самой скорости.
                Maximum = 150,
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
                // 150 — тот же потолок, что у SettingHideTenths выше.
                Maximum = 150,
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
                // Когда полосы загорятся — решают пороги тревог, своих у них нет (план 30 §4.4).
                SeeAlso = ["Alerts:PwmWarning"],
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
                // Ноль — не «сломанная настройка», а «не моргать»: полоса тогда горит ровно, и это
                // выбор человека, а не отсутствие сигнала. Тем же нулём выключаются пороги в
                // «Предупреждениях» — соглашение оригинала, «ноль выключает».
                Minimum = 0,
                Maximum = 5,
                Current = () => SettingsCatalogue.Fixed(dashboard.BlinkHz, 0),
                Apply = text => dashboard.BlinkHz = SettingsCatalogue.ParseNumber(text),
            },
            // Приехала с «Предупреждений» (план 30 §3.1, Д4): ширина полосы — это размер на экране,
            // а не порог, при котором тревожатся; здесь она стоит рядом со своим включателем и
            // частотой моргания. Ключ прежний — заданное человеком значение на месте. Живёт
            // по-прежнему в AlertOptions: объект тот же, что читает движок тревог, и переезд строки
            // его не касается.
            new()
            {
                Key = "Alerts:MaxBorderCoverage",
                Kind = SettingKind.Number,
                Page = SettingsPage.Display,
                SectionKey = "SectionAlertBars",
                LabelKey = "SettingBorderCoverage",
                HintKey = "SettingBorderCoverageHint",
                UnitKey = "UnitPercent",
                // Свойство экрана, на который смотрят, а не колеса, под которым едут.
                GlobalOnly = true,
                IsVisible = () => dashboard.ShowAlertBorder,
                Minimum = 1,
                Maximum = 20,
                Current = () => SettingsCatalogue.Fixed(alerts.MaxBorderCoverage * 100, 0),
                Apply = text => alerts.MaxBorderCoverage = SettingsCatalogue.ParseNumber(text) / 100,
            },

            // Вариант панели (план 17 §3). Выбор общий на приложение, не слоем колеса — решение
            // владельца 09.08.2026: это привычка человека, а не свойство колеса.
            //
            // Строка показывается, только когда вариантов больше одного: в Release вариант ровно
            // один, и строка с единственным пунктом нарушала бы правило плана 6 §0 — «настройка
            // обязана что-то менять». Это же условие снимает вопрос «что показать, когда выбирать
            // не из чего»: ничего.
            new()
            {
                Key = "Screen:PanelVariant",
                Kind = SettingKind.Choice,
                Page = SettingsPage.Display,
                SectionKey = "SectionLook",
                LabelKey = "SettingPanelVariant",
                HintKey = "SettingPanelVariantHint",
                GlobalOnly = true,
                IsVisible = () => panels.All.Count > 1,
                Choices = [.. panels.All.Select(variant => variant.Id)],
                ChoiceLabelKeys = [.. panels.All.Select(variant => variant.LabelKey)],
                Current = () => panels.CurrentId,
                // Экран пересобирается не здесь: панель живёт у MainActivity, и та берёт выбранный
                // вариант, когда возвращается на передний план. Настройке довольно записать выбор.
                Apply = id => panels.CurrentId = id,
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
                // Признак «дополнительная» снят: в секции «Оформление» он терялся (план 30, Д8).

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
