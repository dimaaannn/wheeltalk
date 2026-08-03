using Microsoft.Extensions.Time.Testing;
using WheelTalk.Core.Contracts;
using WheelTalk.Storage;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Storage;

/// <summary>
/// What the database has to get right that a file never had to: a ride is a thing with a start and
/// an end, the slow tables keep their own pace, and a recording the phone did not survive is still
/// a recording when the app comes back.
/// <para>
/// После плана 23 сюда добавилось главное: <b>поток существует сам по себе</b>, а поездка — окно во
/// времени поверх него. Отсюда и проверки: строка пишется без всякой поездки, поездка находит свои
/// строки диапазоном, удаление поездки поток не трогает, а чистка по сроку не смеет опередить
/// закрытие поездок.
/// </para>
/// </summary>
public class RideStoreTests
{
    private const string Mac = "88:25:83:F5:75:4A";

    private static readonly DateTimeOffset Start =
        new(2026, 7, 28, 20, 5, 0, TimeSpan.FromHours(3));

    private static long Ms(DateTimeOffset at) => at.ToUnixTimeMilliseconds();

    [Fact]
    public async Task A_ride_gets_its_rows_and_a_closing_stamp()
    {
        using var temp = new TempDatabase();
        var database = temp.Open();
        await using (var store = temp.Store(database))
        {
            store.BeginRide();
            for (int i = 0; i < 5; i++)
            {
                store.Write(Mac, "Veteran", Sample(speed: 1000 + i), Start.AddSeconds(i));
            }

            await store.CloseRideAsync();

            Assert.Equal(1, temp.Count("ride"));
            Assert.Equal(5, temp.Count("telemetry"));
            Assert.Equal(5, store.RowsWritten);
        }

        // Unix ms пошли в базу, местное время — на экран: зона живёт при поездке и служит показу.
        Assert.Equal(Ms(Start), temp.Scalar("SELECT started_at FROM ride;"));
        Assert.Equal(Ms(Start.AddSeconds(4)), temp.Scalar("SELECT ended_at FROM ride;"));
        Assert.Equal(180L, temp.Scalar("SELECT utc_offset_minutes FROM ride;"));
    }

    /// <summary>
    /// Главное, ради чего затевался план 23 §5: истории больше не нужно разрешения. Строка потока
    /// пишется сама по себе, а поездок при этом нет вовсе — раньше без нажатой кнопки не было ни
    /// одной строки, и графику было не из чего строиться.
    /// </summary>
    [Fact]
    public async Task The_stream_is_written_with_no_ride_marked_at_all()
    {
        using var temp = new TempDatabase();
        await using var store = temp.Store(temp.Open());

        for (int i = 0; i < 5; i++) store.Write(Mac, "Veteran", Sample(), Start.AddSeconds(i));
        await store.FlushAsync();

        Assert.Equal(5, temp.Count("telemetry"));
        Assert.Equal(1, temp.Count("wheel"));
        Assert.Equal(0, temp.Count("ride"));
        // Медленные таблицы тоже: план 9 просит `wheel_state` раз в минуту при подключённом колесе
        // и без поездки — на стоянке строится кривая «напряжение покоя ↔ заряд».
        Assert.Equal(1, temp.Count("wheel_state"));
    }

    /// <summary>
    /// Разметка — это окно во времени, и своими считаются строки внутри него. Кадры до нажатия
    /// кнопки и после «стоп» остаются в потоке, но поездке не принадлежат.
    /// </summary>
    [Fact]
    public async Task A_ride_owns_the_rows_inside_its_window_and_no_others()
    {
        using var temp = new TempDatabase();
        var database = temp.Open();
        await using (var store = temp.Store(database))
        {
            for (int i = 0; i < 3; i++) store.Write(Mac, "Veteran", Sample(), Start.AddSeconds(i));

            store.BeginRide();
            for (int i = 3; i < 8; i++) store.Write(Mac, "Veteran", Sample(), Start.AddSeconds(i));
            await store.CloseRideAsync();

            for (int i = 8; i < 12; i++) store.Write(Mac, "Veteran", Sample(), Start.AddSeconds(i));
            await store.FlushAsync();
        }

        Assert.Equal(12, temp.Count("telemetry"));

        var ride = Assert.Single(new RideExporter(database).Rides());
        Assert.Equal(5, ride.Rows);
        Assert.Equal(Start.AddSeconds(3), ride.StartedAt);
        Assert.Equal(Start.AddSeconds(7), ride.EndedAt);
    }

