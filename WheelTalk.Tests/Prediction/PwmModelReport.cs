using System.Globalization;
using System.Text;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Ports;

namespace WheelTalk.Tests.Prediction;

/// <summary>
/// Проверка формулы ШИМ по записанной поездке — план 9, фаза 2. Считает две вещи и не делает
/// третьей: не подставляет получившееся ни в какие настройки.
/// <para>
/// Первое — <b>сходится ли форма зависимости вообще</b>. Sherman L сообщает скважность сам, так
/// что на его записях есть правда, с которой можно сверить расчёт. Не сойдётся здесь —
/// подгонять на MTen3, где сверять не с чем, нечего.
/// </para>
/// <para>
/// Второе — какой коэффициент <c>k</c> (км/ч на вольт) следует из самих данных. Из формулы
/// WheelLog <c>скважность = скорость / (k · U · множитель)</c> обратным ходом:
/// <c>k · множитель = скорость / (скважность · U)</c>. Медиана по отсчётам, а не среднее:
/// один кадр с почти нулевой скважностью даёт деление на почти ноль и уносит среднее куда угодно.
/// </para>
/// </summary>
public static class PwmModelReport
{
    /// <summary>
    /// Ниже этой скважности отсчёт не о чем: делитель близок к нулю, и относительная ошибка
    /// улетает в сотни процентов, ничего не говоря о формуле. Порог на стороне сообщённого
    /// значения, а не расчётного, — сравнивать надо с правдой.
    /// </summary>
    private const double MinPwmPercent = 5.0;

    /// <summary>Медленнее этого колесо стоит или катится, и скорость шумит сильнее, чем значит.</summary>
    private const double MinSpeedKmh = 3.0;

    public sealed record Result(
        int Samples,
        double MedianAbsErrorPoints,
        double P95AbsErrorPoints,
        double MaxAbsErrorPoints,
        double MedianSignedErrorPoints,
        double ImpliedK,
        double ConfiguredK)
    {
        /// <summary>Ошибка в процентных пунктах скважности: 3 означает «81 % вместо 84 %».</summary>
        public override string ToString()
        {
            var text = new StringBuilder();
            text.AppendLine(CultureInfo.InvariantCulture, $"отсчётов: {Samples}");
            text.AppendLine(CultureInfo.InvariantCulture, $"ошибка расчёта против сообщённого, процентные пункты:");
            text.AppendLine(CultureInfo.InvariantCulture, $"  медиана модуля {MedianAbsErrorPoints:F2}, 95-й перцентиль {P95AbsErrorPoints:F2}, максимум {MaxAbsErrorPoints:F2}");
            text.AppendLine(CultureInfo.InvariantCulture, $"  медиана со знаком {MedianSignedErrorPoints:+0.00;-0.00;0.00} (плюс — расчёт выше правды)");
            text.AppendLine(CultureInfo.InvariantCulture, $"k из данных: {ImpliedK:F3} км/ч на вольт (в настройках {ConfiguredK:F3})");
            return text.ToString();
        }
    }

    /// <summary>
    /// <paramref name="snapshots"/> — отсчёты колеса, которое сообщает скважность само; иначе
    /// сверять не с чем и метод бросает. Настройки берутся те же, что у декодера.
    /// </summary>
    public static Result Compare(IReadOnlyList<TelemetrySnapshot> snapshots, IWheelConfig config)
    {
        if (!config.HwPwm)
        {
            throw new ArgumentException(
                "Сверять расчёт не с чем: колесо не сообщает скважность. Смысл проверки — колесо, " +
                "у которого правда известна.", nameof(config));
        }

        double configuredK = config.RotationSpeed / (double)config.RotationVoltage;
        double powerFactor = config.PowerFactor / 100.0;

        var errors = new List<double>();
        var impliedK = new List<double>();

        foreach (var s in snapshots)
        {
            double speed = Math.Abs(s.SpeedKmh);
            double reported = s.Pwm;
            if (reported < MinPwmPercent || speed < MinSpeedKmh || s.VoltageV <= 0) continue;

            double calculated = speed / (configuredK * s.VoltageV * powerFactor) * 100.0;
            errors.Add(calculated - reported);
            impliedK.Add(speed / (reported / 100.0 * s.VoltageV));
        }

        if (errors.Count == 0)
        {
            throw new ArgumentException("В записи нет ни одного отсчёта под нагрузкой.", nameof(snapshots));
        }

        var absolute = errors.Select(Math.Abs).Order().ToList();
        return new Result(
            errors.Count,
            Median(absolute),
            Percentile(absolute, 0.95),
            absolute[^1],
            Median(errors.Order().ToList()),
            Median(impliedK.Order().ToList()),
            // Множитель мощности входит в подгонку, поэтому и здесь: сравниваются сопоставимые
            // величины, а не k против k·множитель.
            configuredK * powerFactor);
    }

    private static double Median(List<double> sorted) =>
        sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

    private static double Percentile(List<double> sorted, double fraction) =>
        sorted[Math.Clamp((int)Math.Ceiling(fraction * sorted.Count) - 1, 0, sorted.Count - 1)];
}
