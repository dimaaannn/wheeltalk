using System.Globalization;
using System.Text;
using WheelTalk.Core.Alerts;
using WheelTalk.Core.Battery;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Settings;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Resources.Strings;

namespace WheelTalk.Droid.Settings.Catalogue;

/// <summary>
/// Descriptors of <see cref="SettingsPage.Experimental"/> — страница не по теме, а по зрелости
/// (план 28): наши доработки, которые ещё не были на дороге. Портированное 1:1 из WheelLog сюда не
/// попадает никогда — оно обкатано тысячами райдеров, и метка о нём соврала бы.
/// <para>
/// Страница <b>только помечает</b>. Ничего не выключено и не урезано: строки работают ровно так же,
/// как работали на своих прежних местах, — иначе это была бы система флагов, а её никто не заказывал.
/// </para>
/// <para>
/// Настройка живёт в одном месте за раз: переезд сюда — это смена <see cref="SettingDescriptor.Page"/>
/// у описания, а не второй экземпляр строки. Обратный переезд, когда доработка отъездит без
/// нареканий, тоже бесплатен: хранилище знает настройку по <see cref="SettingDescriptor.Key"/>, и
/// заданное человеком значение переживает переезд, не заметив его. Поэтому ни один ключ здесь не
/// переименован при переносе — переименованный ключ тихо вернул бы всем заводское значение.
/// </para>
/// </summary>
internal static class ExperimentalPage
{
    /// <summary>
    /// Ряд ячеек. Константой потому, что в него пишет кнопка «рассчитать» через
    /// <c>SettingsBinder.Set(key, value)</c>, а ключ, названный в двух файлах порознь, однажды
    /// переименуют наполовину. Значение то же, каким было на странице колеса, — переезд ключа не
    /// касается.
    /// </summary>
    public const string CellsKey = "WheelConfig:CellsInSeries";

