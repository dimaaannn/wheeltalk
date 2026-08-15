using System.Globalization;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Settings;
using WheelTalk.Core.Settings.Device;

namespace WheelTalk.Droid.Settings.Catalogue;

/// <summary>
/// Descriptors of <see cref="SettingsPage.WheelDevice"/> — то, что колесо рассказало о себе
/// страницей 8 своего потока (план 34 §1.4). Страница <b>только читает</b>: ни одна строка здесь не
/// правится человеком, запись начинается этапом 5 и не раньше байтовых тестов.
/// <para>
/// Все строки — <see cref="SettingDescriptor.ReportedByWheel"/>, и признак ставится
/// <b>единственным местом</b> — <see cref="Row"/>. Это не украшение, а капкан К4 плана: описание,
/// собранное мимо фабрики и забывшее признак, ушло бы в слои, вернулось из хранилища при следующем
/// запуске и показалось состоянием колеса, которого колесо не подтверждало. Замок на это —
/// <c>WheelDevicePageRulesTests</c>.
/// </para>
/// <para>
/// Показывается ровно то, что пришло: без прижатия к границам ручки и без слова вместо нуля
/// (слово владельца 15.08.2026 — «как есть, ничего не выдумывать»). Наклон, выключенный числом
/// 200, так числом 200 и стоит. Границы <see cref="SettingDescriptor.Minimum"/> и
/// <see cref="SettingDescriptor.Maximum"/> здесь — каталог производителя, а не ограничитель показа:
/// они понадобятся записи и ей же будут проверять вводимое.
/// </para>
/// <para>
/// Подписи — не наши. Оригинальные названия сняты из родного приложения производителя и сведены в
/// <c>docs/originals-reference-data.md</c> §14; в ресурсах лежит перевод оттуда же, строка в строку.
/// Единственное поле раскладки §1.4, которого здесь нет, — <c>maxChargeVolBase</c>: решением
/// владельца 16.08.2026 (§12.0) оно отложено до того, как его смысл соберут разбором.
/// </para>
/// <para>
/// Одна строка приходит не страницей 8, а кадром телеметрии, — режим езды старых колёс
/// (<see cref="WheelSettingKeys.RideMode"/>, шаг 4.2 плана). Она и жёсткость педалей —
/// <b>одно и то же место экрана у разных поколений</b>: видна ровно одна из двух, и решает это
/// сентинел, а не таблица моделей (§1.3). Так же поступает и родное приложение —
/// <c>ControlActivity.initControlData</c> прячет одну строку и показывает другую по одной проверке
/// жёсткости педалей.
/// </para>
/// </summary>
internal static class WheelDevicePage
{
    /// <summary>
    /// Ключ настройки — приставка плюс <b>имя поля протокола</b> из
    /// <see cref="WheelSettingKeys"/>: так ключ хранилища и ключ снимка не могут разойтись, потому
    /// что это одна и та же строка, а не две одинаковых.
    /// </summary>
    private const string KeyPrefix = "WheelDevice:";

    /// <summary>
    /// Одна секция на всю страницу — готовая «Сообщает колесо». Секция принадлежит странице, и то
    /// же имя на странице колеса — другая секция; общего ключа они не делят, только слово.
    /// Разбивка на темы — решение об устройстве страницы, и оно за мастером: перекладка стоит
    /// правки одного поля в описании.
    /// </summary>
    private const string ReportedSection = "SectionReported";

