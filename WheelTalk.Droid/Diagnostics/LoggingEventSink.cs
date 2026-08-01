using Microsoft.Extensions.Logging;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Ports;

namespace WheelTalk.Droid.Diagnostics;

/// <summary>
/// Wheel events go to the log only. Telemetry reaches the screens through the Rx stream, so the
/// event sink is left as what it is on this platform — a diagnostics trail.
/// </summary>
public sealed class LoggingEventSink(ILogger<LoggingEventSink> logger) : IEventSink
{
    public void Publish(WheelEvent e)
    {
        // WheelDataAvailable fires on every decoded frame; logging it here would duplicate the
        // decoder's own trace line dozens of times a second.
        if (e is WheelEvent.WheelDataAvailable) return;

        logger.LogInformation("Wheel.Event {Event}", e);
    }
}
