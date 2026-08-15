namespace WheelTalk.Core.Ports;

/// <summary>
/// Часы как часы, но <b>таймеров у них нет</b>: <see cref="CreateTimer"/> отдаёт пустышку, которая
/// не сработает никогда. Время идёт как у настоящих — всё остальное делегируется.
/// <para>
/// Зачем. Порт протокола, заводящий свой опрос в конструкторе, правке не подлежит, а расписание у
/// него забирает надстройка (план 36 Л3, <c>InMotionDecoderV2_1</c>). Шов для этого уже есть —
/// <see cref="TimeProvider"/>, который надстройка передаёт порту, — и подмена его на эти часы глушит
/// опрос порта насухо, не меняя в порте ни знака. Это не абстракция, а заглушка на существующей
/// точке расширения: ровно то, чем <c>FakeTimeProvider</c> служит в тестах.
/// </para>
/// <para>
/// Пустышка честно молчит и на <see cref="ITimer.Change"/>: сменить срок у таймера, которого нет,
/// значит ничего не сменить, и «истина» здесь означала бы «поставлено», обманывая вызывающего.
/// </para>
/// </summary>
public sealed class TimerlessTimeProvider(TimeProvider inner) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override long GetTimestamp() => inner.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        new SilentTimer();

    private sealed class SilentTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