    /// <param name="lastFrame">
    /// Последний кадр телеметрии — единственный источник значений. Делегатом, а не снимком:
    /// описания строятся один раз при запуске, когда колесо ещё молчит, а страница настроек
    /// перечитывает их на каждой отрисовке.
    /// </param>
    public static IReadOnlyList<SettingDescriptor> Build(Func<TelemetrySnapshot?> lastFrame)
    {
        WheelSettingValue Value(string field) => field == WheelSettingKeys.RideMode
            ? RideMode(lastFrame())
            : lastFrame()?.WheelSettings?[field] ?? WheelSettingValue.Missing();

        // Строки идут тремя смысловыми кучками, а внутри кучки — порядком байтов страницы 8 (§1.4):
        // так строка экрана сверяется с раскладкой без перевода. Секция у всех одна, поэтому кучки
        // — это порядок показа, а не заголовки.
        return
        [
            // ---- Педали и езда -----------------------------------------------------------
            Row(Value, WheelSettingKeys.PedalHardness, ReportedSection, "SettingWheelDevicePedalHardness",
                SettingKind.Number, max: 100, unit: "UnitPercent"),
            // Подмена предыдущей строки у колёс без плавной шкалы. Подпись — своя, оригинальная:
            // у производителя это отдельный экран «Ride Mode setting» (`ride_mode`), и от почти
            // такой же подписи жёсткости педалей («Ride mode setting», `padle_soft_setting`) он
            // отличается одной заглавной буквой — обе перенесены как есть
            // (originals-reference-data.md §14.1). Одновременно их не бывает, путать нечего.
            Row(Value, WheelSettingKeys.RideMode, ReportedSection, "SettingWheelDeviceRideMode",
                SettingKind.Number, min: 1, max: 3, hint: "SettingWheelDeviceRideModeHint"),
            Row(Value, WheelSettingKeys.Gyro, ReportedSection, "SettingWheelDeviceGyro",
                SettingKind.Number, max: 2, hint: "SettingWheelDeviceGyroHint"),
            Row(Value, WheelSettingKeys.TransportMode, ReportedSection, "SettingWheelDeviceTransportMode",
                SettingKind.Toggle),
            Row(Value, WheelSettingKeys.HighSpeedMode, ReportedSection, "SettingWheelDeviceHighSpeedMode",
                SettingKind.Toggle),
            // Подпись — оригинал с одной исправленной опечаткой: в родном приложении на экране
            // стоит «assistt», лишняя «t» (originals-reference-data.md §14.1, отмечена там же).
            // Правка по слову владельца; остальные две опечатки §14 сидят в именах ресурсов, а не
            // в тексте, и править в них нечего.
            Row(Value, WheelSettingKeys.UpOrDownSpeedHelper, ReportedSection, "SettingWheelDeviceUpOrDownSpeedHelper",
                SettingKind.Number, max: 100, unit: "UnitPercent"),
            Row(Value, WheelSettingKeys.UpSpeedCul, ReportedSection, "SettingWheelDeviceUpSpeedCul",
                SettingKind.Number, max: 100, unit: "UnitPercent"),

            // ---- Защита и заряд ----------------------------------------------------------
            Row(Value, WheelSettingKeys.StopSpeed, ReportedSection, "SettingWheelDeviceStopSpeed",
                SettingKind.Number, min: 10, max: 120, unit: "UnitKmh", hint: "SettingWheelDeviceStopSpeedHint"),
            Row(Value, WheelSettingKeys.StopPowerRate, ReportedSection, "SettingWheelDeviceStopPowerRate",
                SettingKind.Number, min: 30, max: 100, unit: "UnitPercent"),
            // Знаковое поле — единственное на странице (капкан К1), и единственное с масштабом:
            // колесо пакует десятые доли, и родное приложение делит это число на десять
            // (originals-reference-data.md §14.1 и §7 — «−15..15 (÷10 → %)», сказано дважды). Делим и
            // мы, как делим напряжение во всех декодерах: масштаб — часть чтения значения, а не
            // толкование смысла.
            Row(Value, WheelSettingKeys.Vol, ReportedSection, "SettingWheelDeviceVol",
                SettingKind.Number, min: -1.5, max: 1.5, unit: "UnitPercent", decimals: 1),
            Row(Value, WheelSettingKeys.LowVolMode, ReportedSection, "SettingWheelDeviceLowVolMode",
                SettingKind.Toggle),
            Row(Value, WheelSettingKeys.MaxChargeVol, ReportedSection, "SettingWheelDeviceMaxChargeVol",
                SettingKind.Number, max: 120, unit: "UnitVolts"),
            Row(Value, WheelSettingKeys.BrakePressureAlarm, ReportedSection, "SettingWheelDeviceBrakePressureAlarm",
                SettingKind.Number, min: 80, max: 125, unit: "UnitPercent"),

            // ---- Экран и единицы ---------------------------------------------------------
            Row(Value, WheelSettingKeys.ScreenBacklightRate, ReportedSection, "SettingWheelDeviceScreenBacklightRate",
                SettingKind.Number, max: 100, unit: "UnitPercent"),
            // Выбор, а не переключатель: «Да/Нет» о единицах измерения не говорит ничего. Подписи
            // вариантов — дословные кнопки родного приложения (§14.1), а не наш пересказ.
            Row(Value, WheelSettingKeys.Unit, ReportedSection, "SettingWheelDeviceUnit",
                SettingKind.Choice, max: 1,
                choices: ["0", "1"],
                choiceLabels: ["SettingWheelDeviceUnitKilometres", "SettingWheelDeviceUnitMiles"]),
            Row(Value, WheelSettingKeys.KeyTone, ReportedSection, "SettingWheelDeviceKeyTone",
                SettingKind.Number, max: 100, unit: "UnitPercent"),
        ];
    }

