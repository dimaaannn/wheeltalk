using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Contracts;

namespace WheelTalk.Storage;

/// <summary>
/// Writes the telemetry stream into the database. The whole point of this class is the thread it
/// does not run on: telemetry arrives on the GATT callback, the one feeding the decoder twenty-odd
/// frames a second, and a WAL commit with three index updates has no business being there. So
/// <see cref="Write"/> only queues, and one background loop owns the connection and does every write.
/// <para>
/// Rows pile up for <see cref="StorageOptions.CommitInterval"/> and go in as one transaction. That
/// window is also what is lost if the process dies mid-ride — the rows before it are on disk, and
/// the ride left open gets closed at the next start (<see cref="RideDatabase"/>).
/// </para>
/// <para>
/// <b>Поток и поездка — разные вещи</b> (план 23 §5, решения владельца 03.08.2026). Строка
/// телеметрии принадлежит колесу и пишется сама по себе; поездка её лишь размечает —
/// <see cref="BeginRide"/> открывает окно, <see cref="CloseRideAsync"/> закрывает. Кто и когда
/// зовёт <see cref="Write"/>, решает переключатель записи снаружи: здесь про то не знают.
/// </para>
/// </summary>
public sealed partial class RideStore : IAsyncDisposable
{
    private readonly RideDatabase _database;
    private readonly TimeProvider _timeProvider;
    private readonly StorageOptions _options;
    private readonly ILogger<RideStore> _logger;

    private readonly Channel<Work> _queue = Channel.CreateUnbounded<Work>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly Task _loop;

    private int _rowsWritten;
    private long _rideId;

    // Everything below is touched only by the loop.
    private SqliteConnection? _connection;
    private SqliteCommand? _insertTelemetry;
    private string _mac = "";
    private long _wheelId;

    /// <summary>«Размечаем» — кнопка нажата. Строка поездки заводится первым же отсчётом после этого.</summary>
    private bool _marking;

    private string _model = "";
    private string _version = "";
    private string _lastAlert = "";
    private (int Charging, bool Alarm)? _lastWheelState;
    private DateTimeOffset _lastWheelStateAt;
    private readonly PackSample?[] _lastPacks = new PackSample?[2];
    private readonly DateTimeOffset[] _lastPackAt = new DateTimeOffset[2];

    public RideStore(
        RideDatabase database,
        TimeProvider timeProvider,
        StorageOptions options,
        ILogger<RideStore> logger)
    {
        _database = database;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
        _loop = Task.Run(RunAsync);
    }

    /// <summary>Rows of telemetry committed so far — what the recording screen counts.</summary>
    public int RowsWritten => Volatile.Read(ref _rowsWritten);

    /// <summary>The ride being written, or 0 before the first row has opened one.</summary>
    public long CurrentRideId => Interlocked.Read(ref _rideId);

    /// <summary>
    /// Queues one snapshot. Returns immediately and never touches the database — see the class
    /// remarks for why that matters. A snapshot for a different wheel ends the current ride: a ride
    /// belongs to one wheel by definition, but the stream carries on either way.
    /// </summary>
    public void Write(string mac, string protocol, TelemetrySnapshot snapshot, DateTimeOffset at)
    {
        if (!_database.IsWritable || mac.Length == 0) return;

        _queue.Writer.TryWrite(new Row(mac, protocol, at, snapshot));
    }

    /// <summary>
    /// «Отсюда покатушка». Сама строка поездки заводится первым отсчётом после этого — до него
    /// неизвестно ни колесо, ни время, а поездка без единого кадра вышла бы строкой с двумя датами
    /// и пустыми итогами.
    /// </summary>
    public void BeginRide()
    {
        if (!_database.IsWritable) return;

        _queue.Writer.TryWrite(new MarkRide());
    }