    /// <summary>
    /// Model and firmware do not change during a ride, so they belong to the ride and not to each
    /// of its thousands of rows — but they only arrive with a decoded frame, which is not always
    /// the first snapshot.
    /// </summary>
    [Fact]
    public async Task The_model_is_filled_in_when_it_arrives_rather_than_at_the_start()
    {
        using var temp = new TempDatabase();
        var database = temp.Open();
        await using var store = temp.Store(database);

        store.BeginRide();
        store.Write(Mac, "Veteran", Sample(), Start);
        await store.FlushAsync();
        Assert.Null(temp.Scalar("SELECT model FROM ride;"));

        store.Write(Mac, "Veteran", Sample() with { Model = "Sherman L", Version = "006.0.10" }, Start.AddSeconds(1));
        await store.FlushAsync();

        Assert.Equal("Sherman L", temp.Scalar("SELECT model FROM ride;"));
        Assert.Equal("006.0.10", temp.Scalar("SELECT version FROM ride;"));
    }

    /// <summary>
    /// A ride belongs to one wheel. The recorder already closed its file when the MAC changed; the
    /// database has to do the same, or two wheels end up in one ride and every summary over it lies.
    /// Кнопку при этом никто не отпускал — разметка продолжается на новом колесе своей поездкой.
    /// </summary>
    [Fact]
    public async Task Another_wheel_ends_the_ride_and_starts_the_next()
    {
        using var temp = new TempDatabase();
        var database = temp.Open();
        await using var store = temp.Store(database);

        store.BeginRide();
        store.Write(Mac, "Veteran", Sample(), Start);
        store.Write("88:25:83:F2:1A:98", "Gotway", Sample(), Start.AddSeconds(1));
        await store.CloseRideAsync();

        Assert.Equal(2, temp.Count("ride"));
        Assert.Equal(2, temp.Count("wheel"));
        Assert.Equal(0, temp.Count("ride", "ended_at IS NULL"));
    }

    /// <summary>
    /// The alert goes on the row where it happened. Ours is the last value seen and would otherwise
    /// repeat on every row until something replaced it — the original empties its buffer on read.
    /// </summary>
    [Fact]
    public async Task An_alert_lands_on_one_row_and_not_on_the_ones_after_it()
    {
        using var temp = new TempDatabase();
        var database = temp.Open();
        await using var store = temp.Store(database);

        store.Write(Mac, "Veteran", Sample(), Start);
        for (int i = 1; i <= 3; i++)
        {
            store.Write(Mac, "Veteran", Sample() with { Alert = "Speed2" }, Start.AddSeconds(i));
        }

        await store.FlushAsync();

        Assert.Equal(1, temp.Count("telemetry", "alert IS NOT NULL"));
        Assert.Equal("Speed2", temp.Scalar("SELECT alert FROM telemetry WHERE alert IS NOT NULL;"));
    }

    /// <summary>
    /// Одиннадцать величин, которых база не видела до плана 23. Каждая сообщается ровно одним
    /// семейством протоколов, и у остальных её нет — не ноль, а нет: NULL значит «колесо молчит»,
    /// и это несущий смысл. Ровная линия по нулю на графике выглядит как показание.
    /// </summary>
    [Fact]
    public async Task What_only_one_protocol_reports_is_null_for_the_others()
    {
        using var temp = new TempDatabase();
        await using var store = temp.Store(temp.Open());

        store.Write("88:25:83:F2:1A:11", "KingSong", Sample() with
        {
            WheelType = WheelType.KingSong,
            CpuLoad = 42,
            SpeedLimit = 31.5,
            FanStatus = 1,
            OutputRaw = 8125,
            ModeStr = "3",
        }, Start);
        store.Write(Mac, "Veteran", Sample(), Start.AddSeconds(1));
        await store.FlushAsync();

        Assert.Equal(42L, temp.Scalar("SELECT cpu_load FROM telemetry WHERE cpu_load IS NOT NULL;"));
        Assert.Equal(3150L, temp.Scalar("SELECT speed_limit FROM telemetry WHERE speed_limit IS NOT NULL;"));
        Assert.Equal(8125L, temp.Scalar("SELECT hw_pwm FROM telemetry WHERE hw_pwm IS NOT NULL;"));
        Assert.Equal("3", temp.Scalar("SELECT mode FROM telemetry WHERE mode IS NOT NULL;"));

        // У Veteran ничего этого нет — ни своего, ни чужого.
        Assert.Equal(1, temp.Count("telemetry", "cpu_load IS NULL AND torque IS NULL AND roll IS NULL"));
    }

