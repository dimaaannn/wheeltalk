using WheelTalk.Storage;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Storage;

/// <summary>
/// Переезд файла, который уже лежит на телефоне. Единственная дверь в один конец во всём проекте:
/// поездки — данные, которых нет больше нигде, и миграция, испортившая их, ничем не чинится.
/// <para>
/// Схема v5 здесь выписана руками, а не взята из <c>Schema.Migrations</c>, и это нарочно: тест
/// обязан говорить о файле, который лежит на телефоне сегодня, а не о том, что о нём думает
/// сегодняшний код. Возьми он константы из того же места, что и миграция, — и общая ошибка стала
/// бы невидимой для обоих.
/// </para>
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class SchemaMigrationTests
{
    /// <summary>
    /// v6: поток не переносится (решение владельца 03.08.2026), поездки и их итоги остаются, время
    /// становится числом. Проверяется именно это разделение — потерять поездку при переезде нельзя,
    /// а телеметрию можно: она и так живёт сутки.
    /// </summary>
    [Fact]
    public void A_file_from_the_previous_build_keeps_its_rides_and_loses_its_stream()
    {
        using var temp = new TempDatabase();
        temp.Execute(SchemaV5);
        temp.Execute(
            """
            INSERT INTO wheel (id, mac, protocol) VALUES (1, '88:25:83:F5:75:4A', 'Veteran');

            INSERT INTO ride (id, wheel_id, started_at, ended_at, utc_offset_minutes,
                              model, version, distance_m, duration_s, moving_s, avg_speed,
                              max_speed, max_pwm, max_power, max_current, consumption_wh)
            VALUES (1, 1, '2026-07-28T17:05:00.000Z', '2026-07-28T17:35:12.250Z', 180,
                    'Sherman L', '006.0.10', 12345, 1812, 1700, 2614,
                    5230, 8140, 320000, 4500, 41500);

            INSERT INTO ride (id, wheel_id, started_at, utc_offset_minutes)
            VALUES (2, 1, '2026-07-29T09:00:00.500Z', 180);

            INSERT INTO telemetry (ride_id, wheel_id, at, speed, voltage, phase_current, current,
                                   power, pwm, battery_level, distance, totaldistance, system_temp)
            VALUES (1, 1, '2026-07-28T17:05:00.000Z', 1000, 15012, -125, 250, 37530, 1250, 87, 1234, 987654, 3400);

            INSERT INTO wheel_state (ride_id, at, charging_status, wheel_alarm)
            VALUES (1, '2026-07-28T17:05:00.000Z', 0, 0);
            """);
        temp.Execute("PRAGMA user_version = 5;");

        var database = temp.Open();
        Assert.True(database.IsWritable);

        // Поездки на месте, и время в них — unix ms, миллисекунды включительно.
        Assert.Equal(2, temp.Count("ride"));
        Assert.Equal(
            new DateTimeOffset(2026, 7, 28, 17, 5, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            temp.Scalar("SELECT started_at FROM ride WHERE id = 1;"));
        Assert.Equal(
            new DateTimeOffset(2026, 7, 28, 17, 35, 12, 250, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            temp.Scalar("SELECT ended_at FROM ride WHERE id = 1;"));
        Assert.Equal(
            new DateTimeOffset(2026, 7, 29, 9, 0, 0, 500, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            temp.Scalar("SELECT started_at FROM ride WHERE id = 2;"));

        // Итоги — вечная память покатушки, и переезд их не касается.
        Assert.Equal(12345L, temp.Scalar("SELECT distance_m FROM ride WHERE id = 1;"));
        Assert.Equal("Sherman L", temp.Scalar("SELECT model FROM ride WHERE id = 1;"));

        // Поток не переносится, и колонки у него теперь другие: ни `ride_id`, ни текстового времени.
        Assert.Equal(0, temp.Count("telemetry"));
        Assert.Equal(0, temp.Count("wheel_state"));
        Assert.Equal(0, temp.Count("pragma_table_info('telemetry')", "name = 'ride_id'"));
        Assert.Equal(1, temp.Count("pragma_table_info('telemetry')", "name = 'torque'"));
        Assert.Equal(1, temp.Count("pragma_table_info('wheel_state')", "name = 'wheel_id'"));

        // v7: колонка отметки появилась, и у колеса из старого файла она пуста — привязанным его
        // делает подключение, а не поездки, которые оно успело записать раньше (план 24 §А1).
        Assert.Equal(1, temp.Count("pragma_table_info('wheel')", "name = 'last_connected_at'"));
        Assert.Null(temp.Scalar("SELECT last_connected_at FROM wheel WHERE id = 1;"));

        var rides = new RideExporter(database).Rides();

        // Закрытая поездка отдаёт свои девять чисел — они пережили переезд, а её кадры нет.
        var closed = rides.Single(r => r.Id == 1);
        Assert.NotNull(closed.Totals);
        Assert.Equal(12345, closed.Totals.DistanceMetres);
        Assert.Equal(0, closed.Rows);

        // Открытая закрывается своим же началом: кадров, которыми её закрыть, после переезда нет.
        // Итоги при ней пусты — и это её единственный честный ответ, а не ноль километров.
        var abandoned = rides.Single(r => r.Id == 2);
        Assert.Equal(abandoned.StartedAt, abandoned.EndedAt);
        Assert.Null(abandoned.Totals);
    }

    /// <summary>Схема v5 целиком, как её видит файл на телефоне, — v1 плюс v2…v5 в готовом виде.</summary>
    private const string SchemaV5 =
        """
        CREATE TABLE wheel (
            id INTEGER PRIMARY KEY, mac TEXT NOT NULL UNIQUE, protocol TEXT NOT NULL);

        CREATE TABLE ride (
            id INTEGER PRIMARY KEY,
            wheel_id INTEGER NOT NULL REFERENCES wheel(id),
            started_at TEXT NOT NULL,
            ended_at TEXT,
            utc_offset_minutes INTEGER NOT NULL,
            model TEXT, version TEXT,
            distance_m INTEGER, duration_s INTEGER, moving_s INTEGER, avg_speed INTEGER,
            max_speed INTEGER, max_pwm INTEGER, max_power INTEGER, max_current INTEGER,
            consumption_wh INTEGER);

        CREATE TABLE telemetry (
            ride_id INTEGER NOT NULL REFERENCES ride(id),
            wheel_id INTEGER NOT NULL REFERENCES wheel(id),
            at TEXT NOT NULL,
            speed INTEGER NOT NULL, voltage INTEGER NOT NULL, phase_current INTEGER NOT NULL,
            current INTEGER NOT NULL, power INTEGER NOT NULL, pwm INTEGER NOT NULL,
            battery_level INTEGER NOT NULL, distance INTEGER NOT NULL,
            totaldistance INTEGER NOT NULL, system_temp INTEGER NOT NULL,
            temp2 INTEGER, tilt INTEGER, alert TEXT);

        CREATE TABLE wheel_state (
            ride_id INTEGER NOT NULL REFERENCES ride(id),
            at TEXT NOT NULL, charging_status INTEGER NOT NULL, wheel_alarm INTEGER NOT NULL);

        CREATE TABLE pack_state (
            ride_id INTEGER NOT NULL REFERENCES ride(id),
            at TEXT NOT NULL, pack_no INTEGER NOT NULL,
            cell_min INTEGER, cell_max INTEGER, cell_avg INTEGER,
            temp_min INTEGER, temp_max INTEGER, temp_avg INTEGER,
            health INTEGER, current INTEGER);

        CREATE TABLE setting (
            scope TEXT NOT NULL, key TEXT NOT NULL, value TEXT NOT NULL,
            PRIMARY KEY (scope, key)) WITHOUT ROWID;

        CREATE INDEX telemetry_by_ride   ON telemetry(ride_id, at);
        CREATE INDEX wheel_state_by_ride ON wheel_state(ride_id, at);
        CREATE INDEX pack_state_by_ride  ON pack_state(ride_id, at);
        """;
}