    public static IReadOnlyList<SettingDescriptor> Build(
        AppWheelConfig wheel,
        DashboardOptions dashboard,
        AlertSignalOptions channels,
        Action<double> previewAlarm,
        Func<TelemetrySnapshot?> lastFrame,
        Action<int> saveCells,
        Func<bool> wheelScopeChosen)
    {
        // Кадр, по которому считала кнопка «рассчитать», — чтобы отчитаться ровно им. Кадры идут
        // по нескольку в секунду, и взять напряжение заново значило бы назвать вольт на банку от
        // одного кадра, а ряд — от другого.
        TelemetrySnapshot? calculatedFrom = null;

        return
        [
            // ---- Сигнал тревоги (план 26) ------------------------------------------------
            //
            // Сюда уехала только форма сигнала и прослушивание — то, что придумано нами. Сам канал
            // звука («предупреждать звуком») остался на странице тревог: он в оригинале был и на
            // дороге бывал, и метка о нём соврала бы.
            //
            // Оба варианта отобраны владельцем на слух из восьми; заводской — тот, что он назвал
            // первым. Выбирается ушами, поэтому подписи называют рисунок, а не частоты.
            new()
            {
                Key = "AlertSignals:Wave",
                Kind = SettingKind.Choice,
                Page = SettingsPage.Experimental,
                SectionKey = "SectionAlarmSignal",
                LabelKey = "SettingAlarmWave",
                GlobalOnly = true,

                // Каскад пережил переезд: условие смотрит на живой AlertSignalOptions, а не на
                // соседнюю строку экрана, — и продолжает работать, хотя выключатель звука остался
                // страницей выше. Выбирать нечего, пока звук выключен.
                IsVisible = () => channels.Sound,
                Choices = [nameof(AlarmWave.TwoToneStack), nameof(AlarmWave.Stack)],
                ChoiceLabelKeys = ["SettingAlarmWaveTwoTone", "SettingAlarmWaveStack"],
                Current = () => channels.Wave.ToString(),
                Apply = text => channels.Wave = Enum.TryParse(text, out AlarmWave wave) ? wave : AlarmWave.TwoToneStack,
            },
            // Прослушивание, а не настройка: хранить нечего, а звук, переживший страницу, играл бы в
            // кармане. Ползунок ведёт ту же интенсивность, что приходит от движка тревог, поэтому
            // слышно и редкий писк у порога, и сплошной сигнал у предела.
            //
            // Гашение при уходе и при перестроении держит SettingsCategoryActivity.Silence — оно
            // ищет ползунки той страницы, что открыта, поэтому переезд его не обошёл: ползунок и
            // страница уехали вместе.
            new()
            {
                Key = "AlertSignals:Preview",
                Kind = SettingKind.Slider,
                Page = SettingsPage.Experimental,
                SectionKey = "SectionAlarmSignal",
                LabelKey = "SettingAlarmPreview",
                HintKey = "SettingAlarmPreviewHint",
                Transient = true,
                IsVisible = () => channels.Sound,
                UnitKey = "UnitPercent",
                Maximum = 100,
                Current = () => "0",
                Apply = text => previewAlarm(SettingsCatalogue.ParseNumber(text) / 100),
            },

            // ---- Вольт на банку (план 27 §27.4) ------------------------------------------
            //
            // Ряд ячеек — верхняя ступень каскада: задан человеком — бьёт и умный BMS, и знание
            // протокола. Ноль означает «не задано», как принято у нас и в оригинале.
            new()
            {
                Key = CellsKey,
                Kind = SettingKind.Number,
                Page = SettingsPage.Experimental,
                SectionKey = "SectionCellVoltage",
                LabelKey = "SettingCellsInSeries",
                HintKey = "SettingCellsInSeriesHint",
                UnitKey = "UnitCells",
                // Число принадлежит колесу, а не приложению: 20S у одного и 16S у другого — не
                // разногласие, а два разных колеса. Общего значения у него не бывает вовсе, поэтому
                // и в общей области строка не показывается: писать её там некуда.
                WheelOnly = true,
                IsVisible = wheelScopeChosen,
                Maximum = 60,
                Current = () => SettingsCatalogue.Whole(wheel.CellsInSeries),
                Apply = text => wheel.CellsInSeries = (int)SettingsCatalogue.ParseNumber(text),

                // Отменять заданное человеком мы не вправе — он знает своё колесо, — но сказать,
                // что из его числа выходит 21 В на банку, обязаны: ошибку «4 вместо 20» иначе не
                // видно. Пока напряжения не было, пугать нечем, и предупреждения нет.
                Warning = () => ImplausibleCellsNote(wheel.CellsInSeries, lastFrame()?.VoltageV ?? 0),
            },
            new()
            {
                Key = "WheelConfig:CellsFromCascade",
                Kind = SettingKind.Action,
                Page = SettingsPage.Experimental,
                SectionKey = "SectionCellVoltage",
                LabelKey = "SettingCellsCalculate",
                HintKey = "SettingCellsCalculateHint",
                ActionLabelKey = "SettingCellsCalculateAction",
                IsVisible = wheelScopeChosen,
                Current = () => "",

                // Догадка становится решением человека: он видит число в соседней строке и либо
                // оставляет его, либо правит. Считать не по чему — сказать об этом, а не записать
                // ноль: ноль здесь значит «не задано», и молчаливая запись отменила бы настройку.
                //
                // Берётся ответ **без верхней ступени**: с ней кнопка возвращала бы человеку его же
                // число — то есть была бы бесполезна ровно тогда, когда нужна. «Рассчитать» значит
                // «что сказало бы приложение, не скажи я ему».
                Apply = _ =>
                {
                    var frame = lastFrame();
                    var cells = frame?.AutoPackCells ?? CellCount.Unknown;
                    if (!cells.IsKnown) throw new InvalidOperationException(AppStrings.SettingCellsNoData);

                    calculatedFrom = frame;
                    saveCells(cells.Cells);
                },

                // Молча подставленное число некому проверить, а проверить его обязан человек —
                // требование владельца. Отчёт даёт ему для этого вольт на банку: 3,9 правдоподобно,
                // 2,7 значит, что ряд назван вдвое больше нужного.
                Report = () => CellsReport(calculatedFrom),

                // До нажатия — то же условие, но лишь когда оно и вправду в силе: у колеса, чей ряд
                // называет протокол, заряд ни при чём, и пугать им там значило бы приучить
                // пролистывать предупреждения.
                Warning = () => lastFrame()?.AutoPackCells.Source == CellCountSource.VoltageGuess
                    ? AppStrings.SettingCellsGuessCaveat
                    : null,
            },

            // ---- Шкала на ячейку ---------------------------------------------------------
            //
            // Чем меряет левая шкала. Заводское — вольты пакета, и сама она на банку не
            // переключается никогда: пункт выбирает человек (план 27 §27.4).
            //
            // Пунктов два, и автоматического среди них нет намеренно (решение владельца): ряд у
            // колеса не меняется, и решать каждый кадр то, что решается однажды, незачем. Догадка
            // живёт за кнопкой «рассчитать» выше — она записывает ответ в настройку, а лента делит
            // на записанное. Не задано — лента возвращается к вольтам пакета.
            new()
            {
                Key = "Display:VoltageScale",
                Kind = SettingKind.Choice,
                Page = SettingsPage.Experimental,
                SectionKey = "SectionCellVoltage",
                LabelKey = "SettingVoltageScale",
                HintKey = "SettingVoltageScaleHint",
                Choices = [nameof(VoltageScaleMode.Pack), nameof(VoltageScaleMode.Cells)],
                ChoiceLabelKeys = ["SettingVoltageScalePack", "SettingVoltageScaleCells"],
                Current = () => dashboard.VoltageScale.ToString(),
                Apply = text => dashboard.VoltageScale = Enum.TryParse(text, out VoltageScaleMode mode)
                    ? mode
                    : VoltageScaleMode.Pack,
            },

            // Пороги на ячейку. В отличие от пакетных, предзаполнены: 3,5 В — это 3,5 В и на 20S, и
            // на 60S, и угадывать тут нечего (план 27 §27.4). Пакетные остались на «Отображении» —
            // у кого заданы, работают в пакетном режиме; два набора живут при разных режимах и не
            // спорят.
            new()
            {
                Key = "Display:SagWindowCellVolts",
                Kind = SettingKind.Number,
                Page = SettingsPage.Experimental,
                SectionKey = "SectionCellVoltage",
                LabelKey = "SettingSagWindowCell",
                HintKey = "SettingSagWindowCellHint",
                UnitKey = "UnitVolts",
                IsVisible = () => dashboard.VoltageScale != VoltageScaleMode.Pack,
                Minimum = 0.1,
                Maximum = 2,
                Step = 0.05,
                Decimals = 2,
                Current = () => SettingsCatalogue.Fixed(dashboard.SagWindowCellVolts, 2),
                Apply = text => dashboard.SagWindowCellVolts = SettingsCatalogue.ParseNumber(text),
            },
            new()
            {
                Key = "Display:WarnCellVolts",
                Kind = SettingKind.Number,
                Page = SettingsPage.Experimental,
                SectionKey = "SectionCellVoltage",
                LabelKey = "SettingWarnCellVolts",
                HintKey = "SettingWarnCellVoltsHint",
                UnitKey = "UnitVolts",
                IsVisible = () => dashboard.VoltageScale != VoltageScaleMode.Pack,
                Maximum = 4.25,
                Step = 0.05,
                Decimals = 2,
                Current = () => SettingsCatalogue.Fixed(dashboard.WarnCellVolts, 2),
                Apply = text => dashboard.WarnCellVolts = SettingsCatalogue.ParseNumber(text),
            },
            new()
            {
                Key = "Display:DangerCellVolts",
                Kind = SettingKind.Number,
                Page = SettingsPage.Experimental,
                SectionKey = "SectionCellVoltage",
                LabelKey = "SettingDangerCellVolts",
                UnitKey = "UnitVolts",
                IsVisible = () => dashboard.VoltageScale != VoltageScaleMode.Pack,
                Maximum = 4.25,
                Step = 0.05,
                Decimals = 2,
                Current = () => SettingsCatalogue.Fixed(dashboard.DangerCellVolts, 2),
                Apply = text => dashboard.DangerCellVolts = SettingsCatalogue.ParseNumber(text),
            },
            new()
            {
                Key = "Display:EmptyCellVolts",
                Kind = SettingKind.Number,
                Page = SettingsPage.Experimental,
                SectionKey = "SectionCellVoltage",
                LabelKey = "SettingEmptyCellVolts",
                HintKey = "SettingEmptyCellVoltsHint",
                UnitKey = "UnitVolts",
                IsVisible = () => dashboard.VoltageScale != VoltageScaleMode.Pack,
                Maximum = 4.25,
                Step = 0.05,
                Decimals = 2,
                Current = () => SettingsCatalogue.Fixed(dashboard.EmptyCellVolts, 2),
                Apply = text => dashboard.EmptyCellVolts = SettingsCatalogue.ParseNumber(text),
            },
        ];
    }

