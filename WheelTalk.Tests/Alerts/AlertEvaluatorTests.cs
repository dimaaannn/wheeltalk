using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Reactive.Testing;
using WheelTalk.Core.Alerts;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Services;

namespace WheelTalk.Tests.Alerts;

/// <summary>
/// The alert engine decides when the rider gets shouted at, so the cases worth pinning down are
/// the ones that are wrong in opposite directions: a spike that must not be missed, and a signal
/// that must not stay on. Everything runs on virtual time — no test waits for a real millisecond.
///
/// Timing to keep in mind while reading: with a 500 ms window stepped every 100 ms, the first
/// verdict lands at 500 ms and each one after that covers the preceding 500 ms.
/// </summary>
public class AlertEvaluatorTests
{
    private static readonly AlertOptions Options = new()
    {
        Hold = TimeSpan.FromMilliseconds(500),
        Step = TimeSpan.FromMilliseconds(100),
        PwmWarning = 40,
        PwmCritical = 60,
        SpeedThreshold = 10,
    };

    /// <summary>
    /// Zero means off, as it does in the original — and off is what it ships with. Treating it as
    /// "warn above zero" would put a speed alert on every ride out of the box.
    /// </summary>
    [Fact]
    public void A_zero_speed_threshold_switches_the_speed_alert_off()
    {
        var scheduler = new TestScheduler();
        var telemetry = new Subject<TelemetrySnapshot>();
        var options = new AlertOptions
        {
            Hold = Options.Hold,
            Step = Options.Step,
            SpeedThreshold = 0,
        };
        var states = Record(telemetry, scheduler, options);

        Feed(scheduler, telemetry, 100, Speed(40));
        scheduler.AdvanceTo(TimeSpan.FromSeconds(1).Ticks);

        Assert.DoesNotContain(states, s => s.SpeedExceeded);
    }

    /// <summary>
    /// Колесо отдаёт скорость знаковой, и запись этот знак хранит — по нему видна рекуперация.
    /// Тревога обязана смотреть на модуль: задом на предельной скважности так же опасно, как
    /// вперёд, а сравнение со знаком просто никогда бы не сработало.
    /// </summary>
    [Fact]
    public void Riding_backwards_still_raises_the_pwm_alert()
    {
        var scheduler = new TestScheduler();
        var telemetry = new Subject<TelemetrySnapshot>();
        var states = Record(telemetry, scheduler);

        Feed(scheduler, telemetry, 100, new TelemetrySnapshot { Pwm = -80, SpeedRaw = -3000 });
        scheduler.AdvanceTo(TimeSpan.FromSeconds(1).Ticks);

        Assert.Contains(states, s => s.PwmIntensity > 0);
    }

    [Fact]
    public void A_single_spike_raises_the_alert_and_holds_it()
    {
        var scheduler = new TestScheduler();
        var telemetry = new Subject<TelemetrySnapshot>();
        var states = Record(telemetry, scheduler);

        Feed(scheduler, telemetry, 100, Pwm(80));
        // Спокойные отсчёты идут дальше, как на едущем колесе. Раньше их здесь не было, и тревога
        // спадала от **опустевшего** окна, а не от того, что всплеск из него вышел, — то есть тест
        // держался на дефекте, который потом и нашёлся на телефоне.
        for (int at = 150; at <= 900; at += 100)
        {
            Feed(scheduler, telemetry, at, Pwm(0));
        }

        // One frame out of a whole second of quiet ones, and the alert is up.
        AdvanceTo(scheduler, 600);
        Assert.Equal(1.0, states[^1].PwmIntensity);

        // ...and stays up until the spike falls out of the window, not a moment longer.
        AdvanceTo(scheduler, 800);
        Assert.Equal(0.0, states[^1].PwmIntensity);
    }

