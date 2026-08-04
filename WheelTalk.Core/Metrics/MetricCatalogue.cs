using WheelTalk.Core.Contracts;

namespace WheelTalk.Core.Metrics;

/// <summary>
/// Все числовые величины телеметрии, описанные один раз (план 23 §3.1). Отсюда берут и плитки
/// второго главного экрана, и — позже — скины плана 17 §4: словарь величин делается однажды и
/// служит обоим.
/// <para>
/// <b>Числа, и только они.</b> Текстовые поля снимка (сообщение колеса, строка режима) сюда не
/// входят: <see cref="MetricDescriptor.Read"/> отдаёт <c>double?</c>, а плитка рисует число. Всё
/// поле снимка целиком, без отбора, показывает экран «Данные» (план 23 §3.5) — это его работа, не
/// эта.
/// </para>
/// <para>
/// <b>Подписи — ключи ресурсов приложения</b>, а слова живут там же, где остальные: у величин,
/// которые уже показывает экран «Данные», ключ тот же самый (<c>Telemetry*</c>). Одна величина —
/// одно слово; два ключа на «Напряжение» разошлись бы перводом.
/// </para>
/// </summary>
public static class MetricCatalogue
{
    /// <summary>
    /// Порядок здесь — порядок в списке выбора величин (план 23 §3.3, шаг 5): движение, питание,
    /// температура, пробег, затем то, что сообщает одно семейство протоколов.
    /// </summary>
    public static readonly IReadOnlyList<MetricDescriptor> All =
    [
        // ---- Движение ----------------------------------------------------------------------
        new()
        {
            Id = "speed",
            LabelKey = "TelemetrySpeed",
            UnitKey = "UnitKmh",
            Decimals = 1,
            Read = s => s.SpeedKmh,
            Column = "speed",
        },
        new()
        {
            Id = "pwm",
            LabelKey = "TelemetryPwm",
            UnitKey = "UnitPercent",
            Decimals = 1,
            Read = s => s.Pwm,
            Column = "pwm",
        },
        // Максимумы колесо ведёт само, и в таблицу телеметрии они не пишутся: график по ним — это
        // график исходной величины. Живьём есть, графика нет.
        new()
        {
            Id = "max_pwm",
            LabelKey = "TelemetryMaxPwm",
            UnitKey = "UnitPercent",
            Decimals = 1,
            Read = s => s.MaxPwm,
        },
        new()
        {
            Id = "top_speed",
            LabelKey = "TelemetryTopSpeed",
            UnitKey = "UnitKmh",
            Decimals = 1,
            Read = s => s.TopSpeedKmh,
        },
        new()
        {
            Id = "tilt",
            LabelKey = "TelemetryAngle",
            UnitKey = "UnitDegrees",
            Decimals = 1,
            Read = s => WheelReports.Veteran(s) ? s.Angle : null,
            Column = "tilt",
        },

        // ---- Питание -----------------------------------------------------------------------
        new()
        {
            Id = "voltage",
            LabelKey = "TelemetryVoltage",
            UnitKey = "UnitVolts",
            Decimals = 2,
            Read = s => s.VoltageV,
            Column = "voltage",
        },
        new()
        {
            Id = "current",
            LabelKey = "TelemetryCurrent",
            UnitKey = "UnitAmperes",
            Decimals = 2,
            Read = s => s.CurrentA,
            Column = "current",
        },
        new()
        {
            Id = "phase_current",
            LabelKey = "TelemetryPhaseCurrent",
            UnitKey = "UnitAmperes",
            Decimals = 2,
            Read = s => s.PhaseCurrentA,
            Column = "phase_current",
        },
        new()
        {
            Id = "power",
            LabelKey = "TelemetryPower",
            UnitKey = "UnitWatts",
            Read = s => s.PowerW,
            Column = "power",
        },
        new()
        {
            Id = "battery_level",
            LabelKey = "TelemetryBattery",
            UnitKey = "UnitPercent",
            Read = s => s.Battery,
            Column = "battery_level",
        },

        // ---- Температура -------------------------------------------------------------------
        new()
        {
            Id = "system_temp",
            LabelKey = "TelemetryBoardTemp",
            UnitKey = "UnitCelsius",
            Read = s => s.TemperatureC,
            Column = "system_temp",
        },
        new()
        {
            Id = "temp2",
            LabelKey = "TelemetryMotorTemp",
            UnitKey = "UnitCelsius",
            Read = s => WheelReports.Gotway(s) ? s.Temperature2C : null,
            Column = "temp2",
        },

        // ---- Пробег ------------------------------------------------------------------------
        new()
        {
            Id = "distance",
            LabelKey = "TelemetryTrip",
            UnitKey = "UnitKm",
            Decimals = 2,
            Read = s => s.WheelDistanceKm,
            Column = "distance",
        },
        new()
        {
            Id = "distance_from_start",
            LabelKey = "TelemetryFromStart",
            UnitKey = "UnitKm",
            Decimals = 2,
            Read = s => s.DistanceFromStartKm,
        },
        new()
        {
            Id = "totaldistance",
            LabelKey = "TelemetryTotal",
            UnitKey = "UnitKm",
            Read = s => s.TotalDistanceKm,
            Column = "totaldistance",
        },

        // ---- Что сообщает одно семейство ---------------------------------------------------
        //
        // Одиннадцать величин из §1 плана. У остальных семейств их нет вовсе — прочерк, а не ноль.
        new()
        {
            Id = "torque",
            LabelKey = "MetricTorque",
            UnitKey = "UnitNewtonMetres",
            Decimals = 2,
            Read = s => WheelReports.InMotionV2(s) ? s.Torque : null,
            Column = "torque",
        },
        new()
        {
            Id = "motor_power",
            LabelKey = "MetricMotorPower",
            UnitKey = "UnitWatts",
            Read = s => WheelReports.InMotionV2(s) ? s.MotorPower : null,
            Column = "motor_power",
        },
        new()
        {
            Id = "cpu_temp",
            LabelKey = "MetricCpuTemp",
            UnitKey = "UnitCelsius",
            Read = s => WheelReports.InMotionV2(s) ? s.CpuTemp : null,
            Column = "cpu_temp",
        },
        new()
        {
            Id = "current_limit",
            LabelKey = "MetricCurrentLimit",
            UnitKey = "UnitAmperes",
            Decimals = 1,
            Read = s => WheelReports.InMotionV2(s) ? s.CurrentLimit : null,
            Column = "current_limit",
        },
        new()
        {
            Id = "roll",
            LabelKey = "MetricRoll",
            UnitKey = "UnitDegrees",
            Decimals = 1,
            Read = s => WheelReports.InMotion(s) ? s.Roll : null,
            Column = "roll",
        },
        new()
        {
            Id = "imu_temp",
            LabelKey = "MetricImuTemp",
            UnitKey = "UnitCelsius",
            Read = s => WheelReports.InMotion(s) ? s.ImuTemp : null,
            Column = "imu_temp",
        },
        new()
        {
            Id = "cpu_load",
            LabelKey = "MetricCpuLoad",
            UnitKey = "UnitPercent",
            Read = s => WheelReports.KingSong(s) ? s.CpuLoad : null,
            Column = "cpu_load",
        },
        new()
        {
            Id = "speed_limit",
            LabelKey = "MetricSpeedLimit",
            UnitKey = "UnitKmh",
            Decimals = 1,
            Read = s => WheelReports.KingSong(s) ? s.SpeedLimit : null,
            Column = "speed_limit",
        },
        new()
        {
            Id = "hw_pwm",
            LabelKey = "MetricHardwarePwm",
            UnitKey = "UnitPercent",
            Read = s => WheelReports.KingSong(s) ? s.Output : null,
            Column = "hw_pwm",
        },
        // Код, а не измерение: смысла за ним, кроме «крутится / не крутится», мы не знаем — и не
        // придумываем, ровно как с кодом зарядки на экране «Данные».
        new()
        {
            Id = "fan_status",
            LabelKey = "MetricFanStatus",
            Read = s => WheelReports.KingSong(s) ? s.FanStatus : null,
            Column = "fan_status",
        },
        // Таймер автовыключения сообщает только Veteran, и в таблице телеметрии его нет: он меняется
        // раз в минуту и графиком не бывает.
        new()
        {
            Id = "sleep_timer",
            LabelKey = "TelemetrySleep",
            UnitKey = "UnitSeconds",
            Read = s => WheelReports.Veteran(s) ? s.SleepTimerSec : null,
        },
    ];

    private static readonly Dictionary<string, MetricDescriptor> ById =
        All.ToDictionary(m => m.Id, StringComparer.Ordinal);

    /// <summary>
    /// Величина по имени — или <c>null</c>, если такой нет. Хранимая раскладка ссылается именно
    /// именем, и ссылка на исчезнувшую величину должна быть отличима от величины без значения.
    /// </summary>
    public static MetricDescriptor? Find(string id) => ById.GetValueOrDefault(id);
}