    /// <summary>
    /// Ниже этого заряда догадка по одному напряжению уже опасна: она делит на 4,2 В, то есть
    /// считает колесо полным, и на разряженном занижает ряд. Порог грубый нарочно — точное «под
    /// завязку» назвать нечем, а процент здесь только повод предупредить, не поправка к числу.
    /// </summary>
    private const int NearlyFullPercent = 90;

    /// <summary>
    /// Отчёт кнопки «рассчитать»: сколько, откуда и <b>сколько вольт на банку из этого выходит</b>.
    /// Последнее и есть проверка без арифметики — ради неё отчёт и заведён (план 27 §27.4).
    /// <para>
    /// Считается по кадру, которым считала сама кнопка, а не по свежему: иначе ряд и напряжение
    /// пришли бы из разных мгновений.
    /// </para>
    /// </summary>
    private static string? CellsReport(TelemetrySnapshot? frame)
    {
        if (frame?.AutoPackCells is not { IsKnown: true } cells) return null;

        var report = new StringBuilder();
        report.AppendFormat(CultureInfo.CurrentCulture, AppStrings.SettingCellsFound, cells.Cells, CellsSourceName(cells.Source));

        // Напряжения может не быть у колеса, назвавшего ряд рукопожатием раньше первой телеметрии.
        // Тогда делить не на что, и проверка откладывается до кадра — но число уже названо.
        if (frame.VoltageV > 0)
        {
            report.Append(' ').AppendFormat(CultureInfo.CurrentCulture, AppStrings.SettingCellsPerCell, frame.VoltageV / cells.Cells);
        }

        report.Append("\n\n").Append(AppStrings.SettingCellsCheck);

        if (cells.Source == CellCountSource.VoltageGuess)
        {
            report.Append("\n\n").Append(AppStrings.SettingCellsGuessWarning);

            if (frame.Battery < NearlyFullPercent)
            {
                report.Append(' ').AppendFormat(CultureInfo.CurrentCulture, AppStrings.SettingCellsGuessLowCharge, frame.Battery);
            }
        }

        return report.ToString();
    }

