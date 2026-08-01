namespace WheelTalk.Core.Services;

/// <summary>Where a <see cref="WheelSession"/> stands with its wheel.</summary>
public enum ConnectionState
{
    /// <summary>No wheel wanted — either nothing was ever asked for, or the rider disconnected.</summary>
    Disconnected,

    /// <summary>First attempt at the wheel the rider asked for.</summary>
    Connecting,

    /// <summary>Frames can flow.</summary>
    Connected,

    /// <summary>The link was lost and is being chased. Readings on screen are the last known ones.</summary>
    Reconnecting,
}