    /// <summary>
    /// A minute apart is fine for watching a pack warm up and useless for catching the instant a
    /// charger was plugged in, so the slow tables get a row on the clock *and* on any change. This
    /// is the branch the replay dump cannot exercise: it is built from one template frame, so its
    /// charging status and alarm flag never move.
    /// </summary>
    [Fact]
    public async Task The_slow_table_ticks_once_a_minute_and_again_the_moment_something_changes()
    {
        using var temp = new TempDatabase();
        var database = temp.Open();
        await using var store = temp.Store(database);

        store.Write(Mac, "Veteran", Sample(), Start);                        // first row, always
        store.Write(Mac, "Veteran", Sample(), Start.AddSeconds(10));         // nothing changed, too soon
        store.Write(Mac, "Veteran", Sample(), Start.AddSeconds(70));         // the minute came round
        await store.FlushAsync();
        Assert.Equal(2, temp.Count("wheel_state"));

        store.Write(Mac, "Veteran", Sample() with { ChargingStatus = 1 }, Start.AddSeconds(75));
        await store.FlushAsync();

        Assert.Equal(3, temp.Count("wheel_state"));
        Assert.Equal(1L, temp.Scalar("SELECT charging_status FROM wheel_state ORDER BY at DESC LIMIT 1;"));
    }

    /// <summary>
    /// MTen3 reports nothing about its battery and never will — it has a total voltage and that is
    /// already in <c>telemetry</c>. No rows should appear for such a wheel rather than rows of
    /// zeroes, which on a graph would look like a pack at absolute zero volts.
    /// </summary>
    [Fact]
    public async Task A_wheel_without_a_battery_management_system_gets_no_pack_rows()
    {
        using var temp = new TempDatabase();
        var database = temp.Open();
        await using var store = temp.Store(database);

        store.Write("88:25:83:F2:1A:98", "Gotway", Sample(), Start);
        await store.FlushAsync();

        Assert.Equal(0, temp.Count("pack_state"));
    }

    [Fact]
    public async Task A_pack_that_reports_is_recorded_by_its_spread()
    {
        using var temp = new TempDatabase();
        var database = temp.Open();
        await using var store = temp.Store(database);

        var snapshot = Sample();
        snapshot.Bms1.Voltage = 150.12;
        snapshot.Bms1.MinCell = 4.167;
        snapshot.Bms1.MaxCell = 4.190;
        snapshot.Bms1.AvgCell = 4.180;
        snapshot.Bms1.Temp1 = 31.5;
        snapshot.Bms1.Temp2 = 33.25;
        // Sensors three to six are unpopulated on this pack and read exactly zero. Counting them
        // would put the minimum at 0 °C and the average halfway to it.
        store.Write(Mac, "Veteran", snapshot, Start);
        await store.FlushAsync();

        Assert.Equal(1, temp.Count("pack_state"));
        Assert.Equal(4167L, temp.Scalar("SELECT cell_min FROM pack_state;"));
        Assert.Equal(4190L, temp.Scalar("SELECT cell_max FROM pack_state;"));
        Assert.Equal(3150L, temp.Scalar("SELECT temp_min FROM pack_state;"));
        Assert.Equal(3325L, temp.Scalar("SELECT temp_max FROM pack_state;"));
        // No decoder of ours produces a health figure yet, and a zero would claim a dead pack.
        Assert.Null(temp.Scalar("SELECT health FROM pack_state;"));
    }