    /// <summary>Finishes the ride and waits until it is on disk, so a caller can report it as saved.</summary>
    public Task CloseRideAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(new CloseRide { Done = done })) return Task.CompletedTask;

        return done.Task;
    }

    /// <summary>
    /// Removes a ride — the row and its totals, and nothing else. Строки потока остаются: очистки
    /// раздельны и друг о друге не знают (план 23 §5.1 п. 5), а телеметрию удаляет срок хранения.
    /// Раньше удаление поездки уносило с собой и данные, и это было ровно то, от чего ушли: их
    /// нельзя было чистить, не трогая поездок.
    /// <para>
    /// Goes through the same queue as everything else and for the same reason: SQLite takes one
    /// writer, and a delete from the list screen arriving on its own connection while the writer
    /// holds the file is a busy error, not a delete. The ride being recorded right now is refused —
    /// it is not finished being written.
    /// </para>
    /// </summary>
    public Task DeleteRideAsync(long rideId)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(new DeleteRide(rideId) { Done = done })) return Task.CompletedTask;

        return done.Task;
    }

    /// <summary>Waits until everything queued so far has been committed. For tests and for shutdown.</summary>
    public Task FlushAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(new Barrier { Done = done })) return Task.CompletedTask;

        return done.Task;
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        try
        {
            await _loop.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException ex)
        {
            LogShutdownStuck(ex);
        }
    }

    private async Task RunAsync()
    {
        var reader = _queue.Reader;
        try
        {
            while (await reader.WaitToReadAsync())
            {
                // Let a batch accumulate before touching the disk. The wait is the whole saving:
                // one transaction for seven rows instead of seven for seven.
                if (_options.CommitInterval > TimeSpan.Zero)
                {
                    await Task.Delay(_options.CommitInterval, _timeProvider);
                }

                Drain(reader);
            }
        }
        finally
        {
            Drain(reader);
            // Shutting down is still an ending: leaving ended_at empty would make an orderly stop
            // look exactly like the phone dying.
            RunGuarded(() => FinishRide());
            _insertTelemetry?.Dispose();
            _connection?.Dispose();
        }
    }

    private void Drain(ChannelReader<Work> reader)
    {
        List<Work>? batch = null;
        while (reader.TryRead(out var work)) (batch ??= []).Add(work);
        if (batch is null) return;

        RunGuarded(() =>
        {
            var connection = Connection();
            using var tx = connection.BeginTransaction();
            foreach (var work in batch) Apply(work, tx);
            tx.Commit();
        });

        // After the commit, not before: a caller waiting on this is waiting for "on disk".
        foreach (var work in batch) work.Done?.TrySetResult();
    }

    /// <summary>
    /// A batch that fails must not take the recording down with it. The connection goes, because a
    /// failure can leave one mid-transaction, and the next batch opens a fresh one.
    /// </summary>
    private void RunGuarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            LogWriteFailed(ex);
            _insertTelemetry?.Dispose();
            _insertTelemetry = null;
            _connection?.Dispose();
            _connection = null;
        }
    }

    private void Apply(Work work, SqliteTransaction tx)
    {
        switch (work)
        {
            case Row row:
                ApplyRow(row, tx);
                break;
            case MarkRide:
                _marking = true;
                break;
            case CloseRide:
                // Разметка снимается только здесь. Смена колеса поездку тоже заканчивает, но
                // кнопку никто не отпускал — на новом колесе разметка продолжится своей поездкой.
                _marking = false;
                FinishRide(tx);
                break;
            case DeleteRide delete:
                DeleteRide_(delete.RideId, tx);
                break;
            case Barrier:
                break;
        }
    }

    private void DeleteRide_(long rideId, SqliteTransaction tx)
    {
        if (rideId == _rideId)
        {
            LogDeleteRefused(rideId);
            return;
        }

        using var command = Command("DELETE FROM ride WHERE id = $id;", tx);
        command.Parameters.AddWithValue("$id", rideId);
        if (command.ExecuteNonQuery() > 0) LogRideDeleted(rideId);
    }

    private void ApplyRow(Row row, SqliteTransaction tx)
    {
        if (row.Mac != _mac) SwitchWheel(row, tx);
        if (_marking && _rideId == 0) StartRide(row, tx);

        // Model and firmware arrive with the first decoded frame, which is not always the first
        // snapshot — the ride is opened before they are known and filled in when they turn up.
        if (_rideId != 0 && _model.Length == 0 && row.Snapshot.Model.Length > 0) NameRide(row.Snapshot, tx);

        InsertTelemetry(row, tx);
        WriteSlowTables(row, tx);
        Interlocked.Increment(ref _rowsWritten);
    }

    /// <summary>
    /// Другое колесо. Поездка принадлежит одному колесу по определению, поэтому она здесь и
    /// кончается; поток же продолжается — просто под другим <c>wheel_id</c>. Всё, что копилось про
    /// прежнее колесо (последняя тревога, состояния, пакеты), сбрасывается: оно про тот поток, а не
    /// про этот.
    /// </summary>
    private void SwitchWheel(Row row, SqliteTransaction tx)
    {
        if (_rideId != 0) FinishRide(tx);

        _mac = row.Mac;
        _wheelId = EnsureWheel(row.Mac, row.Protocol, tx);
        _lastAlert = "";
        _lastWheelState = null;
        _lastWheelStateAt = default;
        Array.Clear(_lastPacks);
        Array.Clear(_lastPackAt);
    }

    private void StartRide(Row row, SqliteTransaction tx)
    {
        _model = "";
        _version = "";

        // Открытая поездка этого колеса — не помеха, а она же и есть: приложение убили посреди
        // покатушки, и RideDatabase оставил её открытой, потому что с последнего кадра прошло
        // немного (план 23 §5.4). Кадры лягут в неё же, а не в новую поверх старой.
        if (AdoptOpenRide(tx)) return;

        using var command = Command(
            """
            INSERT INTO ride (wheel_id, started_at, utc_offset_minutes)
            VALUES ($wheel, $started, $offset);
            SELECT last_insert_rowid();
            """, tx);
        command.Parameters.AddWithValue("$wheel", _wheelId);
        command.Parameters.AddWithValue("$started", row.At.ToUnixTimeMilliseconds());
        // The zone the ride happened in. The export prints local time, as the original does, and
        // without this there is no way back to it from a UTC stamp.
        command.Parameters.AddWithValue("$offset", (int)row.At.Offset.TotalMinutes);

        long id = (long)(command.ExecuteScalar() ?? 0L);
        Interlocked.Exchange(ref _rideId, id);
        Volatile.Write(ref _rowsWritten, 0);
        LogRideStarted(id, row.Mac);
    }

    /// <summary>
    /// Подхватывает незакрытую поездку этого колеса, если она есть. Заводить вторую поверх неё
    /// нельзя: две поездки на одном колесе не могут пересекаться (решение владельца 03.08.2026) —
    /// иначе строка потока принадлежит двум поездкам сразу, и обе считают её своей.
    /// </summary>
    private bool AdoptOpenRide(SqliteTransaction tx)
    {
        using var command = Command(
            """
            SELECT id, COALESCE(model, ''), COALESCE(version, '')
              FROM ride WHERE wheel_id = $wheel AND ended_at IS NULL
             ORDER BY started_at DESC LIMIT 1;
            """, tx);
        command.Parameters.AddWithValue("$wheel", _wheelId);

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return false;

        long id = reader.GetInt64(0);
        _model = reader.GetString(1);
        _version = reader.GetString(2);
        Interlocked.Exchange(ref _rideId, id);
        Volatile.Write(ref _rowsWritten, 0);
        LogRideResumed(id, _mac);
        return true;
    }

    private long EnsureWheel(string mac, string protocol, SqliteTransaction tx)
    {
        using var command = Command(
            """
            INSERT INTO wheel (mac, protocol) VALUES ($mac, $protocol)
                ON CONFLICT(mac) DO UPDATE SET protocol = excluded.protocol;
            SELECT id FROM wheel WHERE mac = $mac;
            """, tx);
        command.Parameters.AddWithValue("$mac", mac);
        command.Parameters.AddWithValue("$protocol", protocol);
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private void NameRide(TelemetrySnapshot snapshot, SqliteTransaction tx)
    {
        _model = snapshot.Model;
        _version = snapshot.Version;

        using var command = Command("UPDATE ride SET model = $model, version = $version WHERE id = $id;", tx);
        command.Parameters.AddWithValue("$model", _model);
        command.Parameters.AddWithValue("$version", _version);
        command.Parameters.AddWithValue("$id", _rideId);
        command.ExecuteNonQuery();
    }

    private void FinishRide(SqliteTransaction? tx = null)
    {
        if (_rideId == 0) return;

        // Последним кадром этого колеса, а не «сейчас»: покатушка кончилась там, где колесо
        // замолчало, и лишние минуты между тем и нажатием кнопки — не езда.
        using (var command = Command(
            """
            UPDATE ride
               SET ended_at = COALESCE(
                       (SELECT MAX(at) FROM telemetry
                         WHERE wheel_id = $wheel AND at >= ride.started_at),
                       started_at)
             WHERE id = $id;
            """, tx))
        {
            command.Parameters.AddWithValue("$id", _rideId);
            command.Parameters.AddWithValue("$wheel", _wheelId);
            command.ExecuteNonQuery();
        }

        // The totals, once, while the ride is fresh and this thread already owns the file. Doing it
        // here is the whole reason the list screen can be a query — see Schema v3.
        var connection = Connection();
        if (RideTotalsWriter.Compute(connection, tx, _rideId) is { } totals)
        {
            RideTotalsWriter.Store(connection, tx, _rideId, totals);
        }

        LogRideFinished(_rideId, RowsWritten);
        Interlocked.Exchange(ref _rideId, 0);
    }

    private void InsertTelemetry(Row row, SqliteTransaction tx)
    {
        var s = row.Snapshot;
        var command = _insertTelemetry ??= PrepareTelemetryInsert();
        command.Transaction = tx;

        command.Parameters["$wheel"].Value = _wheelId;
        command.Parameters["$at"].Value = row.At.ToUnixTimeMilliseconds();
        command.Parameters["$speed"].Value = s.SpeedRaw;
        command.Parameters["$voltage"].Value = s.VoltageRaw;
        command.Parameters["$phase"].Value = s.PhaseCurrentRaw;
        command.Parameters["$current"].Value = s.CurrentRaw;
        command.Parameters["$power"].Value = s.PowerRaw;
        command.Parameters["$pwm"].Value = Hundredths.Of(s.Pwm);
        command.Parameters["$battery"].Value = s.Battery;
        command.Parameters["$distance"].Value = s.WheelDistance;
        command.Parameters["$total"].Value = s.TotalDistance;
        command.Parameters["$temp"].Value = s.TemperatureRaw;

        // A zero in these would read as a measurement. Only Gotway reports motor temperature and
        // only Veteran reports tilt, and on a graph "the protocol is silent" and "exactly zero"
        // are not the same picture.
        command.Parameters["$temp2"].Value =
            s.WheelType == WheelType.GotWay ? s.Temperature2Raw : DBNull.Value;
        command.Parameters["$tilt"].Value =
            s.WheelType == WheelType.Veteran ? Hundredths.Of(s.Angle) : DBNull.Value;

        // The alert belongs on the row where it happened. Ours is the last value seen and would
        // repeat on every row until something replaced it, so it is drained here — which makes the
        // order of the rows part of the data: it cannot be recovered from a snapshot afterwards.
        string alert = s.Alert == _lastAlert ? "" : s.Alert;
        _lastAlert = s.Alert;
        command.Parameters["$alert"].Value = alert.Length == 0 ? DBNull.Value : alert;

        // Одиннадцать величин, которых база не видела до плана 23. Каждая сообщается ровно одним
        // семейством протоколов, и у остальных её нет — не «ноль», а нет вовсе. NULL по типу
        // колеса, а не по значению: ровный ноль момента у InMotion — такое же показание, как и
        // любое другое, и отличить его от молчания Veteran'а можно только здесь.
        bool inMotion = s.WheelType is WheelType.Inmotion or WheelType.InmotionV2;
        bool inMotionV2 = s.WheelType == WheelType.InmotionV2;
        bool kingSong = s.WheelType == WheelType.KingSong;

        command.Parameters["$torque"].Value = inMotionV2 ? Hundredths.Of(s.Torque) : DBNull.Value;
        command.Parameters["$motorPower"].Value = inMotionV2 ? Hundredths.Of(s.MotorPower) : DBNull.Value;
        command.Parameters["$cpuTemp"].Value = inMotionV2 ? s.CpuTemp : DBNull.Value;
        command.Parameters["$currentLimit"].Value = inMotionV2 ? Hundredths.Of(s.CurrentLimit) : DBNull.Value;
        command.Parameters["$roll"].Value = inMotion ? Hundredths.Of(s.Roll) : DBNull.Value;
        command.Parameters["$imuTemp"].Value = inMotion ? s.ImuTemp : DBNull.Value;
        command.Parameters["$cpuLoad"].Value = kingSong ? s.CpuLoad : DBNull.Value;
        command.Parameters["$speedLimit"].Value = kingSong ? Hundredths.Of(s.SpeedLimit) : DBNull.Value;
        command.Parameters["$fanStatus"].Value = kingSong ? s.FanStatus : DBNull.Value;
        command.Parameters["$hwPwm"].Value = kingSong ? s.OutputRaw : DBNull.Value;
        // Режим — свободная строка, и пустая у нас значит «колесо не назвалось», а не «режим пуст».
        command.Parameters["$mode"].Value = s.ModeStr.Length == 0 ? DBNull.Value : s.ModeStr;

        command.ExecuteNonQuery();
    }

    private SqliteCommand PrepareTelemetryInsert()
    {
        var command = Connection().CreateCommand();
        command.CommandText =
            """
            INSERT INTO telemetry (
                wheel_id, at, speed, voltage, phase_current, current, power, pwm,
                battery_level, distance, totaldistance, system_temp, temp2, tilt, alert,
                torque, motor_power, cpu_temp, current_limit, roll, imu_temp,
                cpu_load, speed_limit, mode, fan_status, hw_pwm)
            VALUES (
                $wheel, $at, $speed, $voltage, $phase, $current, $power, $pwm,
                $battery, $distance, $total, $temp, $temp2, $tilt, $alert,
                $torque, $motorPower, $cpuTemp, $currentLimit, $roll, $imuTemp,
                $cpuLoad, $speedLimit, $mode, $fanStatus, $hwPwm);
            """;
        // Added valueless: two of these carry text and the rest integers, and a type pinned here
        // would be a conversion waiting to happen.
        foreach (string name in TelemetryParameters) command.Parameters.AddWithValue(name, DBNull.Value);
        return command;
    }

    private static readonly string[] TelemetryParameters =
    [
        "$wheel", "$at", "$speed", "$voltage", "$phase", "$current", "$power", "$pwm",
        "$battery", "$distance", "$total", "$temp", "$temp2", "$tilt", "$alert",
        "$torque", "$motorPower", "$cpuTemp", "$currentLimit", "$roll", "$imuTemp",
        "$cpuLoad", "$speedLimit", "$mode", "$fanStatus", "$hwPwm",
    ];

    /// <summary>
    /// Charging, the wheel's own alarm flag and the battery packs change on their own schedule, not
    /// on the frame's, so they get a row a minute rather than five a second. But a minute alone
    /// would miss the moment a charger was plugged in, so a change writes one immediately: the
    /// difference between a slow value and a rare one is that the second cannot be sampled.
    /// <para>
    /// Which of the two a field is has to be decided per field, not per table. Charging status and
    /// the alarm flag are genuinely rare — they sit still for minutes and then step. Cell voltages
    /// are not: they drift with every change in load, and treating them as events produced 106 rows
    /// in a minute and a half of replay where four were meant. See <see cref="WritePack"/>.
    /// </para>
    /// </summary>
    private void WriteSlowTables(Row row, SqliteTransaction tx)
    {
        var state = (row.Snapshot.ChargingStatus, row.Snapshot.WheelAlarm);
        if (_lastWheelState != state || row.At - _lastWheelStateAt >= _options.StateInterval)
        {
            using var command = Command(
                "INSERT INTO wheel_state (wheel_id, at, charging_status, wheel_alarm) VALUES ($wheel, $at, $c, $a);", tx);
            command.Parameters.AddWithValue("$wheel", _wheelId);
            command.Parameters.AddWithValue("$at", row.At.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$c", state.ChargingStatus);
            command.Parameters.AddWithValue("$a", state.WheelAlarm ? 1 : 0);
            command.ExecuteNonQuery();

            _lastWheelState = state;
            _lastWheelStateAt = row.At;
        }

        WritePack(1, row.Snapshot.Bms1, row, tx);
        WritePack(2, row.Snapshot.Bms2, row, tx);
    }

    /// <summary>
    /// A pack is sampled, not watched for changes. Its cell voltages move with every change in
    /// load, so "write whenever something differs" means "write every frame" — which is what the
    /// first replay showed. The one genuinely discrete thing a pack reports is its health, and that
    /// is what earns an out-of-turn row; the spread is read off the clock like a thermometer.
    /// </summary>
    private void WritePack(int packNo, SmartBms bms, Row row, SqliteTransaction tx)
    {
        // No pack, or one that has not reported yet. MTen3 never will — it has no BMS at all, only
        // a total voltage, and that already lives in telemetry. No rows appear for such a wheel.
        if (PackSample.From(bms) is not { } sample) return;

        int slot = packNo - 1;
        bool healthChanged = _lastPacks[slot]?.Health != sample.Health;
        if (!healthChanged && _lastPacks[slot] is not null && row.At - _lastPackAt[slot] < _options.StateInterval) return;

        using var command = Command(
            """
            INSERT INTO pack_state (wheel_id, at, pack_no, cell_min, cell_max, cell_avg,
                                    temp_min, temp_max, temp_avg, health, current)
            VALUES ($wheel, $at, $no, $cmin, $cmax, $cavg, $tmin, $tmax, $tavg, $health, $current);
            """, tx);
        command.Parameters.AddWithValue("$wheel", _wheelId);
        command.Parameters.AddWithValue("$at", row.At.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$no", packNo);
        command.Parameters.AddWithValue("$cmin", sample.CellMin);
        command.Parameters.AddWithValue("$cmax", sample.CellMax);
        command.Parameters.AddWithValue("$cavg", sample.CellAvg);
        command.Parameters.AddWithValue("$tmin", (object?)sample.TempMin ?? DBNull.Value);
        command.Parameters.AddWithValue("$tmax", (object?)sample.TempMax ?? DBNull.Value);
        command.Parameters.AddWithValue("$tavg", (object?)sample.TempAvg ?? DBNull.Value);
        // No decoder of ours produces a health figure yet; a zero would claim a dead pack.
        command.Parameters.AddWithValue("$health", sample.Health is 0 ? DBNull.Value : sample.Health);
        command.Parameters.AddWithValue("$current", (object?)sample.Current ?? DBNull.Value);
        command.ExecuteNonQuery();

        _lastPacks[slot] = sample;
        _lastPackAt[slot] = row.At;
    }

    private SqliteConnection Connection() => _connection ??= _database.Connect();

    private SqliteCommand Command(string sql, SqliteTransaction? tx)
    {
        var command = Connection().CreateCommand();
        command.CommandText = sql;
        command.Transaction = tx;
        return command;
    }

    private abstract record Work
    {
        /// <summary>Signalled after the commit, for the commands whose caller waits on "on disk".</summary>
        public TaskCompletionSource? Done { get; init; }
    }

    private sealed record Row(string Mac, string Protocol, DateTimeOffset At, TelemetrySnapshot Snapshot) : Work;
    private sealed record MarkRide : Work;
    private sealed record CloseRide : Work;
    private sealed record DeleteRide(long RideId) : Work;
    private sealed record Barrier : Work;

    /// <summary>
    /// What is worth keeping about a pack once a minute: the spread, not a reading. One cell out of
    /// dozens sampled once a minute says nothing; minimum against maximum is the pack's condition.
    /// </summary>
    private readonly record struct PackSample(
        long CellMin, long CellMax, long CellAvg,
        long? TempMin, long? TempMax, long? TempAvg,
        int Health, long? Current)
    {
        public static PackSample? From(SmartBms bms)
        {
            if (bms.Voltage <= 0 && bms.MaxCell <= 0) return null;

            // An unpopulated sensor reads exactly zero, so zeroes are dropped. A sensor genuinely
            // sitting at 0.00 °C is dropped with them — out of six that costs an average a little
            // accuracy on a cold morning, and a phantom 0 would cost the minimum everything.
            double[] temps = [bms.Temp1, bms.Temp2, bms.Temp3, bms.Temp4, bms.Temp5, bms.Temp6];
            double[] live = [.. temps.Where(t => t != 0.0)];

            return new PackSample(
                Hundredths.Thousandths(bms.MinCell),
                Hundredths.Thousandths(bms.MaxCell),
                Hundredths.Thousandths(bms.AvgCell),
                live.Length == 0 ? null : Hundredths.Of(live.Min()),
                live.Length == 0 ? null : Hundredths.Of(live.Max()),
                live.Length == 0 ? null : Hundredths.Of(live.Average()),
                bms.Health,
                // Ровный ноль здесь — «пакет не сказал»: BMS, которая отдаёт ток, отдаёт его и на
                // стоянке, но там он колеблется около нуля, а не стоит на нём.
                bms.Current == 0.0 ? null : Hundredths.Of(bms.Current));
        }
    }

    [LoggerMessage(EventId = 1610, EventName = "Ride.DbStarted", Level = LogLevel.Information,
        Message = "Ride.DbStarted #{RideId} {Mac}")]
    private partial void LogRideStarted(long rideId, string mac);

    [LoggerMessage(EventId = 1616, EventName = "Ride.DbResumed", Level = LogLevel.Information,
        Message = "Ride.DbResumed #{RideId} {Mac} — the ride the app did not survive carries on")]
    private partial void LogRideResumed(long rideId, string mac);

    [LoggerMessage(EventId = 1611, EventName = "Ride.DbFinished", Level = LogLevel.Information,
        Message = "Ride.DbFinished #{RideId} {Rows} rows")]
    private partial void LogRideFinished(long rideId, int rows);

    [LoggerMessage(EventId = 1614, EventName = "Ride.DbDeleted", Level = LogLevel.Information,
        Message = "Ride.DbDeleted #{RideId}")]
    private partial void LogRideDeleted(long rideId);

    [LoggerMessage(EventId = 1615, EventName = "Ride.DbDeleteRefused", Level = LogLevel.Warning,
        Message = "Ride.DbDeleteRefused #{RideId} — it is the ride being recorded")]
    private partial void LogDeleteRefused(long rideId);

    [LoggerMessage(EventId = 1612, EventName = "Ride.DbWriteFailed", Level = LogLevel.Error,
        Message = "Ride.DbWriteFailed — batch dropped, recording continues")]
    private partial void LogWriteFailed(Exception ex);

    [LoggerMessage(EventId = 1613, EventName = "Ride.DbShutdownStuck", Level = LogLevel.Warning,
        Message = "Ride.DbShutdownStuck — the writer did not finish in time")]
    private partial void LogShutdownStuck(Exception ex);
}
