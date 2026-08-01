using Microsoft.Extensions.Time.Testing;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Services;

namespace WheelTalk.Tests.Services;

/// <summary>
/// Величины, которых нет ни в одном снимке телеметрии и которые приборная панель показывает как
/// главное: куда ШИМ идёт, где он только что был, насколько просел пак. Проверяется здесь потому,
/// что на телефоне ошибка в любой из них выглядит как «стрелка ведёт себя странно» — то есть никак.
/// </summary>
public class RideTraceTests
{
    /// <summary>
    /// Райдеру нужен не ШИМ, а время до предела, и производная — единственный способ его показать.
    /// Десять процентов в секунду при пяти кадрах в секунду и есть тот случай, ради которого всё.
    /// </summary>
    [Fact]
    public void The_rate_of_change_is_what_the_trend_arrow_is_drawn_from()
    {
        var (trace, time) = Build(smoothing: 0);

        // Две секунды ровного роста по 10 % в секунду, по пять кадров в секунду.
        for (int i = 0; i <= 10; i++)
        {
            trace.Push(Sample(pwm: 60 + i * 2));
            time.Advance(TimeSpan.FromMilliseconds(200));
        }

        Assert.Equal(10, trace.PwmRate, 1);
        Assert.Equal(100, trace.PwmIn(2), 0);
    }

    /// <summary>
    /// Пик за последние секунды — не то же самое, что максимум за поездку: он показывает, откуда
    /// значение только что упало, и должен уходить со шкалы сам, когда становится историей.
    /// </summary>
    [Fact]
    public void The_recent_peak_leaves_the_scale_once_it_stops_being_recent()
    {
        var (trace, time) = Build(smoothing: 0);

        trace.Push(Sample(pwm: 95));
        time.Advance(TimeSpan.FromSeconds(1));
        trace.Push(Sample(pwm: 60));
        Assert.Equal(95, trace.RecentPwmPeak, 0);

        // Прошло больше окна пика — всплеск стал историей поездки, а не тем, что «только что было».
        time.Advance(RideTrace.PeakWindow);
        trace.Push(Sample(pwm: 60));

        Assert.Equal(60, trace.RecentPwmPeak, 0);
    }

    /// <summary>
    /// Смысл опоры целиком: 77 В сами по себе не говорят, просело колесо или пак разряжен. Опора
    /// снимается только с разгруженного колеса, а под нагрузкой стоит на месте.
    /// </summary>
    [Fact]
    public void The_sag_is_measured_from_the_last_voltage_seen_with_no_load_on_the_wheel()
    {
        var (trace, time) = Build();

        trace.Push(Sample(volts: 84, amps: 0));
        time.Advance(TimeSpan.FromSeconds(1));
        trace.Push(Sample(volts: 76, amps: 40));      // разгон: 8 В просадки

        Assert.Equal(84, trace.NoLoadVoltageV, 1);
        Assert.Equal(8, trace.MaxSagV, 1);
    }

    /// <summary>
    /// Ради чего опора не может быть максимумом за поездку: пак за час честно разряжается на
    /// несколько вольт. С максимумом в качестве опоры разряд складывался бы с просадкой, и «просело
    /// на восемь вольт» к концу поездки означало бы «просело на два и разрядилось на шесть».
    /// </summary>
    [Fact]
    public void A_pack_that_has_simply_discharged_has_not_sagged()
    {
        var (trace, time) = Build();

        trace.Push(Sample(volts: 84, amps: 0));
        time.Advance(TimeSpan.FromMinutes(30));

        // Полчаса спустя, снова на холостом ходу: пак ниже, но ничего не просело.
        trace.Push(Sample(volts: 78, amps: 0));

        Assert.Equal(78, trace.NoLoadVoltageV, 1);
        Assert.Equal(0, trace.MaxSagV, 1);
    }

    /// <summary>
    /// Следы — это «на что колесо оказалось способно», и назад они не ходят. Минимум напряжения
    /// стартует не с нуля: пустой след и «ноль вольт» на шкале выглядели бы одинаково.
    /// </summary>
    [Fact]
    public void The_trip_marks_remember_the_worst_of_it_and_do_not_walk_back()
    {
        var (trace, time) = Build();

        trace.Push(Sample(volts: 84, temperature: 30));
        time.Advance(TimeSpan.FromSeconds(1));
        trace.Push(Sample(volts: 71, temperature: 52, amps: 60));
        time.Advance(TimeSpan.FromSeconds(1));
        trace.Push(Sample(volts: 83, temperature: 31));

        Assert.Equal(71, trace.MinVoltageV, 1);
        Assert.Equal(84, trace.MaxVoltageV, 1);
        Assert.Equal(52, trace.MaxTemperatureC);
    }

    /// <summary>
    /// Новая поездка — новый след. Максимум прошлой поездки на шкале этой означал бы то, чего в ней
    /// не было, а на ленте просадки — чужую метку в том самом месте, куда смотрят.
    /// </summary>
    [Fact]
    public void A_new_ride_starts_with_a_clean_scale()
    {
        var (trace, time) = Build();

        trace.Push(Sample(volts: 84, pwm: 97, temperature: 55));
        time.Advance(TimeSpan.FromSeconds(1));

        trace.Reset();
        Assert.False(trace.HasData);

        trace.Push(Sample(volts: 84, pwm: 40, temperature: 25));

        Assert.Equal(40, trace.RecentPwmPeak, 0);
        Assert.Equal(25, trace.MaxTemperatureC);
        Assert.Equal(0, trace.PwmRate, 1);
    }

    /// <summary>
    /// Сглаживание врёт про скачки, а именно скачок и важен, — поэтому это ручка, и ноль в ней
    /// должен отдавать сырое значение, а не «почти сырое».
    /// </summary>
    [Fact]
    public void Smoothing_switched_off_gives_back_exactly_what_the_wheel_said()
    {
        var (trace, time) = Build(smoothing: 0);

        trace.Push(Sample(pwm: 60));
        time.Advance(TimeSpan.FromMilliseconds(200));
        trace.Push(Sample(pwm: 96));

        Assert.Equal(96, trace.Pwm, 1);
    }

    /// <summary>
    /// И обратное: со сглаживанием ступенька приходит не сразу. Одна постоянная времени — это
    /// 63 % пути, и это ровно та задержка, которой платят за спокойную ленту.
    /// </summary>
    [Fact]
    public void Smoothing_switched_on_lets_a_step_arrive_over_its_time_constant()
    {
        var (trace, time) = Build(smoothing: 0.2);

        trace.Push(Sample(pwm: 60));
        time.Advance(TimeSpan.FromMilliseconds(200));
        trace.Push(Sample(pwm: 100));

        Assert.Equal(60 + 40 * 0.632, trace.Pwm, 1);
    }

    private static (RideTrace Trace, FakeTimeProvider Time) Build(double smoothing = 0.15)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 28, 20, 5, 0, TimeSpan.FromHours(3)));
        return (new RideTrace(time) { SmoothingSeconds = smoothing }, time);
    }

    private static TelemetrySnapshot Sample(
        double pwm = 50, double volts = 84, double amps = 0, int temperature = 25, double speed = 20) =>
        new()
        {
            Pwm = pwm,
            VoltageRaw = (int)Math.Round(volts * 100),
            CurrentRaw = (int)Math.Round(amps * 100),
            TemperatureRaw = temperature * 100,
            SpeedRaw = (int)Math.Round(speed * 100),
            WheelType = WheelType.Veteran,
        };
}
