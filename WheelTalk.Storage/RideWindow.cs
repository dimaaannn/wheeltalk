using Microsoft.Data.Sqlite;

namespace WheelTalk.Storage;

/// <summary>
/// Где искать строки поездки. Связь между поездкой и потоком — по времени, а не колонкой (план 23
/// §5.1, решение владельца 03.08.2026): в телеметрии нет <c>ride_id</c>, и строки покатушки
/// находятся диапазоном по тому же индексу <c>(wheel_id, at)</c>, что и всё остальное.
/// <para>
/// Заведено типом, а не тремя переменными в каждом читателе, ровно по одной причине: экспорт,
/// итоги и закрытие поездки обязаны понимать её границы одинаково. Три копии условия разошлись бы
/// на первой же правке — и разошлись бы молча, потому что каждая по отдельности выглядит верной.
/// </para>
/// </summary>
/// <param name="To">
/// Конец окна. У поездки, которая идёт прямо сейчас, это <see cref="Open"/>: <c>ended_at IS NULL</c>
/// значит ровно «ещё не кончилась», и всё, что приходит, — её.
/// </param>
internal readonly record struct RideWindow(long WheelId, long From, long To)
{
    /// <summary>Конец у незакрытой поездки — дальше любого мыслимого отсчёта.</summary>
    public const long Open = long.MaxValue;

    /// <summary>Условие «строка принадлежит этой поездке», одно на всех читателей.</summary>
    public const string Filter = "wheel_id = $wheel AND at BETWEEN $from AND $to";

    /// <summary>То же условие в подзапросе по строке таблицы <c>ride</c>, для одного запроса на список.</summary>
    public const string CorrelatedFilter =
        "t.wheel_id = r.wheel_id AND t.at BETWEEN r.started_at AND COALESCE(r.ended_at, 9223372036854775807)";

    public void Bind(SqliteCommand command)
    {
        command.Parameters.AddWithValue("$wheel", WheelId);
        command.Parameters.AddWithValue("$from", From);
        command.Parameters.AddWithValue("$to", To);
    }

    /// <summary>Границы поездки, или ничего, если такой поездки в файле нет.</summary>
    public static RideWindow? Read(SqliteConnection connection, SqliteTransaction? tx, long rideId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT wheel_id, started_at, ended_at FROM ride WHERE id = $id;";
        command.Transaction = tx;
        command.Parameters.AddWithValue("$id", rideId);

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        return new RideWindow(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.IsDBNull(2) ? Open : reader.GetInt64(2));
    }
}