    /// <summary>
    /// Cell voltages move with every change in load, so a pack is sampled on the clock rather than
    /// watched for changes. Written the other way it produced 106 rows in a minute and a half of
    /// replay where four were meant — the drift of an analogue value is not an event.
    /// </summary>
    [Fact]
    public async Task A_pack_is_sampled_on_the_clock_and_not_on_every_flicker_of_a_cell()
    {
        using var temp = new TempDatabase();
        var database = temp.Open();
        await using var store = temp.Store(database);

        for (int i = 0; i < 30; i++)
        {
            var snapshot = Sample();
            snapshot.Bms1.Voltage = 150.12;
            snapshot.Bms1.MinCell = 4.167 + i * 0.001;   // the pack breathing under load
            snapshot.Bms1.MaxCell = 4.190;
            snapshot.Bms1.AvgCell = 4.180;
            store.Write(Mac, "Veteran", snapshot, Start.AddSeconds(i));
        }

        await store.FlushAsync();

        // One at the start of the ride, and nothing else inside the minute.
        Assert.Equal(1, temp.Count("pack_state"));
    }

    /// <summary>
    /// The phone died mid-ride. Every row is on disk and only the closing stamp is missing, so the
    /// next start closes the ride where its last row is — и делает это, потому что с последнего
    /// кадра прошло больше трёх часов: столько молчания означает, что прошлая сессия точно не та.
    /// </summary>
    [Fact]
    public async Task A_ride_the_app_never_finished_is_closed_at_the_next_start()
    {
        using var temp = new TempDatabase();
        var database = temp.Open();
        await using var store = temp.Store(database);

        store.BeginRide();
        store.Write(Mac, "Veteran", Sample(), Start);
        store.Write(Mac, "Veteran", Sample(), Start.AddSeconds(30));
        await store.FlushAsync();
        Assert.Equal(1, temp.Count("ride", "ended_at IS NULL"));

        // Nothing else stands in for a process that was killed: the store is simply never told.
        temp.Open(now: At(Start.AddHours(4)));

        Assert.Equal(0, temp.Count("ride", "ended_at IS NULL"));
        Assert.Equal(Ms(Start.AddSeconds(30)), temp.Scalar("SELECT ended_at FROM ride;"));
    }

    /// <summary>
    /// Убило посреди покатушки, перезапустил через пять минут — поездка та же, и кадры лягут в неё
    /// же (план 23 §5.4). Три часа здесь не «когда кончилась поездка», а «прошлая сессия уже точно
    /// не та»; пять минут — та самая.
    /// </summary>
    [Fact]
    public async Task A_ride_left_open_minutes_ago_carries_on_instead_of_starting_a_second_one()
    {
        using var temp = new TempDatabase();
        await using (var store = temp.Store(temp.Open()))
        {
            store.BeginRide();
            store.Write(Mac, "Veteran", Sample(), Start);
            await store.FlushAsync();
        }

        // Приложение убито: `CloseRideAsync` никто не звал, и строка осталась без `ended_at`.
        temp.Execute("UPDATE ride SET ended_at = NULL;");

        var database = temp.Open(now: At(Start.AddMinutes(5)));
        Assert.Equal(1, temp.Count("ride", "ended_at IS NULL"));

        await using (var store = temp.Store(database))
        {
            store.BeginRide();
            store.Write(Mac, "Veteran", Sample(), Start.AddMinutes(6));
            await store.CloseRideAsync();
        }

        Assert.Equal(1, temp.Count("ride"));
        Assert.Equal(Ms(Start), temp.Scalar("SELECT started_at FROM ride;"));
        Assert.Equal(Ms(Start.AddMinutes(6)), temp.Scalar("SELECT ended_at FROM ride;"));
    }

