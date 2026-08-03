using Microsoft.Data.Sqlite;

namespace WheelTalk.Storage;

/// <summary>
/// Как кончается поездка — одно правило на всё приложение (план 23 §5.4). Конец ставится
/// <b>последним кадром</b> поездки, а не «сейчас»: покатушка кончилась там, где колесо замолчало,
/// и минуты между тем и нажатием кнопки — не езда. Поездке без единого кадра концом служит её
/// начало.
/// <para>
/// Заведено отдельным типом по той же причине, что и <see cref="RideWindow"/>: правило зовётся из
/// трёх мест — кнопка (<c>RideStore.FinishRide</c>), открытие базы (<see cref="RideDatabase"/>) и
/// приход кадра после разрыва (<c>RideStore</c>). Три копии одного <c>UPDATE</c> разошлись бы на
/// первой же правке, и разошлись бы молча: каждая по отдельности выглядит верной, а поездки,
/// посчитанные на старте и на ходу, стали бы разными.
/// </para>
/// </summary>
internal static class RideClosing
{
    /// <summary>
    /// Конец поездки одним выражением: максимум времени её кадров, а если кадров нет — начало.
    /// Требует, чтобы таблица поездок звалась в запросе <c>r</c> — как того же требует
    /// <see cref="RideWindow.CorrelatedFilter"/>, вместе с которым оно и работает.
    /// </summary>
    private const string EndedAtByLastFrame =
        $"COALESCE((SELECT MAX(t.at) FROM telemetry t WHERE {RideWindow.CorrelatedFilter}), r.started_at)";

    /// <summary>Закрывает одну названную поездку. Уже закрытую не трогает: конец ставится один раз.</summary>
    public static void Close(SqliteConnection connection, SqliteTransaction? tx, long rideId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            UPDATE ride AS r
               SET ended_at = {EndedAtByLastFrame}
             WHERE r.id = $id AND r.ended_at IS NULL;
            """;
        command.Transaction = tx;
        command.Parameters.AddWithValue("$id", rideId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Закрывает открытые поездки, чей последний кадр старше <paramref name="staleBefore"/>: столько
    /// молчания означает, что прошлая сессия уже точно не та, и кадры, пришедшие после, к ней не
    /// относятся. Порог один — <see cref="StorageOptions.AbandonedRideGap"/>; меняется он у
    /// зовущего, а правило здесь.
    /// <para>
    /// <paramref name="wheelId"/> сужает до одного колеса — так спрашивает живая запись, которой
    /// есть дело только до колеса на связи. <c>null</c> значит «все», как при открытии базы.
    /// </para>
    /// </summary>
    /// <returns>Что закрыли. Пусто — значит закрывать было нечего.</returns>
    public static IReadOnlyList<long> CloseAbandoned(
        SqliteConnection connection, SqliteTransaction? tx, long staleBefore, long? wheelId = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            UPDATE ride AS r
               SET ended_at = {EndedAtByLastFrame}
             WHERE r.ended_at IS NULL
               AND ({(wheelId is null ? "1 = 1" : "r.wheel_id = $wheel")})
               AND {EndedAtByLastFrame} < $stale
            RETURNING id;
            """;
        command.Transaction = tx;
        command.Parameters.AddWithValue("$stale", staleBefore);
        if (wheelId is { } wheel) command.Parameters.AddWithValue("$wheel", wheel);

        var closed = new List<long>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) closed.Add(reader.GetInt64(0));
        return closed;
    }
}
