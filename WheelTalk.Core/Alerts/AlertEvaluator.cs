using System.Reactive.Concurrency;
using System.Reactive.Linq;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Services;

namespace WheelTalk.Core.Alerts;

/// <summary>
/// Turns telemetry into an alert state. Built as a chain of Rx operators over a sliding window
/// rather than a state machine with timers, because the window gives the three properties this
/// needs for free:
///
/// <list type="bullet">
/// <item>the peak over the window catches a spike that lasted a single frame;</item>
/// <item>the window sliding off releases the alert exactly one <see cref="AlertOptions.Hold"/>
/// after the reading stopped being alarming — no separate timer to forget;</item>
/// <item>telemetry that stops (wheel switched off, link lost) silences the alert on its own instead
/// of leaving a signal on forever — but only after <see cref="AlertOptions.Silence"/>, not on the
/// first empty window: the window is barely wider than the gap between readings, and one lost
/// packet used to be enough to cut the alarm off at the very limit.</item>
/// </list>
/// </summary>
public static class AlertEvaluator
{
    /// <summary>
    /// Builds the alert stream. Nothing is subscribed until the caller subscribes; the same
    /// <paramref name="scheduler"/> drives every timing, so tests can run it on virtual time.
    /// </summary>
    public static IObservable<AlertState> Create(
        IObservable<TelemetrySnapshot> telemetry,
        IObservable<ConnectionState> connection,
        AlertOptions options,
        IScheduler scheduler)
    {
        // Пустое окно — это не «тихо», а «пока нечего сказать». Раньше оно понималось буквально, и
        // любой перерыв в телеметрии гасил тревогу: окно (500 мс) у́же промежутка между отсчётами
        // колеса (200 мс) лишь вдвое, поэтому один потерянный пакет опустошал его целиком. На
        // предельной скважности это выглядело как тревога с половинным заполнением — и звук, и
        // рамка пропадали в такт. Теперь состояние держится, пока молчание не затянулось на
        // <see cref="AlertOptions.Silence"/>.
        int emptyWindowsBeforeQuiet = Math.Max(1, (int)Math.Ceiling(options.Silence / options.Step));

        // Оба источника сходятся в один разбор, а не складываются двумя потоками. Сложенные, они
        // спорили: отключение говорило «тихо», а окна следом продолжали выдавать удержанное
        // состояние, и тревога возвращалась через сотню миллисекунд после «Стоп». Здесь разрыв
        // связи не просто вставляет «тихо» в поток, а **обнуляет удержанное** — сказать «тихо»
        // и продолжать помнить обратное нельзя.
        // Окна живут только при связи. Buffer с планировщиком тикает по часам, а не по данным:
        // без гейта потерянное (или ещё не пойманное) колесо держало бы таймер и аллокацию списка
        // десять раз в секунду всё время жизни процесса. StartWith(true) — презумпция связи для
        // источника, который о ней не сообщает вовсе: тестам на виртуальном времени окна нужны с
        // первого тика, а в приложении State — BehaviorSubject и говорит правду сразу же.
        var windows = connection
            .Select(state => state == ConnectionState.Connected)
            .StartWith(true)
            .DistinctUntilChanged()
            .Select(connected => connected
                ? telemetry.Buffer(options.Hold, options.Step, scheduler)
                : Observable.Empty<IList<TelemetrySnapshot>>())
            .Switch()
            .Select(window => (Window: window, LinkLost: false));

        var drops = connection
            .Where(state => state != ConnectionState.Connected)
            .Select(_ => (Window: (IList<TelemetrySnapshot>)[], LinkLost: true));

        return windows
            .Merge(drops)
            .Scan(
                (State: AlertState.Quiet, Empty: 0),
                (previous, input) => input switch
                {
                    { LinkLost: true } => (AlertState.Quiet, emptyWindowsBeforeQuiet),
                    { Window.Count: > 0 } => (Evaluate(input.Window, options), 0),
                    _ => (previous.Empty + 1 >= emptyWindowsBeforeQuiet ? AlertState.Quiet : previous.State,
                          previous.Empty + 1),
                })
            .Select(step => step.State)
            .DistinctUntilChanged();
    }

    /// <summary>Окно, в котором заведомо есть хотя бы один отсчёт: пустые разбираются выше.</summary>
    private static AlertState Evaluate(IList<TelemetrySnapshot> window, AlertOptions options)
    {
        // По модулю. Знака в Pwm с 28.07.2026 нет — множитель GotwayNegative вернулся к
        // оригинальному «0» (AGENTS.md, «Снятые»), — но тревоге направление безразлично и с ним:
        // назад на предельной скважности так же опасно, как вперёд.
        double intensity = Intensity(window.Max(s => Math.Abs(s.Pwm)), options);
        bool speedExceeded = options.SpeedThreshold > 0
            && window.Any(s => Math.Abs(s.SpeedKmh) > options.SpeedThreshold);

        if (intensity > 0 && options.SuppressSpeedWhilePwmAlert)
        {
            speedExceeded = false;
        }

        return new AlertState(intensity, speedExceeded);
    }

    /// <summary>0 below the warning threshold, 1 at the critical one, linear in between.</summary>
    /// <summary>
    /// Сила тревоги по скважности: 0 ниже порога предупреждения, 1 на критическом, между ними —
    /// линейно. Открыто наружу ради воспроизведения записи: плеер показывает тревожные полосы
    /// глазами, не поднимая настоящую тревогу (звук и вибрация остаются за живой сессией), и
    /// вторая копия этой формулы разошлась бы с первой при первой же правке порогов.
    /// </summary>
    public static double Intensity(double pwm, AlertOptions options)
    {
        if (pwm < options.PwmWarning) return 0;
        if (pwm >= options.PwmCritical) return 1;

        // A misconfigured pair (critical at or below warning) would divide by zero or worse; treat
        // it as a plain threshold rather than refusing to run.
        double span = options.PwmCritical - options.PwmWarning;
        return span <= 0 ? 1 : (pwm - options.PwmWarning) / span;
    }
}
