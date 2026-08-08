using System.Globalization;
using WheelTalk.Core.Battery;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Settings;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Resources.Strings;

namespace WheelTalk.Droid.Settings.Catalogue;

/// <summary>
/// Descriptors of <see cref="SettingsPage.Wheel"/> — split out of <c>SettingsCatalogue.Build</c>
/// (plan 14, А2.1), body moved as-is.
/// </summary>
internal static class WheelPage
{
    /// <summary>
    /// Своё имя колеса, в слое этого колеса. Константой, потому что читает его не только страница
    /// настроек: экран поиска подписывает им привязанные колёса (план 24 §А2), а два одинаковых
    /// литерала в разных файлах — это ключ, который однажды переименуют наполовину.
    /// </summary>
    public const string AliasKey = "Wheel:Alias";

    /// <summary>
    /// Ряд ячеек. Константой по той же причине, что и алиас: в него пишет кнопка «рассчитать»,
    /// а ключ, названный в двух файлах порознь, однажды переименуют наполовину.
    /// </summary>
    public const string CellsKey = "WheelConfig:CellsInSeries";

    public static IReadOnlyList<SettingDescriptor> Build(
        AppWheelConfig wheel,
        WheelOptions selected,
        WheelIdentity identity,
        Func<WheelProtocol?> protocol,
        Action restartAuthentication,
        Func<TelemetrySnapshot?> lastFrame,
        Action<int> saveCells,
        Func<bool> wheelScopeChosen)
    {
        // Спрашивается у сессии, а не у настроек: протокол теперь опознаётся при подключении, и
        // сохранённой копии, по которой можно было бы решить заранее, больше нет. До первого кадра
        // ответа нет вовсе — тогда настройки Begode не показываются, и это честно.
        bool IsGotway() => protocol() == WheelProtocol.Gotway;
        bool IsInMotion() => protocol() == WheelProtocol.InMotion;

        return
        [
            // ---- Wheel -------------------------------------------------------------------

            // Имя колеса стоит первым на своей странице: это единственная строка здесь, которую
            // видно на главном экране. Заводится из рекламного имени Bluetooth при выборе колеса
            // (у Begode и Veteran оно одинаковое у всех), правится потому, что на панели оно нужно
            // ровно затем, чтобы поймать подключение к чужому колесу — прогон 5.
            new()
            {
                Key = AliasKey,
                Kind = SettingKind.Text,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionWheelIdentity",
                LabelKey = "SettingWheelName",
                HintKey = "SettingWheelNameHint",
                // Алиас — настройка **этого** колеса, поэтому обычная слоевая строка, а не запись в
                // общий файл: пустая означает «звать как объявляется по Bluetooth».
                Current = () => identity.Alias,
                Apply = text => identity.Alias = text,
            },
            new()
            {
                Key = "WheelConfig:GotwayNegative",
                Kind = SettingKind.Choice,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionCurrent",
                LabelKey = "SettingGotwayNegative",
                HintKey = "SettingGotwayNegativeHint",
                // Порядок и подписи — оригинала, и это стоило одной ошибки. Множитель и подпись
                // говорят о разном: «-1» умножает на минус единицу, но называется «прямой», потому
                // что имя описывает **результат** — «показания выходят той стороной». Я однажды
                // переставил их по арифметике, и вариант, который оригинал зовёт обратным, оказался
                // подписан «как есть». Настройку подбирают руками по подсказке, так что расходиться
                // с оригиналом в словах здесь дороже, чем казалось.
                // Подбирается на глаз потому, что разные колёса ориентируют скорость и ток
                // по-разному и угадать за них нельзя. На тревоги и крупные цифры знак не влияет —
                // они берут модуль.
                Choices = ["-1", "0", "1"],
                ChoiceLabelKeys = ["SettingSignStraight", "SettingSignAbsolute", "SettingSignReverse"],
                Current = () => wheel.GotwayNegative,
                Apply = text => wheel.GotwayNegative = text,
            },
            new()
            {
                Key = "WheelConfig:AutoVoltage",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionCurrent",
                LabelKey = "SettingAutoVoltage",
                HintKey = "SettingAutoVoltageHint",
                // Gotway only, and the original hides it under custom firmware as well — that
                // firmware is not ported, so there is nothing here to hide it behind yet.
                IsVisible = IsGotway,
                Current = () => wheel.AutoVoltage.ToString(),
                Apply = text => wheel.AutoVoltage = SettingsCatalogue.ParseBool(text),
            },
            new()
            {
                Key = "WheelConfig:GotwayVoltage",
                Kind = SettingKind.Choice,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionCurrent",
                LabelKey = "SettingGotwayVoltage",
                // Hidden the moment the wheel is asked to work it out itself: two answers to one
                // question, one of which is ignored, is worse than one answer.
                IsVisible = () => IsGotway() && !wheel.AutoVoltage,
                // Five and six are the wrong way round against the volts they mean. That is how
                // the original stores them, and a stored value we renumber is a wheel that comes
                // back configured differently.
                Choices = ["0", "1", "2", "3", "4", "5", "6"],
                ChoiceLabelKeys =
                [
                    "SettingVoltage672", "SettingVoltage840", "SettingVoltage1008", "SettingVoltage1176",
                    "SettingVoltage1344", "SettingVoltage1680", "SettingVoltage1512",
                ],
                Current = () => wheel.GotwayVoltage,
                Apply = text => wheel.GotwayVoltage = text,
            },
            new()
            {
                Key = "WheelConfig:UseRatio",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionCurrent",
                LabelKey = "SettingUseRatio",
                HintKey = "SettingUseRatioHint",
                IsVisible = IsGotway,
                Current = () => wheel.UseRatio.ToString(),
                Apply = text => wheel.UseRatio = SettingsCatalogue.ParseBool(text),
            },
            new()
            {
                Key = "WheelConfig:InMotionPassword",
                Kind = SettingKind.Text,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionInMotion",
                LabelKey = "SettingInMotionPassword",
                HintKey = "SettingInMotionPasswordHint",
                IsVisible = IsInMotion,
                Current = () => wheel.InMotionPassword,
                // Zero-padded to six digits on save, same as AppConfig.passwordForWheel's own
                // setter — CANMessage.getPassword indexes the first six bytes unconditionally, so a
                // shorter string would crash the very next password frame.
                Apply = text => wheel.InMotionPassword = text.Length >= 6
                    ? text[..6]
                    : text.PadLeft(6, '0'),

                // Здесь единственный вход к паролю, и правка обязана дойти до колеса сама: счётчик
                // отправок к этому времени исчерпан, и без перезапуска новый пароль лежал бы в
                // настройках, а колесу не уходил — до переподключения ничего бы не изменилось.
                //
                // Крючок правки, а не Apply: тот зовётся и на старте приложения, и при смене слоя
                // или колеса, и на правку соседней строки — то есть будил бы колесо без всякой
                // просьбы, а на старте ещё и поднимал бы сессию раньше обработчиков падений.
                //
                // Без условия «значение изменилось»: те же шесть цифр, введённые заново, — попытка
                // не хуже прочих, и промолчать на неё значит оставить человека без ответа.
                AfterEdit = restartAuthentication,
            },
            // Ёмкости батареи здесь нет намеренно. Её не читает ни одна строка кода — она
            // понадобится прогнозу запаса хода (план 9) и вернётся вместе с ним. Настройка,
            // которая ничего не меняет, нарушает правило §0 плана 6 и вводит в заблуждение
            // сильнее, чем её отсутствие.
            new()
            {
                Key = "WheelConfig:UseBetterPercents",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionBattery",
                LabelKey = "SettingUseBetterPercents",
                HintKey = "SettingUseBetterPercentsHint",
                Current = () => wheel.UseBetterPercents.ToString(),
                Apply = text => wheel.UseBetterPercents = SettingsCatalogue.ParseBool(text),
            },
            // Ряд ячеек — верхняя ступень каскада (план 27): задан человеком — бьёт и умный BMS, и
            // знание протокола. Ноль означает «не задано», как принято у нас и в оригинале.
            new()
            {
                Key = CellsKey,
                Kind = SettingKind.Number,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionBattery",
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
                Page = SettingsPage.Wheel,
                SectionKey = "SectionBattery",
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
                    var cells = lastFrame()?.AutoPackCells ?? CellCount.Unknown;
                    if (!cells.IsKnown) throw new InvalidOperationException(AppStrings.SettingCellsNoData);

                    saveCells(cells.Cells);
                },
            },
            new()
            {
                Key = "WheelConfig:CustomPercents",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionCustomPercents",
                Advanced = true,
                LabelKey = "SettingCustomPercents",
                HintKey = "SettingCustomPercentsHint",
                Current = () => wheel.CustomPercents.ToString(),
                Apply = text => wheel.CustomPercents = SettingsCatalogue.ParseBool(text),
            },
            new()
            {
                Key = "WheelConfig:CellVoltageTiltback",
                Kind = SettingKind.Number,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionCustomPercents",
                Advanced = true,
                LabelKey = "SettingCellVoltageTiltback",
                HintKey = "SettingCellVoltageTiltbackHint",
                UnitKey = "UnitVolts",
                // Volts on screen, hundredths in the object — the same split the original makes.
                IsVisible = () => wheel.CustomPercents,
                Minimum = 2.5,
                Maximum = 4.0,
                Step = 0.01,
                Decimals = 2,
                Current = () => SettingsCatalogue.Fixed(wheel.CellVoltageTiltback / 100.0, 2),
                Apply = text => wheel.CellVoltageTiltback = (int)Math.Round(SettingsCatalogue.ParseNumber(text) * 100),
            },

            // ---- Wheel, read only --------------------------------------------------------
            new()
            {
                Key = "WheelConfig:HwPwm",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionReported",
                LabelKey = "SettingHwPwm",
                HintKey = "SettingHwPwmHint",
                ReportedByWheel = true,
                Current = () => wheel.HwPwm.ToString(),
                Apply = text => wheel.HwPwm = SettingsCatalogue.ParseBool(text),
            },
            new()
            {
                Key = "WheelConfig:IsAlexovikFW",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionReported",
                LabelKey = "SettingAlexovikFw",
                HintKey = "SettingAlexovikFwHint",
                ReportedByWheel = true,
                Current = () => wheel.IsAlexovikFW.ToString(),
                Apply = text => wheel.IsAlexovikFW = SettingsCatalogue.ParseBool(text),
            },
            new()
            {
                Key = "WheelConfig:LightEnabled",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionReported",
                LabelKey = "SettingLightEnabled",
                HintKey = "SettingLightEnabledHint",
                ReportedByWheel = true,
                Current = () => wheel.LightEnabled.ToString(),
                Apply = text => wheel.LightEnabled = SettingsCatalogue.ParseBool(text),
            },

            // ---- Wheel, only for wheels that do not compute their own duty cycle ---------
            // Три числа, из которых считается ШИМ. В оригинале они лежат на экране тревог, у нас
            // переехали к колесу и в дополнительные, по двум причинам. Первая: они кормят не
            // только тревогу, но и крупную цифру ШИМ на главном экране и запись поездки — то есть
            // работают, когда тревоги молчат. Вторая: их выключатель `HwPwm` сообщает само колесо,
            // и раздел исчезает сам, посреди сеанса; исчезать он должен там же, где стоит причина.
            new()
            {
                Key = "WheelConfig:RotationSpeed",
                Kind = SettingKind.Number,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionPwmModel",
                Advanced = true,
                LabelKey = "SettingRotationSpeed",
                HintKey = "SettingRotationSpeedHint",
                UnitKey = "UnitKmh",
                IsVisible = () => !wheel.HwPwm,
                // Не ноль: все три числа стоят в знаменателе CalculatePwm. Ноль в скорости или
                // множителе — деление на ноль, ноль в напряжении — вечно нулевой ШИМ, то есть
                // тревога, которая не сработает никогда. В оригинале нижняя граница нулевая;
                // у нас это последний рубеж перед райдером, и мы от неё отходим.
                Minimum = 5,
                Maximum = 250,
                Step = 0.1,
                Decimals = 1,
                Current = () => SettingsCatalogue.Fixed(wheel.RotationSpeed / 10.0, 1),
                Apply = text => wheel.RotationSpeed = (int)Math.Round(SettingsCatalogue.ParseNumber(text) * 10),
            },
            new()
            {
                Key = "WheelConfig:RotationVoltage",
                Kind = SettingKind.Number,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionPwmModel",
                Advanced = true,
                LabelKey = "SettingRotationVoltage",
                UnitKey = "UnitVolts",
                // Смысл имеет отношение этой пары к RotationSpeed, а не каждое число по
                // отдельности: в CalculatePwm они входят частным.
                HintKey = "SettingRotationVoltageHint",
                IsVisible = () => !wheel.HwPwm,
                Minimum = 20,
                // Поле не Gotway-only: InMotion тоже считает ШИМ программно (CalculatePwm,
                // HwPwm там никогда не взводится) и держит его паки до 50S — под 174 В у EX30 и
                // выше. 250 — запас над полным зарядом такого пака, а не над списком GotwayVoltage
                // выше (тот 40S-предел касается только выбора из Choices, не этой шкалы).
                Maximum = 250,
                Step = 0.1,
                Decimals = 1,
                Current = () => SettingsCatalogue.Fixed(wheel.RotationVoltage / 10.0, 1),
                Apply = text => wheel.RotationVoltage = (int)Math.Round(SettingsCatalogue.ParseNumber(text) * 10),
            },
            new()
            {
                Key = "WheelConfig:PowerFactor",
                Kind = SettingKind.Number,
                Page = SettingsPage.Wheel,
                SectionKey = "SectionPwmModel",
                Advanced = true,
                LabelKey = "SettingPowerFactor",
                HintKey = "SettingPowerFactorHint",
                UnitKey = "UnitPercent",
                IsVisible = () => !wheel.HwPwm,
                Minimum = 10,
                Current = () => SettingsCatalogue.Whole(wheel.PowerFactor),
                Apply = text => wheel.PowerFactor = (int)SettingsCatalogue.ParseNumber(text),
            },
        ];
    }

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