    /// <summary>
    /// Режим езды старых колёс — байт 31 кадра телеметрии (<see cref="TelemetrySnapshot.RideModeRaw"/>),
    /// единственное значение страницы не из снимка настроек.
    /// <para>
    /// <b>Строка есть ровно там, где нет плавной шкалы</b> — два «нет» подряд, и оба сказаны
    /// колесом. Первое: сам байт не сентинел. Sherman L шлёт в нём <c>0x80</c> во всех 597 кадрах
    /// записи, а жёсткость педалей сообщает страницей 8 — то же «такой настройки нет», что и на
    /// странице (<see cref="VeteranSettingsPage.NoSuchSetting"/>). Второе: колесо не сообщило
    /// плавной жёсткости. Молчание страницы 8 тоже «не сообщило», и это не оплошность: у колёс
    /// старше пятого поколения страницы настроек нет вовсе
    /// (<c>VeteranDecoder.DecodeSmartBms</c> — <c>_protocolVersion &lt; 5</c>), а строка нужна
    /// именно им. Родное приложение решает так же: <c>controlSettingData == null ||
    /// getPedalHardness() == 128</c> (<c>ControlActivity.initControlData</c>).
    /// </para>
    /// <para>
    /// <b>Число идёт сырым, без толкования.</b> Родное приложение читает этот байт двояко — три
    /// положения 1/2/3 либо плавная шкала со смещением (<c>SetRideModeActivity.java:70-78</c>), —
    /// и выбирает <b>не по кадру</b>: признак <c>isContinuousSoftHardSet</c> берётся из карточки
    /// колеса, которую приложение скачивает с сервера производителя и ищет по коду версии железа
    /// (<c>CarBaseInfo</c>, <c>CarDataManager.getCarInfoByHardVersion</c>). Ни карточки, ни её
    /// заменителя у нас нет, а наших данных три точки — Abrams 3, Patton 2, Lynx 180. Догадка
    /// поверх трёх точек стоила бы райдеру неверно понятой жёсткости педалей, поэтому показываем
    /// то, что пришло (решение владельца «как есть, ничего не выдумывать»).
    /// </para>
    /// <para>
    /// Когда признак найдётся, три положения подписываются <b>«Soft» / «Medium» / «Strong»</b> —
    /// <c>R.array.ride_mode</c>, которым родное приложение показывает <i>значение</i> и в меню
    /// настроек, и на приборной панели. Расхождение, отмеченное в
    /// <c>docs/originals-reference-data.md</c> §14.1, разрешается этим же: «Hard»
    /// (<c>mode_hard</c>) — подпись <i>кнопки записи</i> на экране правки, а наша строка читает.
    /// Уточнение к §14.1: «Strong» стоит не только на приборной панели — тем же массивом подписан
    /// и пункт меню настроек (<c>ControlActivity.java:383</c>).
    /// </para>
    /// </summary>
    private static WheelSettingValue RideMode(TelemetrySnapshot? frame)
    {
        if (frame?.RideModeRaw is not { } raw) return WheelSettingValue.Missing();

        bool noSuchSetting = raw == VeteranSettingsPage.NoSuchSetting
            || frame.WheelSettings?[WheelSettingKeys.PedalHardness].Supported == true;

        return noSuchSetting ? WheelSettingValue.Missing(raw) : WheelSettingValue.Reported(raw, raw);
    }

