using WheelTalk.Core.Contracts;

namespace WheelTalk.Core.Services;

/// <summary>
/// The wheel was asked for something with nothing to send it over: <see cref="WheelSession"/> has
/// no service, meaning either nothing is connected or the session is still chasing the wheel after
/// a drop.
/// <para>
/// Its own type rather than a bare <see cref="InvalidOperationException"/> so that "there was
/// nobody to send this to" stays distinguishable from "it was sent and the write failed". Only the
/// second one is a defect worth reporting; the first is the ordinary state of a wheel that is
/// switched off, and it should read that way in the log.
/// </para>
/// </summary>
public sealed class WheelNotConnectedException(WheelCommand command)
    : InvalidOperationException($"No link to the wheel — {command} was not sent")
{
    public WheelCommand Command { get; } = command;
}
