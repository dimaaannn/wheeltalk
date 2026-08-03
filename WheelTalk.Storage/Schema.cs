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
    public const int Version = 6;

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

        // v6 — поток телеметрии отделяется от поездки (план 23 §5, решения владельца 03.08.2026).
        //
        // Поток принадлежит колесу, а не поездке: `ride_id` из него уходит совсем, и строки поездки
        // находятся диапазоном `wheel_id = ? AND at BETWEEN началом и концом`. Хранимая копия связи
        // разошлась бы с границами при первой же их правке, а главное — без поездки потока раньше
        // не существовало вовсе, и графику было не из чего строиться, пока человек не нажал кнопку.
        //
        // Медленные таблицы уходят от поездки по той же причине, но с собственной: план 9 §3 просит
        // режим, в котором при подключённом колесе пишется `wheel_state` раз в минуту БЕЗ поездки —
        // кривая «напряжение покоя ↔ заряд» строится именно на стоянке. С `ride_id NOT NULL` такой
        // строки не написать.
        //
        // Время — везде unix ms. Текст сравнивать с числом нельзя, а связь поездки с потоком — это
        // именно сравнение; заодно уходят двадцать пять байт на строку при пяти строках в секунду.
        // `utc_offset_minutes` остаётся при поездке и служит только показу.
        //
        // Старые строки потока не переносятся — решение владельца: телеметрия живёт сутки, и всё,
        // что лежало в файле, старше этого срока по определению. Поездки и их итоги остаются: они
        // единственная вечная память, и после чистки от покатушки только они и остаются.
        //
        // Одиннадцать величин, которых база не видела (§1). Сотые, как и всё вокруг, кроме тех,
        // что колесо и так сообщает целыми (`cpu_temp`, `imu_temp`, `cpu_load`, `fan_status`) и
        // `hw_pwm` — он приходит уже умноженным на сто. NULL в любой величине значит «колесо
        // молчит», и это несущий смысл, а не пропуск: ноль на графике читается как показание.
        """
        DROP TABLE telemetry;
        DROP TABLE wheel_state;
        DROP TABLE pack_state;

        CREATE TABLE ride_v6 (
            id                 INTEGER PRIMARY KEY,
            wheel_id           INTEGER NOT NULL REFERENCES wheel(id),
            started_at         INTEGER NOT NULL,
            ended_at           INTEGER,
            utc_offset_minutes INTEGER NOT NULL,
            model              TEXT,
            version            TEXT,
            distance_m         INTEGER,
            duration_s         INTEGER,
            moving_s           INTEGER,
            avg_speed          INTEGER,
            max_speed          INTEGER,
            max_pwm            INTEGER,
            max_power          INTEGER,
            max_current        INTEGER,
            consumption_wh     INTEGER
        );

        INSERT INTO ride_v6
        SELECT id, wheel_id,
               CAST(strftime('%s', started_at) AS INTEGER) * 1000
                   + CAST(substr(started_at, 21, 3) AS INTEGER),
               CAST(strftime('%s', ended_at) AS INTEGER) * 1000
                   + CAST(substr(ended_at, 21, 3) AS INTEGER),
               utc_offset_minutes, model, version,
               distance_m, duration_s, moving_s, avg_speed,
               max_speed, max_pwm, max_power, max_current, consumption_wh
          FROM ride;

        DROP TABLE ride;
        ALTER TABLE ride_v6 RENAME TO ride;

        CREATE TABLE telemetry (
            wheel_id        INTEGER NOT NULL REFERENCES wheel(id),
            at              INTEGER NOT NULL,

            speed           INTEGER,
            voltage         INTEGER,
            phase_current   INTEGER,
            current         INTEGER,
            power           INTEGER,
            pwm             INTEGER,
            battery_level   INTEGER,
            distance        INTEGER,
            totaldistance   INTEGER,
            system_temp     INTEGER,
            temp2           INTEGER,
            tilt            INTEGER,
            alert           TEXT,

            torque          INTEGER,
            motor_power     INTEGER,
            cpu_temp        INTEGER,
            current_limit   INTEGER,
            roll            INTEGER,
            imu_temp        INTEGER,
            cpu_load        INTEGER,
            speed_limit     INTEGER,
            mode            TEXT,
            fan_status      INTEGER,
            hw_pwm          INTEGER
        );

        CREATE TABLE wheel_state (
            wheel_id        INTEGER NOT NULL REFERENCES wheel(id),
            at              INTEGER NOT NULL,
            charging_status INTEGER NOT NULL,
            wheel_alarm     INTEGER NOT NULL
        );

        CREATE TABLE pack_state (
            wheel_id  INTEGER NOT NULL REFERENCES wheel(id),
            at        INTEGER NOT NULL,
            pack_no   INTEGER NOT NULL,
            cell_min  INTEGER, cell_max INTEGER, cell_avg INTEGER,
            temp_min  INTEGER, temp_max INTEGER, temp_avg INTEGER,
            health    INTEGER, current  INTEGER
        );

        CREATE INDEX telemetry_by_wheel   ON telemetry(wheel_id, at);
        CREATE INDEX wheel_state_by_wheel ON wheel_state(wheel_id, at);
        CREATE INDEX pack_state_by_wheel  ON pack_state(wheel_id, at);
        """,
    ];
}
