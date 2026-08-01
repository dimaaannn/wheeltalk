using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Storage;

namespace WheelTalk.Tests.TestSupport;

/// <summary>
/// A ride database in a throwaway folder, plus the small amount of SQL a test needs to look inside
/// it. A real file rather than <c>:memory:</c> on purpose: WAL, the schema version and reopening
/// after a crash are all file behaviour, and an in-memory database would quietly not have it.
/// </summary>
public sealed class TempDatabase : IDisposable
{
    private readonly string _folder;

    public TempDatabase()
    {
        _folder = Path.Combine(Path.GetTempPath(), "wheeltalk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        Path_ = Path.Combine(_folder, "rides.db");
    }

    public string Path_ { get; }

    public RideDatabase Open() => RideDatabase.Open(Path_, TimeProvider.System, NullLogger<RideDatabase>.Instance);

    public RideStore Store(RideDatabase database, StorageOptions? options = null) =>
        new(database,
            TimeProvider.System,
            // Zero commit interval: the batching window is a battery decision, and waiting it out
            // in every test would only make the suite slower without checking anything.
            options ?? new StorageOptions { CommitInterval = TimeSpan.Zero },
            NullLogger<RideStore>.Instance);

    public long Count(string table, string where = "1=1") =>
        (long)(Scalar($"SELECT COUNT(*) FROM {table} WHERE {where};") ?? 0L);

    /// <summary>
    /// For putting the file into a state the app itself would not write — a ride left without its
    /// totals, say, which is what a database from an older build looks like.
    /// </summary>
    public void Execute(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path_ }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Null means null — <c>DBNull</c> is folded away so a test can say <c>Assert.Null</c>.</summary>
    public object? Scalar(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path_ }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open on Windows is not worth failing a green test over; the temp
            // folder is the operating system's problem after that.
        }
    }
}
