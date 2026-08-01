using System.Threading.Channels;

namespace WheelTalk.Core.Ports;

/// <summary>
/// Delivery queue for a transport that allows only one write in flight and confirms completion
/// asynchronously through a callback — Android's <c>BluetoothGatt</c> is exactly this shape:
/// <c>WriteCharacteristic</c> returns immediately (accepted, or "busy — some other GATT operation
/// is still running"), and the real outcome arrives afterwards through <c>OnCharacteristicWrite</c>.
/// Lives here rather than in <c>WheelTalk.Droid</c> because none of this needs a platform API to
/// describe, and it is exactly the part roadmap "Пункт 9" identifies as missing: before it,
/// <c>gatt.WriteCharacteristic(...)</c>'s return value was discarded, so a command the stack
/// refused (busy — the wheel notifies twenty times a second) vanished silently while the log still
/// said it went out.
/// <para>
/// One command is in flight at a time; the next is not attempted until <see cref="Complete"/>
/// reports the previous one's outcome. A write the platform refuses immediately is retried after a
/// short pause rather than dropped, and stays at the head of the queue — order among everything
/// already queued is preserved. This also makes Gotway's two-step commands (calibrate "c", then
/// "y" 300 ms later) safe to route through the same queue as user-initiated commands: both are
/// just payloads handed to <see cref="Enqueue"/>, delivered one at a time, in the order they
/// arrived.
/// </para>
/// <para>
/// Retrying has an end (<see cref="WriteRefusedException"/>). "Busy" is usually a state that clears
/// within a frame or two, but it is not guaranteed to: the platform clears its flag only when the
/// callback of the operation holding it arrives, and that callback can be the very one
/// <paramref name="confirmationTimeout"/> already gave up on. Then every later write is refused
/// forever, and without a deadline the queue would sit in that loop silently — the command's task
/// neither completing nor faulting, and everything behind it waiting with it.
/// </para>
/// </summary>
public sealed class SequentialWriteQueue
{
    private readonly Func<byte[], bool> _beginWrite;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _busyRetryDelay;
    private readonly TimeSpan _busyDeadline;
    private readonly TimeSpan _confirmationTimeout;
    private readonly Channel<PendingWrite> _pending =
        Channel.CreateUnbounded<PendingWrite>(new UnboundedChannelOptions { SingleReader = true });

    private TaskCompletionSource? _inFlight;

    /// <param name="beginWrite">
    /// Attempts the raw platform write for one payload. Returns <c>true</c> if the platform
    /// accepted it and will report the outcome later through <see cref="Complete"/>; <c>false</c>
    /// if it refused immediately ("busy" — the request never left), which is retried. Throwing
    /// fails that command outright instead of retrying — use this for failures that will not
    /// clear on their own (not connected), or a permanent failure would retry forever.
    /// </param>
    /// <param name="timeProvider">Drives every delay below — a fake in tests, real elsewhere.</param>
    /// <param name="busyRetryDelay">Pause before re-attempting a write the platform refused.</param>
    /// <param name="busyDeadline">
    /// How long one payload may go on being refused before the queue stops asking and fails it with
    /// <see cref="WriteRefusedException"/>. Bounds the case where the platform's "busy" never
    /// clears at all — see the class remarks.
    /// </param>
    /// <param name="confirmationTimeout">
    /// How long to wait for <see cref="Complete"/> after an accepted write before giving up on it.
    /// Without this, a write accepted right as the link drops — after which nothing ever calls
    /// <see cref="Complete"/> again — would stall every command queued behind it forever.
    /// </param>
    public SequentialWriteQueue(Func<byte[], bool> beginWrite, TimeProvider timeProvider,
        TimeSpan busyRetryDelay, TimeSpan busyDeadline, TimeSpan confirmationTimeout)
    {
        _beginWrite = beginWrite;
        _timeProvider = timeProvider;
        _busyRetryDelay = busyRetryDelay;
        _busyDeadline = busyDeadline;
        _confirmationTimeout = confirmationTimeout;
        _ = PumpAsync();
    }

    /// <summary>
    /// Queues a command and returns a task that completes once it is confirmed delivered, or
    /// faults if it never was (immediate permanent failure, or confirmation timeout). Ordering is
    /// FIFO among everything queued at the time of the call.
    /// </summary>
    public Task Enqueue(byte[] payload)
    {
        var pending = new PendingWrite(payload);
        _pending.Writer.TryWrite(pending);
        return pending.Completion.Task;
    }

    /// <summary>
    /// The platform learned the outcome of the write it previously accepted — advances the queue
    /// to the next command. A call with nothing in flight (a stray or late callback, e.g. after a
    /// confirmation timeout already gave up on it) is ignored.
    /// </summary>
    public void Complete(bool success, Exception? failure = null)
    {
        var tcs = Interlocked.Exchange(ref _inFlight, null);
        if (tcs is null) return;

        if (success)
        {
            tcs.TrySetResult();
        }
        else
        {
            tcs.TrySetException(failure ?? new InvalidOperationException("GATT write failed"));
        }
    }

    private async Task PumpAsync()
    {
        await foreach (var pending in _pending.Reader.ReadAllAsync())
        {
            await DeliverAsync(pending);
        }
    }

    private async Task DeliverAsync(PendingWrite pending)
    {
        // Both counted from the first refusal of *this* payload, not from the queue's lifetime: a
        // command that waited its turn behind a slow one has not itself been refused yet.
        long? refusedSince = null;
        int refusals = 0;

        while (true)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight = tcs;

            bool started;
            try
            {
                started = _beginWrite(pending.Payload);
            }
            catch (Exception ex)
            {
                _inFlight = null;
                pending.Completion.TrySetException(ex);
                return;
            }

            if (!started)
            {
                _inFlight = null;
                refusals++;
                refusedSince ??= _timeProvider.GetTimestamp();

                var refusedFor = _timeProvider.GetElapsedTime(refusedSince.Value);
                if (refusedFor >= _busyDeadline)
                {
                    pending.Completion.TrySetException(new WriteRefusedException(refusedFor, refusals));
                    return;
                }

                await Task.Delay(_busyRetryDelay, _timeProvider);
                continue;
            }

            try
            {
                await tcs.Task.WaitAsync(_confirmationTimeout, _timeProvider);
                pending.Completion.TrySetResult();
            }
            catch (Exception ex)
            {
                // A timed-out wait leaves _inFlight pointing at a TCS nobody will ever complete —
                // clear it (only if it is still this one) so a stray late callback lands on nothing
                // rather than resolving a command that has already moved on.
                Interlocked.CompareExchange(ref _inFlight, null, tcs);
                pending.Completion.TrySetException(ex);
            }
            return;
        }
    }

    private sealed class PendingWrite(byte[] payload)
    {
        public byte[] Payload { get; } = payload;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
