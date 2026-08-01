using Microsoft.Extensions.Time.Testing;
using WheelTalk.Core.Ports;

namespace WheelTalk.Tests.Ports;

/// <summary>
/// Pins down the delivery guarantees roadmap "Пункт 9" asks for: one command in flight, a busy
/// refusal retried rather than dropped, order preserved, and a confirmation that never arrives
/// eventually freeing the queue instead of stalling it forever. The fake here plays the part of
/// <c>AndroidBleClient</c>'s <c>gatt.WriteCharacteristic</c> + <c>OnCharacteristicWrite</c> pair —
/// <c>beginWrite</c> is the immediate return value, <see cref="SequentialWriteQueue.Complete"/> is
/// the callback.
/// </summary>
public class SequentialWriteQueueTests
{
    private static readonly TimeSpan BusyRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan BusyDeadline = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_command_is_not_reported_delivered_until_the_platform_confirms_it()
    {
        var time = new FakeTimeProvider();
        var attempts = new List<byte[]>();
        var queue = new SequentialWriteQueue(payload => { attempts.Add(payload); return true; },
            time, BusyRetryDelay, BusyDeadline, ConfirmationTimeout);

        var delivery = queue.Enqueue([0x62]); // "b"
        await WaitUntil(() => attempts.Count == 1);

        // The write left the radio, but nobody has said it arrived — Cmd.Sent must not fire yet.
        Assert.False(delivery.IsCompleted);

        queue.Complete(success: true);
        await delivery;

        Assert.Single(attempts);
    }

    [Fact]
    public async Task A_busy_refusal_is_retried_instead_of_losing_the_command()
    {
        var time = new FakeTimeProvider();
        int attemptCount = 0;
        var queue = new SequentialWriteQueue(_ =>
        {
            attemptCount++;
            return attemptCount > 2; // "busy" twice — the wheel was mid-notification — then accepted
        }, time, BusyRetryDelay, BusyDeadline, ConfirmationTimeout);

        var delivery = queue.Enqueue([0x62]);

        await WaitUntil(() => attemptCount == 1);
        Assert.False(delivery.IsCompleted);

        time.Advance(BusyRetryDelay);
        await WaitUntil(() => attemptCount == 2);
        Assert.False(delivery.IsCompleted);

        time.Advance(BusyRetryDelay);
        await WaitUntil(() => attemptCount == 3);

        queue.Complete(success: true);
        await delivery;

        Assert.Equal(3, attemptCount);
    }

    /// <summary>
    /// The sequel to <see cref="A_confirmation_that_never_arrives_times_out_and_frees_the_queue_for_the_next_command"/>:
    /// our timeout releases *our* slot, but the platform's own busy flag is cleared only by the
    /// callback we just gave up on. When there is none, every later write is refused, and retrying
    /// without an end would leave the command pending forever — a button that is pressed and never
    /// answers, with the whole queue stopped behind it.
    /// </summary>
    [Fact]
    public async Task A_busy_that_never_clears_fails_the_command_instead_of_retrying_in_silence()
    {
        var time = new FakeTimeProvider();
        var started = new List<byte>();
        var queue = new SequentialWriteQueue(payload =>
        {
            started.Add(payload[0]);
            return payload[0] != 1; // the stack stays busy for the first command, and only for it
        }, time, BusyRetryDelay, BusyDeadline, ConfirmationTimeout);

        var refused = queue.Enqueue([1]);
        var next = queue.Enqueue([2]);

        await WaitUntil(() => started.Count == 1);
        Assert.False(refused.IsCompleted);

        time.Advance(BusyDeadline);
        var failure = await Assert.ThrowsAsync<WriteRefusedException>(() => refused);
        Assert.Equal(2, failure.Attempts);

        // Отказ — не затор: следующая команда уходит сразу, а не ждёт своей вечности.
        await WaitUntil(() => started.Contains((byte)2));
        queue.Complete(success: true);
        await next;
    }

    [Fact]
    public async Task Queued_commands_are_delivered_one_at_a_time_in_order()
    {
        var time = new FakeTimeProvider();
        var started = new List<byte>();
        var queue = new SequentialWriteQueue(payload => { started.Add(payload[0]); return true; },
            time, BusyRetryDelay, BusyDeadline, ConfirmationTimeout);

        // Gotway's calibrate is exactly this shape: primary "c" enqueued by the button, delayed
        // "y" enqueued 300ms later by the decoder's own WriteRequested — both land on this queue.
        var first = queue.Enqueue([1]);
        var second = queue.Enqueue([2]);
        var third = queue.Enqueue([3]);

        await WaitUntil(() => started.Count == 1);
        // Only one write may be outstanding — the second and third must still be waiting.
        Assert.Single(started);
        Assert.False(second.IsCompleted);
        Assert.False(third.IsCompleted);

        queue.Complete(success: true);
        await WaitUntil(() => started.Count == 2);
        await first;

        queue.Complete(success: true);
        await WaitUntil(() => started.Count == 3);
        await second;

        queue.Complete(success: true);
        await third;

        Assert.Equal(new byte[] { 1, 2, 3 }, started);
    }

    [Fact]
    public async Task A_confirmation_that_never_arrives_times_out_and_frees_the_queue_for_the_next_command()
    {
        var time = new FakeTimeProvider();
        var started = new List<byte>();
        var queue = new SequentialWriteQueue(payload => { started.Add(payload[0]); return true; },
            time, BusyRetryDelay, BusyDeadline, ConfirmationTimeout);

        var stuck = queue.Enqueue([1]); // link drops right after this is accepted — no callback ever comes
        var next = queue.Enqueue([2]);

        await WaitUntil(() => started.Count == 1);
        time.Advance(ConfirmationTimeout);

        await Assert.ThrowsAsync<TimeoutException>(() => stuck);

        await WaitUntil(() => started.Count == 2);
        queue.Complete(success: true);
        await next;
    }

    [Fact]
    public async Task An_immediate_permanent_failure_faults_that_command_without_retrying_forever()
    {
        var time = new FakeTimeProvider();
        var queue = new SequentialWriteQueue(
            _ => throw new InvalidOperationException("not connected"),
            time, BusyRetryDelay, BusyDeadline, ConfirmationTimeout);

        var delivery = queue.Enqueue([1]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => delivery);
    }

    [Fact]
    public async Task A_stray_callback_with_nothing_in_flight_is_ignored()
    {
        var time = new FakeTimeProvider();
        int attempts = 0;
        var queue = new SequentialWriteQueue(_ => { attempts++; return true; },
            time, BusyRetryDelay, BusyDeadline, ConfirmationTimeout);

        // Nothing queued yet — this must not throw.
        queue.Complete(success: true);

        // Wait for the write to actually start before confirming: a Complete that lands before
        // the pump has anything in flight is itself "stray" and ignored — in production the
        // callback can only follow a real WriteCharacteristic call, so the race exists only here.
        var delivery = queue.Enqueue([1]);
        await WaitUntil(() => attempts == 1);
        queue.Complete(success: true);
        await delivery;
    }

    private static async Task WaitUntil(Func<bool> condition, int maxAttempts = 200)
    {
        for (int i = 0; i < maxAttempts && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "condition was not met in time");
    }
}
