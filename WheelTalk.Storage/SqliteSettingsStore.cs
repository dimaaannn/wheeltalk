using Microsoft.Extensions.Logging;
using WheelTalk.Core.Settings;

namespace WheelTalk.Storage;

/// <summary>
/// The settings store on top of the ride database. Deliberately dumb: it knows scopes, keys and
/// text, and nothing about which layer wins — that is <see cref="LayeredSettings"/>'s job, in the
/// core, where it can be tested.
/// <para>
/// A connection per call rather than one held open. Settings are written when a finger moves and
/// read when a page opens, so the cost is nothing, and sharing the recorder's connection would put
/// a page's write on the thread that must never wait.
/// </para>
/// </summary>
public sealed partial class SqliteSettingsStore(RideDatabase database, ILogger<SqliteSettingsStore> logger)
    : ISettingsStore
{
    public IReadOnlyDictionary<string, string> Read(string scope)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var connection = database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT key, value FROM setting WHERE scope = $scope;";
            command.Parameters.AddWithValue("$scope", scope);

            using var reader = command.ExecuteReader();
            while (reader.Read()) values[reader.GetString(0)] = reader.GetString(1);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            // Settings that cannot be read leave the factory defaults showing, which is a working
            // app. Throwing here would take the screen down over a preference.
            LogReadFailed(ex, scope);
        }

        return values;
    }

    public void Write(string scope, string key, string? value)
    {
        if (!database.IsWritable) return;

        try
        {
            using var connection = database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = value is null
                ? "DELETE FROM setting WHERE scope = $scope AND key = $key;"
                : """
                  INSERT INTO setting (scope, key, value) VALUES ($scope, $key, $value)
                      ON CONFLICT(scope, key) DO UPDATE SET value = excluded.value;
                  """;
            command.Parameters.AddWithValue("$scope", scope);
            command.Parameters.AddWithValue("$key", key);
            if (value is not null) command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            LogWriteFailed(ex, scope, key);
        }
    }

    public void Remove(string scope)
    {
        if (!database.IsWritable) return;

        try
        {
            using var connection = database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM setting WHERE scope = $scope;";
            command.Parameters.AddWithValue("$scope", scope);
            LogScopeRemoved(scope, command.ExecuteNonQuery());
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            LogWriteFailed(ex, scope, "*");
        }
    }

    [LoggerMessage(EventId = 1620, EventName = "Settings.ReadFailed", Level = LogLevel.Error,
        Message = "Settings.ReadFailed {Scope} — falling back to the shipped defaults")]
    private partial void LogReadFailed(Exception ex, string scope);

    [LoggerMessage(EventId = 1621, EventName = "Settings.WriteFailed", Level = LogLevel.Error,
        Message = "Settings.WriteFailed {Scope} {Key}")]
    private partial void LogWriteFailed(Exception ex, string scope, string key);

    [LoggerMessage(EventId = 1622, EventName = "Settings.ScopeRemoved", Level = LogLevel.Information,
        Message = "Settings.ScopeRemoved {Scope} — {Count} настроек")]
    private partial void LogScopeRemoved(string scope, int count);
}
