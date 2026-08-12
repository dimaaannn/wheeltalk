namespace WheelTalk.Lab.Droid.Ui;

/// <summary>
/// Слова для плиток стенда. Библиотека панели ресурсов не держит и подписи получает готовыми (тот же
/// порядок, что у команд шторки и плашки связи), а ресурсы приложения стенду не видны — поэтому
/// здесь лежит ровно то, что просит зашитая раскладка <c>TilesLayout.Fixed</c>.
/// <para>
/// Ключа нет в списке — показывается сам ключ: пропавшую подпись иначе нечем заметить, и это то же
/// правило, по которому <c>TranslateExtension</c> приложения рисует <c>!Ключ!</c>.
/// </para>
/// </summary>
public static class LabMetricWords
{
    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["TelemetrySpeed"] = "Скорость",
        ["TelemetryTopSpeed"] = "Максимум",
        ["TelemetryPwm"] = "ШИМ",
        ["TelemetryMaxPwm"] = "ШИМ макс",
        ["TelemetryAngle"] = "Наклон",
        ["TelemetryVoltage"] = "Напряжение",
        ["TelemetryCurrent"] = "Ток",
        ["TelemetryPhaseCurrent"] = "Фазный ток",
        ["TelemetryPower"] = "Мощность",
        ["TelemetryBattery"] = "Заряд",
        ["TelemetryBoardTemp"] = "Плата",
        ["TelemetryMotorTemp"] = "Двигатель",
        ["TelemetryTrip"] = "За поездку",
        ["TelemetryTotal"] = "Одометр",

        // Короткие подписи четвертных плиток (план плиток §4): полное имя в 61 px содержимого не
        // влезает ни при каком кегле. Ключ у них тот же, что у полного, плюс «Short» — и стенд
        // обязан знать их наравне с приложением, иначе на четвертных стоит сырой ключ.
        ["TelemetrySpeedShort"] = "Скор.",
        ["TelemetryVoltageShort"] = "Напр.",
        ["TelemetryBoardTempShort"] = "Темп.",
        ["TelemetryMotorTempShort"] = "Мотор",
        ["TelemetryPhaseCurrentShort"] = "Фазный",
        ["TelemetryPowerShort"] = "Мощн.",
        ["TelemetryMaxPwmShort"] = "Пик ШИМ",
        ["TelemetryTopSpeedShort"] = "Макс.",
        ["TelemetryTripShort"] = "Пробег",
        ["MetricCellVoltageShort"] = "Ячейка",

        // Знаки величин для справочного блока центра: единиц он не рисует, и единица живёт в самой
        // подписи — «V», «t°», «Заряд %» (решение владельца 12.08.2026). Ключ у знака тот же, что у
        // полного имени, плюс «Sign», и берётся он раньше короткого имени.
        ["TelemetryVoltageSign"] = "V",
        ["TelemetryBoardTempSign"] = "t°",
        ["TelemetryBatterySign"] = "Заряд %",

        // Счётчик поездки — величина самой панели: в каталоге телеметрии её нет, из снимка её не
        // прочесть (одометр минус точка отсчёта, которую двигает только кнопка шторки).
        ["MetricTripCounter"] = "Поездка",

        ["UnitKmh"] = "км/ч",
        ["UnitPercent"] = "%",
        ["UnitVolts"] = "В",
        ["UnitAmperes"] = "А",
        ["UnitWatts"] = "Вт",
        ["UnitKm"] = "км",
        ["UnitCelsius"] = "°C",
        ["UnitDegrees"] = "°",

        // Меню плитки показывает весь каталог, а не только зашитую раскладку, — поэтому здесь
        // лежат все двадцать семь величин теми же словами, что и в ресурсах приложения.
        ["TelemetryFromStart"] = "За сеанс",
        ["TelemetrySleep"] = "Автовыключение",
        ["MetricCellVoltage"] = "На ячейку",
        ["MetricTorque"] = "Момент",
        ["MetricMotorPower"] = "Мощность мотора",
        ["MetricCpuTemp"] = "Контроллер",
        ["MetricCpuLoad"] = "Загрузка",
        ["MetricImuTemp"] = "Гироскоп",
        ["MetricCurrentLimit"] = "Ограничение тока",
        ["MetricSpeedLimit"] = "Ограничение скорости",
        ["MetricHardwarePwm"] = "ШИМ колеса",
        ["MetricFanStatus"] = "Вентилятор",
        ["MetricRoll"] = "Крен",

        ["UnitNewtonMetres"] = "Н·м",
        ["UnitSeconds"] = "с",

        ["TilesTileMetric"] = "Величина",
        ["TilesTileSize"] = "Размер",
        ["TilesTileRemove"] = "Убрать",
        ["TilesTileEmpty"] = "Пусто",
        ["TilesTileKind"] = "Вид",
        ["TilesKindValue"] = "Число",
        ["TilesKindChart"] = "График",
        ["TilesKindExtremum"] = "Крайнее значение",
        ["TilesKindTrip"] = "Дистанция",
        ["TilesTileCaption"] = "Подпись",
        ["TilesActionReset"] = "Сбросить",
        ["TilesActionRename"] = "Переименовать",
        ["TilesActionChart"] = "График",
        ["TilesTileLowest"] = "Помнить минимум, а не максимум",
        ["TilesTileWindow"] = "За какое время",
        ["TilesTileOverlay"] = "Число поверх графика",
        ["TilesTileZoom"] = "Масштаб по значениям",
        ["TilesTileLabel"] = "Показывать подпись",
        ["TilesKindDivider"] = "Разделитель",
        ["TilesTileHeatBar"] = "Цветная полоса порога",
        ["TilesTileRounding"] = "Знаков после запятой",
        ["TilesRoundingDefault"] = "По умолчанию",
        ["TilesTileFill"] = "Заливка под линией",
        ["TilesTileAxis"] = "Шкала слева",
        ["TilesTileSmoothing"] = "Значение за период",
        ["TilesSmoothMinMax"] = "Минимум и максимум",
        ["TilesSmoothPeaks"] = "Только пики",
        ["TilesSmoothDips"] = "Только провалы",
        ["TilesTileLimits"] = "Пороги плитки",
        ["TilesTileWarn"] = "Жёлтый порог",
        ["TilesTileDanger"] = "Красный порог",
        ["TilesTileFalling"] = "Порог при снижении значения",

        ["UnitMinutesShort"] = "мин",
        ["UnitHoursShort"] = "ч",
        ["TilesEditSave"] = "Сохранить",
        ["TilesEditCancel"] = "Отменить",

        // Слова редактора центра. Не были вписаны при его заведении — заголовок и кнопки стояли
        // сырыми ключами (поймано прогоном 12.08.2026), потому что замок паритета стерёг только
        // имена величин; теперь он спрашивает и эти четыре ключа.
        ["CentreEditTitle"] = "Что показывать в центре",
        ["CentreEditAdd"] = "Добавить",
        ["CentreEditFull"] = "Больше строк в центр не помещается.",
        ["ButtonDone"] = "Готово",
    };

    public static string Get(string key) => Words.GetValueOrDefault(key, key);
}
