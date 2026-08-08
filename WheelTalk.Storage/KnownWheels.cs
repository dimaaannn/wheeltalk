using Microsoft.Extensions.Logging;

namespace WheelTalk.Storage;

/// <summary>Колесо, к которому подключались, и когда это было в последний раз.</summary>
public readonly record struct KnownWheel(string Mac, DateTimeOffset LastConnectedAt);

/// <summary>
/// Колёса, к которым это приложение подключалось, — то, что экран поиска показывает вверху списка
/// (план 24 §А). <b>Привязано = успешно подключались</b>, и другого определения нет: настройки
/// заводит только правка руками, поездку — только выезд, а подключиться и не тронуть ничего можно
/// сколько угодно раз.
/// <para>
/// Здесь только MAC и время. Имя живёт в настройках, слоем этого колеса, и второй копии ему не
/// заводится — решение владельца 08.08.2026; колонку <c>wheel.name</c> убрали в v5 по той же
/// причине.
/// </para>
/// <para>
/// Соединение на вызов, как у <see cref="SqliteSettingsStore"/>: подключение случается раз в
/// поездку, а список читается при открытии экрана — держать своё соединение тут нечего, а брать
/// чужое, принадлежащее потоку записи, нельзя.
/// </para>
/// </summary>
public sealed partial class KnownWheels(RideDatabase database, ILogger<KnownWheels> logger)
{
    /// <summary>
    /// Подключились — запоминаем момент. Протокол проставляется только при первом появлении строки:
    /// на подключении он ещё не всегда известен (Veteran и Begode опознаются по первому кадру), и
    /// обновлять им уже записанный значило бы затирать опознанное пустышкой. Своё имя протоколу даёт
    /// <see cref="RideStore"/>, когда пойдёт поток.
    /// </summary>
    public void Remember(string mac, string protocol, DateTimeOffset at)
    {
        if (!database.IsWritable || mac.Length == 0) return;

        try
        {
            using var connection = database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO wheel (mac, protocol, last_connected_at) VALUES ($mac, $protocol, $at)
                    ON CONFLICT(mac) DO UPDATE SET last_connected_at = excluded.last_connected_at;
                """;
            command.Parameters.AddWithValue("$mac", mac);
            command.Parameters.AddWithValue("$protocol", protocol);
            command.Parameters.AddWithValue("$at", at.ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            // Незаписанная отметка стоит одной строки в списке поиска. Ронять из-за неё
            // подключение, ради которого человек и пришёл, — плохая мена.
            LogRememberFailed(ex, mac);
        }
    }

    /// <summary>Свежие первыми: экран поиска показывает их сверху именно в этом порядке.</summary>
    public IReadOnlyList<KnownWheel> All()
    {
        var wheels = new List<KnownWheel>();

        try
        {
            using var connection = database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT mac, last_connected_at FROM wheel
                 WHERE last_connected_at IS NOT NULL
                 ORDER BY last_connected_at DESC;
                """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                wheels.Add(new KnownWheel(
                    reader.GetString(0), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1))));
            }
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            // Пустой список — экран поиска без верхней части: найденные колёса на нём остаются, и
            // подключиться можно по-прежнему.
            LogListFailed(ex);
        }

        return wheels;
    }

    /// <summary>
    /// «Забыть колесо» — снимается отметка, а не строка: на <c>wheel(id)</c> ссылаются поездки и весь
    /// поток, и удаление унесло бы с собой историю, которой никто не просил лишаться. Из списка
    /// привязанных колесо после этого уходит, потому что список — это как раз строки с отметкой.
    /// <para>
    /// Настройки колеса сносит не этот метод, а <c>ISettingsStore.Remove</c>: слой настроек живёт
    /// своей таблицей и своим владельцем.
    /// </para>
    /// </summary>
    public void Forget(string mac)
    {
        if (!database.IsWritable || mac.Length == 0) return;

        try
        {
            using var connection = database.Connect();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE wheel SET last_connected_at = NULL WHERE mac = $mac;";
            command.Parameters.AddWithValue("$mac", mac);
            command.ExecuteNonQuery();
            LogForgotten(mac);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            LogForgetFailed(ex, mac);
        }
    }

    [LoggerMessage(EventId = 1630, EventName = "Wheels.RememberFailed", Level = LogLevel.Error,
        Message = "Wheels.RememberFailed {Mac} — колесо не попадёт в список привязанных")]
    private partial void LogRememberFailed(Exception ex, string mac);

    [LoggerMessage(EventId = 1631, EventName = "Wheels.ListFailed", Level = LogLevel.Error,
        Message = "Wheels.ListFailed — экран поиска покажет только найденные")]
    private partial void LogListFailed(Exception ex);

    [LoggerMessage(EventId = 1632, EventName = "Wheels.Forgotten", Level = LogLevel.Information,
        Message = "Wheels.Forgotten {Mac}")]
    private partial void LogForgotten(string mac);

    [LoggerMessage(EventId = 1633, EventName = "Wheels.ForgetFailed", Level = LogLevel.Error,
        Message = "Wheels.ForgetFailed {Mac}")]
    private partial void LogForgetFailed(Exception ex, string mac);
}