    [Fact]
    public void Silence_from_the_wheel_clears_the_alert_by_itself()
    {
        var scheduler = new TestScheduler();
        var telemetry = new Subject<TelemetrySnapshot>();
        var states = Record(telemetry, scheduler);

        Feed(scheduler, telemetry, 100, Pwm(100));
        AdvanceTo(scheduler, 600);
        Assert.True(states[^1].PwmAlarming);

        // No further telemetry at all — the wheel was switched off mid-alarm. Nothing will arrive
        // to clear the alert, so the engine has to do it on time alone.
        AdvanceTo(scheduler, 5000);
        Assert.False(states[^1].PwmAlarming);
    }

    [Theory]
    [InlineData(39, 0.0)]
    [InlineData(40, 0.0)]      // at the threshold there is nothing to warn about yet
    [InlineData(50, 0.5)]
    [InlineData(60, 1.0)]
    [InlineData(95, 1.0)]
    public void Intensity_rises_linearly_between_the_two_thresholds(double pwm, double expected)
    {
        var scheduler = new TestScheduler();
        var telemetry = new Subject<TelemetrySnapshot>();
        var states = Record(telemetry, scheduler);

        Feed(scheduler, telemetry, 100, Pwm(pwm));
        AdvanceTo(scheduler, 600);

        Assert.Equal(expected, states[^1].PwmIntensity, 3);
    }

    [Fact]
    public void Speed_alone_raises_the_soft_alert()
    {
        var scheduler = new TestScheduler();
        var telemetry = new Subject<TelemetrySnapshot>();
        var states = Record(telemetry, scheduler);

        Feed(scheduler, telemetry, 100, Speed(25));
        AdvanceTo(scheduler, 600);

        Assert.True(states[^1].SpeedExceeded);
        Assert.False(states[^1].PwmAlarming);
    }

    [Fact]
    public void The_speed_alert_steps_aside_for_the_pwm_one()
    {
        var scheduler = new TestScheduler();
        var telemetry = new Subject<TelemetrySnapshot>();
        var states = Record(telemetry, scheduler);

        Feed(scheduler, telemetry, 100, SpeedAndPwm(25, 70));
        AdvanceTo(scheduler, 600);

        Assert.True(states[^1].PwmAlarming);
        Assert.False(states[^1].SpeedExceeded);
    }

    [Fact]
    public void Losing_the_link_silences_everything_at_once()
    {
        var scheduler = new TestScheduler();
        var telemetry = new Subject<TelemetrySnapshot>();
        var connection = new Subject<ConnectionState>();
        var states = new List<AlertState>();
        using var subscription = AlertEvaluator
            .Create(telemetry, connection, Options, scheduler)
            .Subscribe(states.Add);

        Feed(scheduler, telemetry, 100, Pwm(100));
        AdvanceTo(scheduler, 600);
        Assert.True(states[^1].PwmAlarming);

        scheduler.ScheduleAbsolute(TimeSpan.FromMilliseconds(650).Ticks,
            () => connection.OnNext(ConnectionState.Reconnecting));
        AdvanceTo(scheduler, 660);

        // Without waiting for the window to drain: the link is known to be gone.
        Assert.False(states[^1].Any);
    }

    /// <summary>
    /// Дефект, найденный на телефоне: тревога на предельной скважности пропадала примерно
    /// наполовину времени — и звук, и рамка гасли в такт. Причина не в звуке, а здесь: окно (500 мс)
    /// шире промежутка между отсчётами колеса всего вдвое, поэтому редкие отсчёты регулярно
    /// оставляли его пустым, а пустое окно понималось как «тихо».
    /// <para>
    /// Отсчёты раз в 800 мс — это реплей на четверти скорости, но то же самое даёт на настоящем
    /// колесе одна потерянная посылка. Тревога, которая гаснет от пропущенного пакета, — это
    /// тревога, которая гаснет ровно тогда, когда она нужна.
    /// </para>
    /// </summary>
    [Fact]
    public void Readings_arriving_slower_than_the_window_keep_the_alarm_on()
    {
        var scheduler = new TestScheduler();
        var telemetry = new Subject<TelemetrySnapshot>();
        var states = Record(telemetry, scheduler);

        for (int at = 100; at <= 3300; at += 800)
        {
            Feed(scheduler, telemetry, at, Pwm(100));
        }

        AdvanceTo(scheduler, 3400);

        // Ни одного провала в тишину: между отсчётами состояние держится.
        Assert.DoesNotContain(states, state => !state.Any);
        Assert.True(states[^1].PwmIntensity >= 1);
    }

