namespace WheelTalk.Storage;

/// <summary>
/// A ride as the outside world needs to see it: enough to name a file and put a line on a list,
/// without reading twenty thousand rows to find out.
/// </summary>
/// <param name="StartedAt">In the zone it was ridden in — that is what the export prints and what a list should show.</param>
/// <param name="EndedAt">Null only for the ride being written right now.</param>
/// <param name="Totals">
/// Null while the ride is still being recorded — there is nothing to total until it ends. A finished
/// ride always has them: they are written when it closes and filled in at the next open for the ones
/// that predate the columns (<see cref="RideTotalsWriter.Backfill"/>).
/// </param>
public sealed record RideSummary(
    long Id,
    string Mac,
    string Protocol,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string Model,
    string Version,
    int Rows,
    RideTotals? Totals = null)
{
    public TimeSpan? Duration => EndedAt - StartedAt;

    /// <summary>The ride the recorder is writing into right now — the one row a list must not offer to delete.</summary>
    public bool IsOpen => EndedAt is null;

    /// <summary>Model when the wheel got as far as saying one, and the address when it did not.</summary>
    public string Name => Model.Length > 0 ? Model : Mac;
}