    /// <summary>
    /// A file written by a newer build may have columns this one has never heard of. Refusing to
    /// write is the only honest answer; the alternative is a ride recorded into half a schema.
    /// </summary>
    [Fact]
    public async Task A_database_from_a_newer_build_is_left_alone()
    {
        using var temp = new TempDatabase();
        temp.Open();
        temp.Execute("PRAGMA user_version = 999;");

        var database = temp.Open();
        Assert.False(database.IsWritable);

        await using var store = temp.Store(database);
        store.BeginRide();
        store.Write(Mac, "Veteran", Sample(), Start);
        await store.FlushAsync();

        Assert.Equal(0, temp.Count("ride"));
        Assert.Equal(0, temp.Count("telemetry"));
    }

    /// <summary>
    /// Удаление поездки уносит саму поездку и ничего больше. Раньше оно забирало и данные — это и
    /// было то, от чего ушли: их нельзя было чистить, не трогая поездок (план 23 §1, §5.1 п. 5).
    /// Поток переживает удаление и уходит своим сроком.
    /// </summary>
    [Fact]
    public async Task Deleting_a_ride_leaves_the_stream_where_it_was()
    {
        using var temp = new TempDatabase();
        await using var store = temp.Store(temp.Open());

        store.BeginRide();
        for (int i = 0; i < 5; i++) store.Write(Mac, "Veteran", Sample(), Start.AddSeconds(i));
        await store.CloseRideAsync();

        store.BeginRide();
        for (int i = 0; i < 5; i++) store.Write(Mac, "Veteran", Sample(), Start.AddMinutes(10).AddSeconds(i));
        await store.CloseRideAsync();

        long doomed = (long)temp.Scalar("SELECT MIN(id) FROM ride;")!;
        await store.DeleteRideAsync(doomed);

        Assert.Equal(1, temp.Count("ride"));
        Assert.Equal(10, temp.Count("telemetry"));
    }

    /// <summary>
    /// The ride being recorded is not finished being written, and half of it on disk is not a ride
    /// anyone asked to keep or to lose. The list screen does not offer the command; the store
    /// refuses it anyway, because "the screen will not ask" is not a guarantee.
    /// </summary>
    [Fact]
    public async Task The_ride_being_recorded_right_now_cannot_be_deleted()
    {
        using var temp = new TempDatabase();
        await using var store = temp.Store(temp.Open());

        store.BeginRide();
        store.Write(Mac, "Veteran", Sample(), Start);
        await store.FlushAsync();

        await store.DeleteRideAsync(store.CurrentRideId);

        Assert.Equal(1, temp.Count("ride"));
        Assert.Equal(1, temp.Count("telemetry"));
    }

    /// <summary>
    /// Ток пакета BMS сообщает сама, и это единственная измеренная величина рядом с вычисленным
    /// током в `telemetry` — тем самым, чей знак означает направление движения, а не энергии
    /// (полевая запись 28.07.2026). Ноль пишется как NULL: у MTen3 BMS нет вовсе, и ровная линия
    /// по нулю выглядела бы как показание.
    /// </summary>
    [Fact]
    public async Task What_the_pack_says_about_its_own_current_is_kept_apart_from_the_computed_one()
    {
        using var temp = new TempDatabase();
        await using var store = temp.Store(temp.Open());

        var loaded = Sample();
        loaded.Bms1.Voltage = 84.0;
        loaded.Bms1.MaxCell = 4.19;
        loaded.Bms1.Current = -12.5;
        store.Write(Mac, "Veteran", loaded, Start);
        await store.FlushAsync();

        Assert.Equal(-1250L, temp.Scalar("SELECT current FROM pack_state WHERE pack_no = 1;"));
        Assert.Equal(0, temp.Count("pack_state", "pack_no = 2"));
    }

    private static FakeTimeProvider At(DateTimeOffset now) => new(now);

    private static TelemetrySnapshot Sample(int speed = 1000) => new()
    {
        SpeedRaw = speed,
        VoltageRaw = 15012,
        PhaseCurrentRaw = -125,
        CurrentRaw = 250,
        PowerRaw = 37530,
        Pwm = 12.5,
        Battery = 87,
        WheelDistance = 1234,
        TotalDistance = 987654,
        TemperatureRaw = 3400,
        Angle = 1.5,
        WheelType = WheelType.Veteran,
    };
}
