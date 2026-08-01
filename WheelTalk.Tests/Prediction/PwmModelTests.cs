using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Tests.TestSupport;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Playback;

namespace WheelTalk.Tests.Prediction;

/// <summary>
/// План 9, фаза 2: сходится ли формула ШИМ вообще. Sherman L сообщает скважность сам, поэтому на
/// его записях есть правда, с которой можно сверить расчёт; не сойдётся здесь — на MTen3, где
/// сверять не с чем, подгонять нечего.
/// <para>
/// Цифры в утверждениях — <b>измеренные</b>, а не назначенные: гейт фазы 2 так и сформулирован —
/// погрешность называется по факту. Тест сторожит, чтобы измеренное не уехало незаметно, а не
/// провозглашает, каким ему быть. Разбор чисел — в плане 9, §2.1.2.
/// </para>
/// </summary>
public class PwmModelTests
{
    /// <summary>Значения WheelLog по умолчанию: 50 км/ч при 84 В, множитель 0,9.</summary>
    private static AppWheelConfig Defaults() => new()
    {
        RotationSpeed = 500,
        RotationVoltage = 840,
        PowerFactor = 90,
        HwPwm = true,
    };

    /// <summary>
    /// Форма зависимости держится: медиана модуля ошибки — единицы процентных пунктов на двух
    /// минутах настоящей езды. Это и есть ответ фазы 2 — формулу можно нести дальше.
    /// </summary>
    [Fact]
    public async Task The_wheellog_formula_tracks_the_duty_cycle_a_Sherman_L_reports()
    {
        var result = PwmModelReport.Compare(await RealRide(), Defaults());

        Assert.True(result.Samples > 2000, $"отсчётов под нагрузкой всего {result.Samples}");
        Assert.InRange(result.MedianAbsErrorPoints, 2.5, 4.0);   // измерено 3,14
    }

    /// <summary>
    /// И ровно поэтому её нельзя нести дальше как есть. Разброс — 10,5 п.п. на 95-м перцентиле и
    /// 18,7 в худшей точке; при пороге тревоги 80 % это разница между «спокойно» и «сейчас
    /// сорвётся». Приближение годится показывать, но не тревожить по нему без калибровки колеса.
    /// </summary>
    [Fact]
    public async Task The_scatter_is_too_wide_to_raise_an_alarm_on()
    {
        var result = PwmModelReport.Compare(await RealRide(), Defaults());

        Assert.InRange(result.P95AbsErrorPoints, 9.0, 12.0);     // измерено 10,50
        Assert.InRange(result.MaxAbsErrorPoints, 15.0, 22.0);    // измерено 18,65
    }

    /// <summary>
    /// Знак ошибки, и он оказался не тем, что предполагалось. На MTen3 расчёт с измеренным
    /// коэффициентом уходил до 110 %, то есть врал в безопасную сторону, — отсюда и мысль, что
    /// множитель 0,9 для этого и стоит. На Sherman L с теми же значениями по умолчанию он врёт
    /// **в другую**: занижает скважность, а значит промолчит там, где надо было предупредить.
    /// По §0 плана такая ошибка хуже отсутствия оценки, и переносить множитель между колёсами
    /// нельзя даже в качестве запаса.
    /// </summary>
    [Fact]
    public async Task With_the_shipped_defaults_the_estimate_errs_optimistically_on_this_wheel()
    {
        var result = PwmModelReport.Compare(await RealRide(), Defaults());

        Assert.InRange(result.MedianSignedErrorPoints, -3.0, -1.0);   // измерено −2,15
    }

    /// <summary>
    /// Главное число. Раскрут в воздухе 28.07.2026 дал k = 1,00 км/ч на вольт (план 9 §2.1.1), а
    /// та же величина, выведенная из езды под райдером, — вдвое меньше. План называл раскрут
    /// верхней границей, «от которой реальность отклоняется вниз»; отклонение оказалось не
    /// поправкой, а множителем два, и как отправная точка для модели под нагрузкой раскрут
    /// поэтому не годится.
    /// </summary>
    [Fact]
    public async Task The_coefficient_the_ride_implies_is_half_the_one_measured_in_the_air()
    {
        var result = PwmModelReport.Compare(await RealRide(), Defaults());

        Assert.InRange(result.ImpliedK, 0.45, 0.55);   // измерено 0,503 против 1,00 на раскруте
    }

    /// <summary>
    /// Колесо, которое сообщает скважность, — единственное, на чём эта сверка имеет смысл. Молча
    /// посчитать её там, где сверять не с чем, хуже, чем отказаться.
    /// </summary>
    [Fact]
    public async Task Comparing_against_a_wheel_that_computes_nothing_is_refused()
    {
        var snapshots = await RealRide();
        var noHardwarePwm = Defaults();
        noHardwarePwm.HwPwm = false;

        Assert.Throws<ArgumentException>(() => PwmModelReport.Compare(snapshots, noHardwarePwm));
    }

    private static async Task<List<TelemetrySnapshot>> RealRide()
    {
        var defaults = Defaults();
        var harness = DecoderHarness.ForVeteran(c =>
        {
            c.GotwayNegative = "0";
            c.RotationSpeed = defaults.RotationSpeed;
            c.RotationVoltage = defaults.RotationVoltage;
            c.PowerFactor = defaults.PowerFactor;
        });

        var snapshots = new List<TelemetrySnapshot>();
        string fixture = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "shermanl_raw_ride_20260728.csv");
        var transport = new ReplayTransport(
            () => new StreamReader(fixture), TimeProvider.System, NullLogger<ReplayTransport>.Instance);
        transport.DataReceived += frame =>
        {
            harness.Decoder.Feed(frame);
            var snapshot = harness.Snapshot();
            if (snapshot.VoltageRaw != 0) snapshots.Add(snapshot);
        };
        await transport.PlayAsync(realtime: false);

        // Sherman L сообщает скважность сам — декодер выставляет HwPwm по протоколу, и это
        // проверяется здесь, а не предполагается.
        Assert.True(harness.Config.HwPwm, "запись не от колеса с аппаратной скважностью");
        return snapshots;
    }
}