    /// <summary>
    /// Но держится не вечно. Замолчавшее колесо обязано отпустить тревогу само — иначе сигнал
    /// переживёт то, из-за чего он поднялся, а это ровно то, чего состояние вместо событий и
    /// должно не допускать.
    /// </summary>
    [Fact]
    public void Telemetry_that_stops_for_good_releases_the_alarm()
    {
        var scheduler = new TestScheduler();
        var telemetry = new Subject<TelemetrySnapshot>();
        var options = new AlertOptions
        {
            Hold = Options.Hold,
            Step = Options.Step,
            PwmWarning = Options.PwmWarning,
            PwmCritical = Options.PwmCritical,
            Silence = TimeSpan.FromSeconds(1),
        };
        var states = Record(telemetry, scheduler, options);

        Feed(scheduler, telemetry, 100, Pwm(100));
        AdvanceTo(scheduler, 600);
        Assert.True(states[^1].PwmAlarming);

        // Дальше тишина — и через секунду после последнего окна с данными тревога отпускает.
        AdvanceTo(scheduler, 2200);
        Assert.False(states[^1].Any);
    }

    /// <summary>
    /// «Стоп» обязан гасить тревогу насовсем, а не на одно мгновение. Пока разрыв связи лишь
    /// вставлял «тихо» в общий поток, удержанное состояние возвращало тревогу через сотню
    /// миллисекунд — на экране это выглядело как «остановил, а оно всё звучит».
    /// </summary>
    [Fact]
    public void Losing_the_link_clears_the_held_state_and_it_does_not_come_back()
    {
        var scheduler = new TestScheduler();
        var telemetry = new Subject<TelemetrySnapshot>();
        var connection = new Subject<ConnectionState>();
        var states = new List<AlertState>();
        AlertEvaluator.Create(telemetry, connection, Options, scheduler).Subscribe(states.Add);

        Feed(scheduler, telemetry, 100, Pwm(100));
        AdvanceTo(scheduler, 600);
        Assert.True(states[^1].PwmAlarming);

        scheduler.ScheduleAbsolute(TimeSpan.FromMilliseconds(650).Ticks,
            () => connection.OnNext(ConnectionState.Disconnected));

        // Дальше только пустые окна — и ни одно не должно воскресить удержанное.
        AdvanceTo(scheduler, 1500);
        Assert.False(states[^1].Any);
        Assert.False(states.SkipWhile(state => state.Any).Any(state => state.Any));
    }

    private static List<AlertState> Record(
        IObservable<TelemetrySnapshot> telemetry, TestScheduler scheduler, AlertOptions? options = null)
    {
        var states = new List<AlertState>();
        AlertEvaluator
            .Create(telemetry, Observable.Never<ConnectionState>(), options ?? Options, scheduler)
            .Subscribe(states.Add);

        return states;
    }

    private static void Feed(TestScheduler scheduler, Subject<TelemetrySnapshot> telemetry, int atMilliseconds, TelemetrySnapshot snapshot) =>
        scheduler.ScheduleAbsolute(TimeSpan.FromMilliseconds(atMilliseconds).Ticks, () => telemetry.OnNext(snapshot));

    private static void AdvanceTo(TestScheduler scheduler, int milliseconds) =>
        scheduler.AdvanceTo(TimeSpan.FromMilliseconds(milliseconds).Ticks);

    private static TelemetrySnapshot Pwm(double percent) => new() { Pwm = percent };

    private static TelemetrySnapshot Speed(double kmh) => new() { SpeedRaw = (int)(kmh * 100) };

    private static TelemetrySnapshot SpeedAndPwm(double kmh, double percent) =>
        new() { SpeedRaw = (int)(kmh * 100), Pwm = percent };
}
