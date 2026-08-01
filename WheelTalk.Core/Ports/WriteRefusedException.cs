namespace WheelTalk.Core.Ports;

/// <summary>
/// The platform refused the write outright for as long as <see cref="SequentialWriteQueue"/> was
/// willing to keep asking. Android's GATT client answers "busy" while another operation is in
/// flight, and the flag it answers by is cleared only by that operation's callback — so a callback
/// that never comes (half-open link, wheel switched off before the stack noticed) leaves it busy
/// forever. Retrying therefore has to have an end, and the end has to be a refusal rather than
/// silence: an unbounded retry leaves the command's task pending, the sheet waiting for a fate that
/// never arrives, and every command queued behind it waiting with it.
/// <para>
/// Its own type rather than <see cref="TimeoutException"/> (which the queue already uses for a
/// write the platform *accepted* and never confirmed) because the two failures need different
/// answers: this one means the radio never took the command at all.
/// </para>
/// </summary>
public sealed class WriteRefusedException(TimeSpan after, int attempts)
    : Exception($"The transport refused the write {attempts} times over {after.TotalMilliseconds:F0} ms — the stack stayed busy")
{
    /// <summary>How long the queue kept asking before giving up.</summary>
    public TimeSpan After { get; } = after;

    /// <summary>How many attempts that came to — useful only next to <see cref="After"/>.</summary>
    public int Attempts { get; } = attempts;
}
