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
    public static RideDatabase Open(
        string path, TimeProvider timeProvider, ILogger<RideDatabase> logger, StorageOptions? options = null)
    {
        options ??= new StorageOptions();
        try
        {
            return OpenCore(path, timeProvider, options, logger);
        }
        catch (SqliteException ex)
        {
            // Not a database, or a corrupt one. Move it aside under a name that says when, so it
            // can still be looked at, and start over.
            string moved = $"{path}.broken-{timeProvider.GetUtcNow():yyyyMMdd_HHmmss}";
            MoveAside(path, moved, logger, ex);
            return OpenCore(path, timeProvider, options, logger);
        }
    }

    /// <summary>
    /// Закрывает все соединения с базами, открытые в этом процессе. Нужно ровно одному случаю —
    /// файл базы собираются удалить.
    /// <para>
    /// <b>Dispose соединения его не закрывает:</b> Microsoft.Data.Sqlite держит пул и возвращает
    /// соединение туда. Файл, удалённый при живом соединении, исчезает из каталога, но пул
    /// продолжает писать в тот же самый безымянный inode — записи уходят в никуда, и молча. Найдено
    /// прогоном стенда 04.08.2026: набивка «заново» удаляла файл, отчитывалась об успехе и не
    /// оставляла после себя ничего.
    /// </para>
    /// </summary>
    public static void CloseAllConnections() => SqliteConnection.ClearAllPools();

    /// <summary>A fresh connection to the same file. Callers own it and dispose it.</summary>
    public SqliteConnection Connect()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        Execute(connection, "PRAGMA foreign_keys = ON;");
        return connection;
    }

    private static RideDatabase OpenCore(
        string path, TimeProvider timeProvider, StorageOptions options, ILogger<RideDatabase> logger)
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

        // ПОРЯДОК ЗДЕСЬ НЕСУЩИЙ, А НЕ КОСМЕТИЧЕСКИЙ (план 23 §5.4). Поездка закрывается последним
        // кадром телеметрии, итоги считаются по кадрам — а чистка кадры удаляет. Опередит она эти
        // два шага, и закрывать поездку станет нечем, а считать итоги — не из чего.
        //
        // Держится это на том, что телеметрию удаляет приложение при запуске, а не время само по
        // себе: не запускали двое суток — не было и чистки, кадры целы все. Конструкция сломается
        // в тот день, когда чистку вынесут в фоновую задачу по расписанию. Чистка не выполняется
        // нигде, кроме как здесь, после закрытия поездок.
        long now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        database.CloseAbandonedRides(connection, now - (long)options.AbandonedRideGap.TotalMilliseconds);

        // After closing them, not before: a ride still open has no end to measure to. Rides from
        // before the totals existed are caught by the same pass.
        int filled = RideTotalsWriter.Backfill(connection);
        if (filled > 0) LogTotalsFilled(logger, filled);

        database.PurgeOldTelemetry(connection, now, options.TelemetryRetention);

        return database;
    }

    /// <summary>
    /// A ride with no <c>ended_at</c> is one the app never got to finish — the phone died, or the
    /// system killed it. The rows are all there; only the closing stamp is missing, and the last
    /// row is exactly where the ride stopped. Rides that never got a row are closed where they
    /// started, so nothing is left open to be confused with the ride about to begin.
    /// <para>
    /// Закрывается не всякая открытая, а только та, с последнего кадра которой прошло больше
    /// <see cref="StorageOptions.AbandonedRideGap"/> (решение владельца 03.08.2026, план 23 §5.4).
    /// Столько молчания означает «прошлая сессия уже точно не та». Меньше — поездка остаётся
    /// открытой и продолжается той же: убило посреди покатушки, перезапустил через пять минут,
    /// кадры лягут в неё же (<c>RideStore.AdoptOpenRide</c>).
    /// </para>
    /// <para>
    /// Запуск — не единственный момент, когда правило применяется: то же самое делает приход кадра
    /// после разрыва (<c>RideStore</c>). Само правило одно на оба входа —
    /// <see cref="RideClosing.CloseAbandoned"/>.
    /// </para>
    /// </summary>
    private void CloseAbandonedRides(SqliteConnection connection, long staleBefore)
    {
        int closed = RideClosing.CloseAbandoned(connection, tx: null, staleBefore).Count;
        if (closed > 0) LogAbandonedClosed(_logger, closed);
    }

    /// <summary>
    /// Сносит поток старше срока хранения — всё, без исключений для размеченного поездками:
    /// очистки раздельны и друг о друге не знают (план 23 §5.1 п. 5). Итоги покатушки к этому
    /// моменту уже посчитаны и лежат при ней, и после чистки от неё остаются именно они.
    /// <para>
    /// Медленные таблицы уходят вместе с телеметрией: они тот же поток, только реже. Два разных
    /// срока в одном хранилище — это два ответа на вопрос «что у меня есть за прошлую неделю».
    /// </para>
    /// </summary>
    private void PurgeOldTelemetry(SqliteConnection connection, long now, TimeSpan retention)
    {
        if (retention <= TimeSpan.Zero) return;

        long cutoff = now - (long)retention.TotalMilliseconds;
        int removed = 0;
        foreach (string table in (string[])["telemetry", "wheel_state", "pack_state"])
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {table} WHERE at < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", cutoff);
            removed += command.ExecuteNonQuery();
        }

        if (removed > 0) LogTelemetryPurged(_logger, removed, retention);
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

    [LoggerMessage(EventId = 1606, EventName = "Db.TelemetryPurged", Level = LogLevel.Information,
        Message = "Db.TelemetryPurged {Count} rows older than {Retention}")]
    private static partial void LogTelemetryPurged(ILogger logger, int count, TimeSpan retention);
}
