using WheelTalk.Core.Contracts;
using WheelTalk.Core.Ports;

namespace WheelTalk.Tests.TestSupport;

/// <summary>Discards published events — decoder tests assert on TelemetrySnapshot, not IEventSink.</summary>
public sealed class NullEventSink : IEventSink
{
    public void Publish(WheelEvent e)
    {
    }
}
