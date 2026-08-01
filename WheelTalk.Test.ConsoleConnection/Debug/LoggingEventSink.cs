using Microsoft.Extensions.Logging;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Ports;

namespace WheelTalk.Debug;

/// <summary>Minimal IEventSink for the console test port — logs every published event.</summary>
public sealed class LoggingEventSink : IEventSink
{
    private readonly ILogger<LoggingEventSink> _logger;

    public LoggingEventSink(ILogger<LoggingEventSink> logger)
    {
        _logger = logger;
    }

    public void Publish(WheelEvent e)
    {
        switch (e)
        {
            case WheelEvent.WheelDataAvailable:
                _logger.LogDebug("Event: WheelDataAvailable");
                break;
            case WheelEvent.CrcCheckFailed:
                _logger.LogWarning("Event: Frame.CrcFail");
                break;
            default:
                _logger.LogInformation("Event: {Event}", e);
                break;
        }
    }
}
