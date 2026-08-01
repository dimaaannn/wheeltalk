using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace WheelTalk.Storage;

/// <summary>
/// The ride database file: opens it, brings the schema up to date, and hands out connections to
/// whoever needs one. Everything that can go wrong with the file is decided here, once, rather
/// than by each caller.
/// <para>
/// The rule behind those decisions: recorded rides are the only data this project holds nowhere
/// else. A file we cannot read is moved aside, never deleted; a file from a newer build is left
/// alone and not written to.
/// </para>
/// </summary>
public sealed partial class RideDatabase
{
    private readonly ILogger<RideDatabase> _logger;

    private RideDatabase(string path, bool writable, ILogger<RideDatabase> logger)
    {
        Path = path;
        IsWritable = writable;
        _logger = logger;
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public string Path { get; }

    internal string ConnectionString { get; }

    /// <summary>
    /// False when the file was written by a newer build than this one. Recording refuses rather
    /// than guessing what columns it does not know about are for.
    /// </summary>
    public bool IsWritable { get; }

    /// <summary>
    /// Opens (or creates) the database and migrates it. Never throws for a file problem: a broken
    /// or too-new file leaves a working, if limited, object and a loud line in the log — losing the
    /// ride currently being recorded because the previous one left a bad file would be the worse
    /// failure of the two.
    /// </summary>
    public static RideDatabase Open(string path, TimeProvider timeProvider, ILogger<RideDatabase> logger)
    {
        try
        {
            return OpenCore(path, logger);
        }
        catch (SqliteException ex)
        {
            // Not a database, or a corrupt one. Move it aside under a name that says when, so it
            // can still be looked at, and start over.
            string moved = $"{path}.broken-{timeProvider.GetUtcNow():yyyyMMdd_HHmmss}";
            MoveAside(path, moved, logger, ex);
            return OpenCore(path, logger);
        }
    }

    /// <summary>A fresh connection to the same file. Callers own it and dispose it.</summary>
    public SqliteConnection Connect()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        Execute(connection, "PRAGMA foreign_keys = ON;");
        return connection;
    }

    private static RideDatabase OpenCore(string path, ILogger<RideDatabase> logger)
    {
        string? folder = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        // WAL keeps a reader (the exporter) out of the writer's way. NORMAL gives up "committed
        // survives a power cut" for "committed survives a crash", which is the trade the original
        // makes too — a phone losing power mid-ride costs a second of telemetry, not the ride.
        Execute(connection, "PRAGMA journal_mode = WAL;");
        Execute(connection, "PRAGMA synchronous = NORMAL;");
        Execute(connection, "PRAGMA foreign_keys = ON;");

        int version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
        if (version > Schema.Version)
        {
            LogTooNew(logger, path, version, Schema.Version);
            return new RideDatabase(path, writable: false, logger);
        }

        for (int next = version; next < Schema.Version; next++)
        {
            using var tx = connection.BeginTransaction();
            Execute(connection, Schema.Migrations[next], tx);
            // Interpolated because PRAGMA does not take parameters; the value is a loop counter.
            Execute(connection, $"PRAGMA user_version = {next + 1};", tx);
            tx.Commit();
            LogMigrated(logger, next, next + 1);
        }

        var database = new RideDatabase(path, writable: true, logger);
        database.CloseAbandonedRides(connection);

        // After closing them, not before: a ride still open has no end to measure to. Rides from
        // before the totals existed are caught by the same pass.
        int filled = RideTotalsWriter.Backfill(connection);
        if (filled > 0) LogTotalsFilled(logger, filled);

        return database;
    }

    /// <summary>
    /// A ride with no <c>ended_at</c> is one the app never got to finish — the phone died, or the
    /// system killed it. The rows are all there; only the closing stamp is missing, and the last
    /// row is exactly where the ride stopped. Rides that never got a row are closed where they
    /// started, so nothing is left open to be confused with the ride about to begin.
    /// </summary>
    private void CloseAbandonedRides(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE ride
               SET ended_at = COALESCE(
                       (SELECT MAX(at) FROM telemetry WHERE telemetry.ride_id = ride.id),
                       started_at)
             WHERE ended_at IS NULL;
            """;
        int closed = command.ExecuteNonQuery();
        if (closed > 0) LogAbandonedClosed(_logger, closed);
    }

    private static void MoveAside(string path, string moved, ILogger logger, Exception cause)
    {
        try
        {
            if (File.Exists(path)) File.Move(path, moved);
            // WAL and shared-memory files belong to the database they were written for; leaving
            // them next to a fresh file is how a good database gets a bad journal applied to it.
            foreach (string suffix in (string[])["-wal", "-shm"])
            {
                if (File.Exists(path + suffix)) File.Move(path + suffix, moved + suffix);
            }

            LogMovedAside(logger, cause, path, moved);
        }
        catch (IOException ex)
        {
            LogCouldNotMoveAside(logger, ex, path);
            throw;
        }
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? tx = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = tx;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    [LoggerMessage(EventId = 1600, EventName = "Db.Migrated", Level = LogLevel.Information,
        Message = "Db.Migrated {From} -> {To}")]
    private static partial void LogMigrated(ILogger logger, int from, int to);

    [LoggerMessage(EventId = 1601, EventName = "Db.TooNew", Level = LogLevel.Error,
        Message = "Db.TooNew {Path} is schema {Found}, this build knows {Known} — recording disabled")]
    private static partial void LogTooNew(ILogger logger, string path, int found, int known);

    [LoggerMessage(EventId = 1602, EventName = "Db.MovedAside", Level = LogLevel.Error,
        Message = "Db.MovedAside {Path} could not be opened and was renamed to {Moved}")]
    private static partial void LogMovedAside(ILogger logger, Exception ex, string path, string moved);

    [LoggerMessage(EventId = 1603, EventName = "Db.CouldNotMoveAside", Level = LogLevel.Critical,
        Message = "Db.CouldNotMoveAside {Path} is unreadable and cannot be renamed either")]
    private static partial void LogCouldNotMoveAside(ILogger logger, Exception ex, string path);

    [LoggerMessage(EventId = 1604, EventName = "Db.AbandonedRidesClosed", Level = LogLevel.Warning,
        Message = "Db.AbandonedRidesClosed {Count}")]
    private static partial void LogAbandonedClosed(ILogger logger, int count);

    [LoggerMessage(EventId = 1605, EventName = "Db.TotalsFilled", Level = LogLevel.Information,
        Message = "Db.TotalsFilled {Count} rides")]
    private static partial void LogTotalsFilled(ILogger logger, int count);
}