    /// <summary>
    /// Ступень каскада словами райдера. <see cref="CellCountSource.UserSetting"/> и
    /// <see cref="CellCountSource.Unknown"/> сюда не доходят: кнопка берёт ответ без верхней
    /// ступени, а незнание отказывает исключением ещё в <c>Apply</c>.
    /// </summary>
    private static string CellsSourceName(CellCountSource source) => source switch
    {
        CellCountSource.SmartBms => AppStrings.SettingCellsSourceBms,
        CellCountSource.Protocol => AppStrings.SettingCellsSourceProtocol,
        CellCountSource.VoltageWithPercent => AppStrings.SettingCellsSourcePercent,
        _ => AppStrings.SettingCellsSourceGuess,
    };

    /// <summary>
    /// Что сказать о заданном ряде, если поделить на него нельзя всерьёз. Критерий один и тот же,
    /// каким живёт весь план 27: вольт на ячейку вне живого Li-ion. Он производный — от ряда и
    /// напряжения вместе, — поэтому до первого кадра ответа нет и предупреждения тоже.
    /// </summary>
    private static string? ImplausibleCellsNote(int cells, double packVolts)
    {
        if (cells <= 0 || packVolts <= 0) return null;

        double cellVolts = packVolts / cells;
        return LiIonCell.IsPlausible(cellVolts)
            ? null
            : string.Format(CultureInfo.CurrentCulture, AppStrings.SettingCellsImplausible, cells, cellVolts);
    }
}
