namespace WheelTalk.Core.Contracts;

/// <summary>
/// Event contract published through <c>IEventSink</c> — stands in for Android's
/// <c>sendBroadcast(ACTION_*)</c> intents.
/// </summary>
public abstract record WheelEvent
{
    public sealed record WheelDataAvailable(TelemetrySnapshot Snapshot) : WheelEvent;
    public sealed record WheelTypeChanged(WheelType WheelType) : WheelEvent;
    public sealed record WheelModelChanged(string Model) : WheelEvent;
    public sealed record CrcCheckFailed : WheelEvent;
    public sealed record Connected(string Mac) : WheelEvent;
    public sealed record Disconnected : WheelEvent;
}
