using WheelTalk.Core.Battery;
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
/// <para>
/// <b>Знаков после запятой — столько, сколько несёт смысл при взгляде на ходу</b> (решение
/// владельца 10.08.2026; ШИМ, скорость и напряжение названы им поимённо). Отсюда три правила, по
/// которым назначено остальное:
/// <list type="bullet">
/// <item>что колесо сообщает целым (проценты, градусы) — целым и показываем: дробь там рисованная;</item>
/// <item>дробный знак живёт, пока он меняется медленнее взгляда, — десятые у скоростей, углов,
/// токов, напряжения пакета и пробегов; сотые остались одной величине — вольту на банку, где в
/// целом вольте умещается весь пакет от пустого до полного;</item>
/// <item>производная величина берёт размерность своей основной (максимум ШИМ — как ШИМ, предел
/// скорости — как скорость): пара, читаемая рядом, не вправе разойтись видом.</item>
/// </list>
/// Своё число человек ставит плитке сам (<see cref="MetricRounding"/>) — здесь именно умолчание.
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
        // ШИМ — целыми (владелец, 10.08.2026): десятая доля процента не говорит ничего, а разряд
        // ширины забирает у всего класса плиток.
        new()
        {
            Id = "pwm",
            LabelKey = "TelemetryPwm",
            UnitKey = "UnitPercent",
            Read = s => s.Pwm,
            Column = "pwm",
        },
        // Максимумы колесо ведёт само, и в таблицу телеметрии они не пишутся: график по ним — это
        // график исходной величины. Живьём есть, графика нет.
        //
        // В выборе плитки их больше нет (решение владельца 11.08.2026): мин-макс на «Цифрах» —
        // это вид плитки «крайнее значение», один на все величины и со своим сбросом, а не вторая
        // величина рядом с основной. Из каталога величины не убраны: раскладка, собранная до этого
        // решения, обязана читаться и показывать их как прежде.
        new()
        {
            Id = "max_pwm",
            LabelKey = "TelemetryMaxPwm",
            UnitKey = "UnitPercent",
            Read = s => s.MaxPwm,
            Offered = false,
        },
        new()
        {
            Id = "top_speed",
            LabelKey = "TelemetryTopSpeed",
            UnitKey = "UnitKmh",
            Decimals = 1,
            Read = s => s.TopSpeedKmh,
            Offered = false,
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
        // Напряжение пакета — десятыми (владелец, 10.08.2026): сотая доля вольта на восьмидесяти
        // вольтах — шум. Сотые отданы соседней величине — вольту на банку.
        new()
        {
            Id = "voltage",
            LabelKey = "TelemetryVoltage",
            UnitKey = "UnitVolts",
            Decimals = 1,
            Read = s => s.VoltageV,
            Column = "voltage",
        },
        // Вольт на банку — сотыми (владелец, 10.08.2026): здесь сотая доля и есть весь разговор,
        // от 3,20 до 4,20 В умещается всё состояние пакета.
        //
        // Величина <b>считается</b>, а не приходит кадром: пакет делится на ряд, которым декодер
        // считал этот же кадр (план 27). Ряда нет — плитка молчит прочерком, и это обычный день у
        // колеса без BMS и без числа в настройках. Неправдоподобное частное (ряд неверен) молчит
        // тем же прочерком: печатать райдеру 4,9 В на банку нельзя — см. CellVoltageStatus.
        //
        // Колонки в таблице телеметрии у неё нет — значит нет и графика: база хранит напряжение
        // пакета, а ряд к записи не приложен.
        new()
        {
            Id = "cell_voltage",
            LabelKey = "MetricCellVoltage",
            UnitKey = "UnitVolts",
            Decimals = 2,
            Read = s => CellVoltageResolver.Resolve(s.PackCells, s.VoltageV) is { IsKnown: true } cell
                ? cell.Volts
                : null,
        },
        // Токи — десятыми: величина в десятках ампер, и сотая меняется быстрее, чем на неё
        // успевают взглянуть.
        new()
        {
            Id = "current",
            LabelKey = "TelemetryCurrent",
            UnitKey = "UnitAmperes",
            Decimals = 1,
            Read = s => s.CurrentA,
            Column = "current",
        },
        new()
        {
            Id = "phase_current",
            LabelKey = "TelemetryPhaseCurrent",
            UnitKey = "UnitAmperes",
            Decimals = 1,
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
            // Проценты хранятся целыми, а не сотыми: колесо сообщает их целыми.
            ColumnScale = 1,
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
            // Пробег поездки — десятыми (владелец, 10.08.2026): сотня метров на плитке видна, а
            // десяток нет. Одометру и того не надо — тысячи километров целыми.
            Decimals = 1,
            Read = s => s.WheelDistanceKm,
            Column = "distance",
            // Пробег база держит в метрах — так его сообщает колесо.
            ColumnScale = 0.001,
        },
        new()
        {
            Id = "distance_from_start",
            LabelKey = "TelemetryFromStart",
            UnitKey = "UnitKm",
            Decimals = 1,
            Read = s => s.DistanceFromStartKm,
        },
        new()
        {
            Id = "totaldistance",
            LabelKey = "TelemetryTotal",
            UnitKey = "UnitKm",
            Read = s => s.TotalDistanceKm,
            Column = "totaldistance",
            ColumnScale = 0.001,
        },

        // ---- Что сообщает одно семейство ---------------------------------------------------
        //
        // Одиннадцать величин из §1 плана. У остальных семейств их нет вовсе — прочерк, а не ноль.
        new()
        {
            Id = "torque",
            LabelKey = "MetricTorque",
            UnitKey = "UnitNewtonMetres",
            // Момент считается из тока, и сотая доля Н·м дрожит вместе с ним.
            Decimals = 1,
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
            // Градусы целыми: колесо сообщает их без дробной части (см. TelemetrySnapshot.CpuTemp).
            ColumnScale = 1,
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
            ColumnScale = 1,
        },
        new()
        {
            Id = "cpu_load",
            LabelKey = "MetricCpuLoad",
            UnitKey = "UnitPercent",
            Read = s => WheelReports.KingSong(s) ? s.CpuLoad : null,
            Column = "cpu_load",
            ColumnScale = 1,
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
