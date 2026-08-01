using Microsoft.Data.Sqlite;

namespace WheelTalk.Storage;

/// <summary>
/// What a ride adds up to. Worked out once, when the ride ends, and kept on the ride — see
/// <see cref="Schema"/> v3 for why it is stored rather than recomputed, and plan 8 §3.1 for where
/// each of these figures comes from.
/// </summary>
/// <param name="Duration">Wall clock from the first row to the last, stops included.</param>
/// <param name="Moving">Only the time above <see cref="RidingSpeedKmh"/>. What "average speed" is over.</param>
public sealed record RideTotals(
    long DistanceMetres,
    TimeSpan Duration,
    TimeSpan Moving,
    double AverageSpeedKmh,
    double MaxSpeedKmh,
    double MaxPwm,
    double MaxPowerW,
    double MaxCurrentA,
    double ConsumptionWh)
{
    public static readonly RideTotals Empty =
        new(0, TimeSpan.Zero, TimeSpan.Zero, 0, 0, 0, 0, 0, 0);

    public double DistanceKm => DistanceMetres / 1000.0;

    /// <summary>Null rather than zero when there is no distance to divide by: nothing per no kilometres.</summary>
    public double? ConsumptionWhPerKm =>
        DistanceMetres > 0 ? ConsumptionWh * 1000.0 / DistanceMetres : null;

    /// <summary>
    /// Below this a wheel is standing under someone, not moving them — the original's
    /// <c>RIDING_SPEED</c>, 2 km/h. Without a threshold "average speed" turns into a function of how
    /// many traffic lights there were.
    /// </summary>
    public const double RidingSpeedKmh = 2.0;

    /// <summary>
    /// Longer than this between two rows and the wheel was not being ridden — it was disconnected,
    /// or the phone was asleep, or it was a night on the charger. The original excludes such gaps
    /// too (<c>timeToExclude</c>); without that a ride charged overnight reports its energy spread
    /// over ten hours and its consumption comes out at nothing.
    /// </summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Computing the totals and putting them where the list can read them. One pass over the ride's
/// rows in the order they were written, because two of the figures — time spent moving and energy —
/// are sums over the intervals between rows and cannot be had from aggregates over the rows alone.
/// <para>
/// One rule holds the pass together: <b>a sample holds until the next one arrives.</b> The speed on
/// a row is the speed for the interval that follows it, and so is the power. Any other reading has
/// to say what happened in between, and the wheel did not say.
/// </para>
/// </summary>
internal static class RideTotalsWriter
{
    /// <summary>The last few rows the odometer's end is taken from — the original looks at ten.</summary>
    private const int TailRows = 10;

    public static RideTotals Compute(SqliteConnection connection, SqliteTransaction? tx, long rideId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT at, speed, power, current, pwm, totaldistance
              FROM telemetry WHERE ride_id = $ride ORDER BY rowid;
            """;
        command.Transaction = tx;
        command.Parameters.AddWithValue("$ride", rideId);

        DateTimeOffset first = default, last = default, previousAt = default;
        double previousSpeed = 0, previousPower = 0;
        long firstOdometer = 0;
        double moving = 0, energy = 0;
        double maxSpeed = 0, maxPwm = 0, maxPower = 0, maxCurrent = 0;
        var tail = new long[TailRows];
        int rows = 0;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var at = Hundredths.ParseStamp(reader.GetString(0));
            double speed = reader.GetInt64(1) / 100.0;
            double power = reader.GetInt64(2) / 100.0;
            long odometer = reader.GetInt64(5);

            if (rows == 0) first = at;

            if (rows > 0 && at - previousAt <= RideTotals.MaxGap)
            {
                double seconds = (at - previousAt).TotalSeconds;
                if (Math.Abs(previousSpeed) > RideTotals.RidingSpeedKmh) moving += seconds;
                energy += previousPower * seconds;
            }

            // By absolute value, the same rule the alerts follow: braking hard is a peak too, and a
            // signed maximum would report the regeneration as a quiet moment.
            maxSpeed = Math.Max(maxSpeed, Math.Abs(speed));
            maxPower = Math.Max(maxPower, Math.Abs(power));
            maxCurrent = Math.Max(maxCurrent, Math.Abs(reader.GetInt64(3) / 100.0));
            maxPwm = Math.Max(maxPwm, Math.Abs(reader.GetInt64(4) / 100.0));

            // The odometer is silent for the first frames of some protocols, and a zero there would
            // make the ride as long as the wheel's whole life. The first one that says anything is
            // where this ride started.
            if (firstOdometer == 0 && odometer > 0) firstOdometer = odometer;
            tail[rows % TailRows] = odometer;

            previousAt = last = at;
            previousSpeed = speed;
            previousPower = power;
            rows++;
        }

        if (rows == 0) return RideTotals.Empty;

        // The end of the odometer off the last few rows rather than the very last one: a single
        // garbled frame at the end would otherwise be the whole ride's distance. Also the original's.
        long lastOdometer = tail.Take(Math.Min(rows, TailRows)).Max();
        long distance = firstOdometer > 0 ? Math.Max(0, lastOdometer - firstOdometer) : 0;

        return new RideTotals(
            distance,
            last - first,
            TimeSpan.FromSeconds(moving),
            // Distance over time spent moving — not the mean of the speed readings. That mean is
            // what the original reports, and it answers a different question: how fast the wheel was
            // going in the average packet, which depends on how often packets arrive and how long
            // the stops were. Their own test log reads 29.7 km/h for 19.5 km in 880 minutes.
            moving > 0 ? distance / moving * 3.6 : 0,
            maxSpeed,
            maxPwm,
            maxPower,
            maxCurrent,
            energy / 3600.0);
    }

    public static void Store(SqliteConnection connection, SqliteTransaction? tx, long rideId, RideTotals totals)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE ride SET distance_m = $distance, duration_s = $duration, moving_s = $moving,
                            avg_speed = $avg, max_speed = $maxSpeed, max_pwm = $maxPwm,
                            max_power = $maxPower, max_current = $maxCurrent,
                            consumption_wh = $consumption
             WHERE id = $id;
            """;
        command.Transaction = tx;
        command.Parameters.AddWithValue("$distance", totals.DistanceMetres);
        command.Parameters.AddWithValue("$duration", (long)totals.Duration.TotalSeconds);
        command.Parameters.AddWithValue("$moving", (long)totals.Moving.TotalSeconds);
        command.Parameters.AddWithValue("$avg", Hundredths.Of(totals.AverageSpeedKmh));
        command.Parameters.AddWithValue("$maxSpeed", Hundredths.Of(totals.MaxSpeedKmh));
        command.Parameters.AddWithValue("$maxPwm", Hundredths.Of(totals.MaxPwm));
        command.Parameters.AddWithValue("$maxPower", Hundredths.Of(totals.MaxPowerW));
        command.Parameters.AddWithValue("$maxCurrent", Hundredths.Of(totals.MaxCurrentA));
        command.Parameters.AddWithValue("$consumption", Hundredths.Of(totals.ConsumptionWh));
        command.Parameters.AddWithValue("$id", rideId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Fills in every finished ride that has no totals: the ones recorded before this version
    /// existed, and the ones the crash recovery just closed. An empty <c>duration_s</c> is the only
    /// signal needed — it is also how a changed formula is rolled out, by clearing the column.
    /// <para>
    /// Runs at open, with nobody else on the file. It costs one pass per unfinished ride, once ever;
    /// leaving it to the list screen instead would mean paying it there, on a page the rider opened
    /// to look at something.
    /// </para>
    /// </summary>
    public static int Backfill(SqliteConnection connection)
    {
        var pending = new List<long>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM ride WHERE ended_at IS NOT NULL AND duration_s IS NULL;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) pending.Add(reader.GetInt64(0));
        }

        foreach (long id in pending)
        {
            Store(connection, null, id, Compute(connection, null, id));
        }

        return pending.Count;
    }
}
