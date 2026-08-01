namespace WheelTalk.Storage;

/// <summary>How <see cref="RideStore"/> spends time and disk.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// How long rows are allowed to pile up before a commit. This is the window that is lost if the
    /// app dies — a second and a half of telemetry, seven or eight rows — bought against a WAL
    /// commit and three index updates five times a second. Zero commits as fast as rows arrive,
    /// which is what tests want and a phone does not.
    /// </summary>
    public TimeSpan CommitInterval { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// How often the slow tables get a row regardless of whether anything changed. They also get
    /// one the moment something does change: a minute is fine for watching a pack warm up, and
    /// useless for catching the instant a charger was plugged in.
    /// </summary>
    public TimeSpan StateInterval { get; set; } = TimeSpan.FromMinutes(1);
}
