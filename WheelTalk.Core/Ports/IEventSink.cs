using WheelTalk.Core.Contracts;

namespace WheelTalk.Core.Ports;

/// <summary>Publishes wheel events — replaces Android's sendBroadcast(ACTION_*).</summary>
public interface IEventSink
{
    void Publish(WheelEvent e);
}