    /// <summary>
    /// Одна строка страницы. <b>Единственное место, где описание этой страницы создаётся</b>:
    /// признак «сообщено колесом», условие видимости и чтение снимка — общие у всех шестнадцати, и
    /// повторить их шестнадцать раз значит однажды повторить пятнадцать (капкан К4).
    /// <para>
    /// <see cref="SettingDescriptor.IsVisible"/> — «снимок знает это поле». Настройки, которой у
    /// колеса нет, на экране не бывает: сентинел <c>0x80</c> прячет строку по каждому полю
    /// отдельно (§1.3). Решение владельца «разрешены все» — про наш список, а не про показ
    /// человеку чужих настроек (§12.0).
    /// </para>
    /// <para>
    /// <see cref="SettingDescriptor.Apply"/> пуст, и это не заглушка: значение живёт в снимке
    /// колеса, куда приложению писать нечем и незачем. <c>SettingsBinder</c> сообщённые строки и
    /// не применяет — он их пропускает во всех трёх местах.
    /// </para>
    /// </summary>
    private static SettingDescriptor Row(
        Func<string, WheelSettingValue> value,
        string field,
        string section,
        string label,
        SettingKind kind,
        double min = 0,
        double max = 100,
        string? unit = null,
        string? hint = null,
        IReadOnlyList<string>? choices = null,
        IReadOnlyList<string>? choiceLabels = null,
        int decimals = 0) => new()
    {
        Key = KeyPrefix + field,
        Kind = kind,
        Page = SettingsPage.WheelDevice,
        SectionKey = section,
        LabelKey = label,
        HintKey = hint,
        ReportedByWheel = true,
        IsVisible = () => value(field).Supported,
        Current = () => Text(kind, value(field), decimals),
        Apply = _ => { },
        Minimum = min,
        Maximum = max,
        UnitKey = unit,
        Decimals = decimals,
        Choices = choices ?? [],
        ChoiceLabelKeys = choiceLabels ?? [],
    };

    /// <summary>
    /// Значение в том виде, в каком его хранит описание: переключатель — «True»/«False», всё
    /// остальное — число. Поля, о котором колесо промолчало, на экране нет, и говорить за него
    /// нечего — пустая строка.
    /// <para>
    /// Знаков после запятой у сообщённого поля ровно столько, во сколько раз колесо пакует
    /// величину: единственная такая — поправка напряжения, десятые доли процента. Масштаб живёт
    /// здесь и нигде больше, как и во всех прочих описаниях каталога.
    /// </para>
    /// </summary>
    private static string Text(SettingKind kind, WheelSettingValue value, int decimals) => value switch
    {
        { Supported: false } => string.Empty,
        _ when kind == SettingKind.Toggle => (value.Value != 0).ToString(),
        _ => (value.Value / Math.Pow(10, decimals)).ToString("F" + decimals, CultureInfo.InvariantCulture),
    };
}
