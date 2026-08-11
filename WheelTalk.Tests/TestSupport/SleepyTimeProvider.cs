namespace WheelTalk.Tests.TestSupport;

/// <summary>
/// Время, которое умеет <b>замораживать процесс</b>: часы уходят вперёд, а таймеры при этом не
/// тикают — и просыпаются одним запоздалым тиком, как оно и бывает после сна экрана или Doze.
/// <para>
/// <b>Почему не <c>FakeTimeProvider</c>.</b> Его <c>Advance</c> двигает часы и тут же прогоняет все
/// сроки, догоняя пропущенные периоды: это модель <b>работающего</b> процесса, у которого молчит
/// колесо. Заморозка — случай ровно обратный: колесо говорит, а слушать некому, и тиков нет вовсе.
/// Отличить их и обязан сторож (<c>WheelSession.CheckFrames</c>), значит и в замке эти два случая
/// должны быть разными, а не одним.
/// </para>
/// <para>
/// Сроки здесь не планируются вовсе: тик даёт сам замок (<see cref="Tick"/>) — так видно, где
/// сторож просыпается, а где спит.
/// </para>
/// </summary>
public sealed class SleepyTimeProvider : TimeProvider
{
    private readonly Lock _lock = new();
    private readonly List<Handle> _timers = [];

    private DateTimeOffset _now = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock) return _now;
    }

    /// <summary>Отсчёт в тиках — тогда частота совпадает с ходом часов и пересчёт нигде не врёт.</summary>
    public override long GetTimestamp() => GetUtcNow().Ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>Процесс заморожен: часы ушли вперёд, а тиков не было ни одного.</summary>
    public void Sleep(TimeSpan span)
    {
        lock (_lock) _now += span;
    }

    /// <summary>Один тик сторожа — тот самый, что приходит на пробуждении.</summary>
    public void Tick()
    {
        Handle[] awake;
        lock (_lock) awake = [.. _timers];

        foreach (var timer in awake) timer.Fire();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var handle = new Handle(this, callback, state);
        lock (_lock) _timers.Add(handle);

        return handle;
    }

    private void Forget(Handle handle)
    {
        lock (_lock) _timers.Remove(handle);
    }

    private sealed class Handle(SleepyTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => owner.Forget(this);

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
