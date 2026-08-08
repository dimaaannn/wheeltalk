using WheelTalk.Core.Battery;
using WheelTalk.Core.Contracts;

namespace WheelTalk.Dashboard.Droid;

/// <summary>
/// Всё, что рисует панель, и ничего сверх того. Отдельный тип от <see cref="TelemetrySnapshot"/>
/// нужен по двум причинам: у панели есть величины, которых нет в телеметрии (производная ШИМ,
/// интенсивность тревоги), и её можно нарисовать без колеса вообще — стенд подаёт сюда и
/// записанную поездку, и придуманный сценарий.
/// <para>
/// Знаки убраны: колесо отдаёт скорость и ШИМ знаковыми, и в записи знак нужен — он показывает
/// рекуперацию. На экране от него толку нет, 22 % назад так же близко к пределу, как вперёд.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/DashboardReading.cs</c> без изменений — MAUI-типов в
/// этом файле не было изначально, поменялось только пространство имён.
/// </para>
/// </summary>
public sealed record DashboardReading
{
    /// <summary>Ниже этого колесо считается стоящим, км/ч. Компоновка от контекста смотрит сюда.</summary>
    private const double StandingSpeed = 0.5;

    public static readonly DashboardReading Idle = new();

    public double SpeedKmh { get; init; }
    public double Pwm { get; init; }

    /// <summary>
    /// Скорость изменения ШИМ, процентов в секунду. То, из чего рисуется вектор тренда: райдеру
    /// нужен не ШИМ, а время до 100 %, и производная — единственный способ его показать.
    /// </summary>
    public double PwmRate { get; init; }

    /// <summary>Пик ШИМ за последние секунды — «где значение только что было», а не за всю поездку.</summary>
    public double RecentPwmPeak { get; init; }

    /// <summary>Ускорение, км/ч в секунду. То же назначение, что у <see cref="PwmRate"/>: показать тенденцию.</summary>
    public double SpeedRate { get; init; }

    public double MaxPwm { get; init; }
    public double TopSpeedKmh { get; init; }
    public int Battery { get; init; }
    public double VoltageV { get; init; }
    public int TemperatureC { get; init; }
    public double TripKm { get; init; }

    /// <summary>Самое низкое напряжение за поездку — след, оставленный самой тяжёлой просадкой.</summary>
    public double MinVoltageV { get; init; }

    /// <summary>Самое высокое за поездку. Вместе с минимумом задаёт масштаб шкалы напряжения.</summary>
    public double MaxVoltageV { get; init; }

    /// <summary>
    /// Опорное напряжение без нагрузки: последнее, виденное на околонулевом токе. Просадка — это
    /// разница между ним и текущим, и без опоры её не посчитать: 77 В сами по себе не говорят,
    /// просело колесо или пак просто разряжен.
    /// </summary>
    public double NoLoadVoltageV { get; init; }

    /// <summary>Самая глубокая просадка за поездку, вольт. Не «сколько сейчас», а «на что способно».</summary>
    public double MaxSagV { get; init; }

    /// <summary>
    /// Ряд ячеек и его источник. Лента на банку делит на это число, и только когда источник — сам
    /// человек: показ не гадает, он делит на записанное в настройке колеса (план 27 §27.4).
    /// </summary>
    public CellCount PackCells { get; init; }

    public int MaxTemperatureC { get; init; }

    /// <summary>
    /// 0 ниже порога предупреждения, 1 на критическом — то же число, что <c>AlertState.PwmIntensity</c>.
    /// Панель решает сама, как его показать.
    /// </summary>
    public double AlertIntensity { get; init; }

    public bool Standing => SpeedKmh < StandingSpeed;

    /// <summary>Куда придёт ШИМ через <paramref name="seconds"/>, если производная сохранится.</summary>
    public double PwmIn(double seconds) => Pwm + PwmRate * seconds;

    public static DashboardReading From(TelemetrySnapshot snapshot, double pwmRate, double alertIntensity,
        double recentPwmPeak = 0) => new()
    {
        SpeedKmh = Math.Abs(snapshot.SpeedKmh),
        Pwm = Math.Abs(snapshot.Pwm),
        PwmRate = pwmRate,
        RecentPwmPeak = recentPwmPeak,
        MaxPwm = Math.Abs(snapshot.MaxPwm),
        TopSpeedKmh = Math.Abs(snapshot.TopSpeedKmh),
        Battery = snapshot.Battery,
        VoltageV = snapshot.VoltageV,
        TemperatureC = snapshot.TemperatureC,
        TripKm = snapshot.WheelDistanceKm,
        AlertIntensity = alertIntensity,
        PackCells = snapshot.PackCells,
    };
}
