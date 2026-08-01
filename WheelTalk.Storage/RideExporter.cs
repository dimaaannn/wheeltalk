using Microsoft.Data.Sqlite;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Logging;
using WheelTalk.Core.Playback;

namespace WheelTalk.Storage;

/// <summary>
/// Reads a ride back out as the WheelLog CSV it used to be written as. The format is not decided
/// here and must not be: <see cref="RideLog"/> owns it, character for character, and this class
/// only rebuilds the snapshot each row was written from and hands it over. A row that comes out
/// different from the one the wheel produced is therefore a reading bug, never a format one — and
/// the test that catches it says so.
/// <para>
/// Rows are yielded one at a time on their own connection. An hour of riding is twenty thousand of
/// them, and WAL means a reader does not stand in the way of the ride currently being recorded.
/// </para>
/// </summary>
public sealed class RideExporter(RideDatabase database)
{
    /// <summary>Every ride in the file, newest first.</summary>
    public IReadOnlyList<RideSummary> Rides()
    {
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        // Newest first, and out of the table rather than off the file system. The original walks its
        // log folder for this, which is why its list carries whatever else was ever put there and
        // why the order is up to the file system on older Android.
        command.CommandText =
            """
            SELECT r.id, w.mac, w.protocol, r.started_at, r.ended_at, r.utc_offset_minutes,
                   COALESCE(r.model, ''), COALESCE(r.version, ''),
                   (SELECT COUNT(*) FROM telemetry t WHERE t.ride_id = r.id),
                   r.distance_m, r.duration_s, r.moving_s, r.avg_speed,
                   r.max_speed, r.max_pwm, r.max_power, r.max_current, r.consumption_wh
              FROM ride r JOIN wheel w ON w.id = r.wheel_id
             ORDER BY r.started_at DESC;
            """;

        var rides = new List<RideSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var offset = TimeSpan.FromMinutes(reader.GetInt32(5));
            rides.Add(new RideSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                Hundredths.ParseStamp(reader.GetString(3)).ToOffset(offset),
                reader.IsDBNull(4) ? null : Hundredths.ParseStamp(reader.GetString(4)).ToOffset(offset),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetInt32(8),
                ReadTotals(reader)));
        }

        return rides;
    }

    /// <summary>
    /// The totals as they were stored, or nothing at all. <c>duration_s</c> is the one that decides:
    /// it is empty exactly while a ride has not been closed and totalled, and reading half a set of
    /// figures as zeroes would put a ride of no distance and no speed on the screen.
    /// </summary>
    private static RideTotals? ReadTotals(SqliteDataReader reader)
    {
        const int Duration = 10;
        if (reader.IsDBNull(Duration)) return null;

        return new RideTotals(
            reader.GetInt64(9),
            TimeSpan.FromSeconds(reader.GetInt64(Duration)),
            TimeSpan.FromSeconds(reader.GetInt64(11)),
            reader.GetInt64(12) / 100.0,
            reader.GetInt64(13) / 100.0,
            reader.GetInt64(14) / 100.0,
            reader.GetInt64(15) / 100.0,
            reader.GetInt64(16) / 100.0,
            reader.GetInt64(17) / 100.0);
    }

    /// <summary>
    /// The ride as CSV lines, header first, no terminators — whoever owns the file decides those,
    /// and WheelLog's are CRLF.
    /// </summary>
    public IEnumerable<string> Export(long rideId)
    {
        yield return RideLog.Header;

        foreach (var (at, snapshot) in ReadTelemetry(rideId))
        {
            yield return RideLog.FormatLine(at, snapshot);
        }
    }

    /// <summary>
    /// The ride as the player wants it: every sample stamped by how far into the ride it happened.
    /// Playback needs a list rather than a stream — seeking is a search over it, and a ride worth
    /// watching is a few tens of thousands of rows, which is nothing to hold.
    /// </summary>
    public IReadOnlyList<RideSample> Samples(long rideId)
    {
        var samples = new List<RideSample>();
        DateTimeOffset? start = null;

        foreach (var (at, snapshot) in ReadTelemetry(rideId))
        {
            // Time is counted from the first row rather than from the ride's own started_at: a ride
            // that began before the wheel said anything would otherwise open on a stretch where
            // there is nothing to show, and the scrubber would start with dead space.
            start ??= at;
            samples.Add(new RideSample(at - start.Value, at, snapshot));
        }

        return samples;
    }

    /// <summary>
    /// Rows of one ride, oldest first, as the pair everything above needs: when it happened and
    /// what the wheel said. Both readers want the same columns and the same NULL handling, and two
    /// copies of that would drift the moment a column is added.
    /// </summary>
    private IEnumerable<(DateTimeOffset At, TelemetrySnapshot Snapshot)> ReadTelemetry(long rideId)
    {
        using var connection = database.Connect();

        var offset = TimeSpan.FromMinutes(RideOffsetMinutes(connection, rideId));

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT at, speed, voltage, phase_current, current, power, pwm, battery_level,
                   distance, totaldistance, system_temp, temp2, tilt, alert
              FROM telemetry WHERE ride_id = $ride ORDER BY rowid;
            """;
        command.Parameters.AddWithValue("$ride", rideId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Local time, as the original writes it: the ride is read back in the zone it happened
            // in, which is the one thing a UTC stamp cannot tell you on its own.
            var at = Hundredths.ParseStamp(reader.GetString(0)).ToOffset(offset);

            // A NULL here is "the protocol never said", and the live snapshot had a zero in that
            // field — so a zero is what the export prints, exactly as it printed then. The column
            // is nullable for the sake of graphs, not for the sake of the file.
            var snapshot = new TelemetrySnapshot
            {
                SpeedRaw = reader.GetInt32(1),
                VoltageRaw = reader.GetInt32(2),
                PhaseCurrentRaw = reader.GetInt32(3),
                CurrentRaw = reader.GetInt32(4),
                PowerRaw = reader.GetInt32(5),
                Pwm = reader.GetInt64(6) / 100.0,
                Battery = reader.GetInt32(7),
                WheelDistance = reader.GetInt64(8),
                TotalDistance = reader.GetInt64(9),
                TemperatureRaw = reader.GetInt32(10),
                Temperature2Raw = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                Angle = reader.IsDBNull(12) ? 0.0 : reader.GetInt64(12) / 100.0,
                Alert = reader.IsDBNull(13) ? "" : reader.GetString(13),
            };

            yield return (at, snapshot);
        }
    }

    private static int RideOffsetMinutes(SqliteConnection connection, long rideId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT utc_offset_minutes FROM ride WHERE id = $ride;";
        command.Parameters.AddWithValue("$ride", rideId);
        object? offset = command.ExecuteScalar()
            ?? throw new ArgumentException($"There is no ride {rideId} in {connection.DataSource}.", nameof(rideId));
        return Convert.ToInt32(offset, System.Globalization.CultureInfo.InvariantCulture);
    }
}
