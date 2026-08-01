namespace WheelTalk.Storage;

/// <summary>
/// The database as it is meant to look, one constant per version. Migrations are applied in order
/// by <see cref="RideDatabase"/>; adding a version means appending to <see cref="Migrations"/> and
/// never editing what is already there — the file on the phone has recorded rides in it, and those
/// are the only data this project keeps nowhere else.
/// <para>
/// Why the columns are what they are — plan 8, §2.1. The short version: everything is an integer in
/// hundredths of a unit, because that is the resolution the wheel sends; the CSV export divides and
/// formats, so it stays a projection of this table rather than a second answer to "what is a row".
/// </para>
/// </summary>
internal static class Schema
{
    /// <summary>What this build understands. A file claiming more than this is not written to.</summary>
    public const int Version = 5;

    public static readonly string[] Migrations =
    [
        // v1 — wheels, rides, telemetry, and the two slow tables next to it.
        """
        CREATE TABLE wheel (
            id        INTEGER PRIMARY KEY,
            mac       TEXT NOT NULL UNIQUE,
            name      TEXT,
            protocol  TEXT NOT NULL
        );

        CREATE TABLE ride (
            id                 INTEGER PRIMARY KEY,
            wheel_id           INTEGER NOT NULL REFERENCES wheel(id),
            started_at         TEXT NOT NULL,
            ended_at           TEXT,
            utc_offset_minutes INTEGER NOT NULL,
            model              TEXT,
            version            TEXT
        );

        CREATE TABLE telemetry (
            ride_id         INTEGER NOT NULL REFERENCES ride(id),
            wheel_id        INTEGER NOT NULL REFERENCES wheel(id),
            at              TEXT    NOT NULL,

            speed           INTEGER NOT NULL,
            voltage         INTEGER NOT NULL,
            phase_current   INTEGER NOT NULL,
            current         INTEGER NOT NULL,
            power           INTEGER NOT NULL,
            pwm             INTEGER NOT NULL,
            battery_level   INTEGER NOT NULL,
            distance        INTEGER NOT NULL,
            totaldistance   INTEGER NOT NULL,
            system_temp     INTEGER NOT NULL,
            temp2           INTEGER,
            tilt            INTEGER,
            alert           TEXT
        );

        CREATE TABLE wheel_state (
            ride_id         INTEGER NOT NULL REFERENCES ride(id),
            at              TEXT    NOT NULL,
            charging_status INTEGER NOT NULL,
            wheel_alarm     INTEGER NOT NULL
        );

        CREATE TABLE pack_state (
            ride_id   INTEGER NOT NULL REFERENCES ride(id),
            at        TEXT    NOT NULL,
            pack_no   INTEGER NOT NULL,
            cell_min  INTEGER, cell_max INTEGER, cell_avg INTEGER,
            temp_min  INTEGER, temp_max INTEGER, temp_avg INTEGER,
            health    INTEGER
        );

        CREATE INDEX telemetry_by_ride   ON telemetry(ride_id, at);
        CREATE INDEX wheel_state_by_ride ON wheel_state(ride_id, at);
        CREATE INDEX pack_state_by_ride  ON pack_state(ride_id, at);
        """,

        // v2 — user settings, in layers. Scope is empty for the value in force everywhere and a
        // MAC for one wheel's own; "which settings has this wheel overridden" is then a query
        // rather than a walk over JSON, and a write is atomic instead of a read-merge-write.
        //
        // The MAC and not wheel.id, deliberately: a wheel can have settings long before it has a
        // ride, and settings must not be what forces a row into `wheel`. They meet on the MAC,
        // which is the wheel's identity either way.
        //
        // Values are text whatever they mean. The store holds four kinds of setting without
        // knowing about any of them, and a column that is sometimes a number and sometimes a flag
        // is a column nothing can be asked about.
        """
        CREATE TABLE setting (
            scope TEXT NOT NULL,
            key   TEXT NOT NULL,
            value TEXT NOT NULL,
            PRIMARY KEY (scope, key)
        ) WITHOUT ROWID;
        """,

        // v3 — what a ride adds up to, worked out once when it ends. The original recomputes this
        // from the whole log every time a row scrolls into view, which is what makes its trip screen
        // slow; the numbers do not change after the ride does, so they belong next to it.
        //
        // Hundredths again, and for the same reason: these come from the columns above and would
        // otherwise be a second kind of number in one table. `distance_m` and the two durations are
        // whole units — the wheel's odometer is in metres and a ride is not timed to the centisecond.
        //
        // NULL means "not worked out yet", and that is a signal rather than a gap: rides recorded
        // before this version, and rides closed by the crash recovery, are filled in at the next
        // open. It is also the way back if one of these formulas turns out wrong — clear the column
        // and it is computed again from rows that are still all there.
        //
        // Wh per km is not stored. It is `consumption_wh` over `distance_m` and nothing else, and a
        // stored copy of a quotient is one more thing that can disagree with its own numerator.
        """
        ALTER TABLE ride ADD COLUMN distance_m     INTEGER;
        ALTER TABLE ride ADD COLUMN duration_s     INTEGER;
        ALTER TABLE ride ADD COLUMN moving_s       INTEGER;
        ALTER TABLE ride ADD COLUMN avg_speed      INTEGER;
        ALTER TABLE ride ADD COLUMN max_speed      INTEGER;
        ALTER TABLE ride ADD COLUMN max_pwm        INTEGER;
        ALTER TABLE ride ADD COLUMN max_power      INTEGER;
        ALTER TABLE ride ADD COLUMN max_current    INTEGER;
        ALTER TABLE ride ADD COLUMN consumption_wh INTEGER;
        """,

        // v4 — ток пакета, как его сообщает сама BMS. Тот, что лежит в `telemetry`, — вычисленный
        // (`pwm × phase_current`, формула оригинала), и его знак означает направление движения, а не
        // направление энергии: полевая запись 28.07.2026 показала это по просадке напряжения, ровно
        // одинаковой на обоих знаках. Ток от BMS измерен, а не выведен, и это единственный способ
        // однажды проверить вычисленный, не гадая.
        //
        // NULL, а не ноль: у MTen3 BMS нет вовсе, а «ноль ампер» и «пакет не сказал» на графике
        // выглядят одинаково только до первого вопроса, что означает ровная линия по нулю.
        """
        ALTER TABLE pack_state ADD COLUMN current INTEGER;
        """,

        // v5 — колонка `wheel.name` убрана. Заводилась она в v1 под имя, которое колесу даёт
        // хозяин, но так ни разу и не записывалась: имя живёт в настройках (`Wheel:Name`), и
        // единственным владельцем оно и остаётся — решение владельца 30.07.2026, план 13 §3.1.
        // Список поездок подставляет имя при показе, а не хранит вторую копию: копия разошлась бы
        // с настройкой при первом же переименовании.
        """
        ALTER TABLE wheel DROP COLUMN name;
        """,
    ];
}
