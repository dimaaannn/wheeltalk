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
        ["TelemetryTopSpeed"] = "Максимальная",
        ["TelemetryPwm"] = "ШИМ",
        ["TelemetryMaxPwm"] = "ШИМ, максимум",
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

        ["UnitKmh"] = "км/ч",
        ["UnitPercent"] = "%",
        ["UnitVolts"] = "В",
        ["UnitAmperes"] = "А",
        ["UnitWatts"] = "Вт",
        ["UnitKm"] = "км",
        ["UnitCelsius"] = "°C",
        ["UnitDegrees"] = "°",

        // Меню плитки показывает весь каталог, а не только зашитую раскладку, — поэтому здесь
        // лежат все двадцать шесть величин теми же словами, что и в ресурсах приложения.
        ["TelemetryFromStart"] = "С подключения",
        ["TelemetrySleep"] = "Автовыключение",
        ["MetricTorque"] = "Момент",
        ["MetricMotorPower"] = "Мощность мотора",
        ["MetricCpuTemp"] = "Контроллер",
        ["MetricCpuLoad"] = "Загрузка",
        ["MetricImuTemp"] = "Гироскоп",
        ["MetricCurrentLimit"] = "Лимит тока",
        ["MetricSpeedLimit"] = "Предел колеса",
        ["MetricHardwarePwm"] = "ШИМ колеса",
        ["MetricFanStatus"] = "Вентилятор",
        ["MetricRoll"] = "Крен",

        ["UnitNewtonMetres"] = "Н·м",
        ["UnitSeconds"] = "с",

        ["TilesTileMetric"] = "Величина",
        ["TilesTileSize"] = "Размер",
        ["TilesTileRemove"] = "Убрать",
        ["TilesTileEmpty"] = "Пусто",
        ["TilesEditSave"] = "Сохранить",
        ["TilesEditCancel"] = "Отменить",
    };

    public static string Get(string key) => Words.GetValueOrDefault(key, key);
}
