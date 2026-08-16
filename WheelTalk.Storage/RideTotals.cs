using Microsoft.Data.Sqlite;

namespace WheelTalk.Storage;

/// <summary>
/// What a ride adds up to. Worked out once, when the ride ends, and kept on the ride — see
/// <see cref="Schema"/> v3 for why it is stored rather than recomputed, and plan 8 §3.1 for where
/// each of these figures comes from.
/// </summary>
/// <param name="DistanceMetres">
/// Сумма положительных приращений одометра за поездку, а не разность его концов — почему именно
/// так, сказано в <c>RideTotalsWriter.Compute</c>.
/// </param>
/// <param name="Duration">Wall clock from the first row to the last, stops included.</param>
/// <param name="Moving">Only the time above <see cref="RidingSpeedKmh"/>. What "average speed" is over.</param>
public sealed record RideTotals(
    long DistanceMetres,
    TimeSpan Duration,
    TimeSpan Moving,
    double AverageSpeedKmh,
    double MaxSpeedKmh,
    double MaxPwm,
    double MaxPowerW,
    double MaxCurrentA,
    double ConsumptionWh)
{
    public double DistanceKm => DistanceMetres / 1000.0;

    /// <summary>Null rather than zero when there is no distance to divide by: nothing per no kilometres.</summary>
    public double? ConsumptionWhPerKm =>
        DistanceMetres > 0 ? ConsumptionWh * 1000.0 / DistanceMetres : null;

    /// <summary>
    /// Below this a wheel is standing under someone, not moving them — the original's
    /// <c>RIDING_SPEED</c>, 2 km/h. Without a threshold "average speed" turns into a function of how
    /// many traffic lights there were.
    /// </summary>
    public const double RidingSpeedKmh = 2.0;

    /// <summary>
    /// Longer than this between two rows and the wheel was not being ridden — it was disconnected,
    /// or the phone was asleep, or it was a night on the charger. The original excludes such gaps
    /// too (<c>timeToExclude</c>); without that a ride charged overnight reports its energy spread
    /// over ten hours and its consumption comes out at nothing.
    /// </summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Computing the totals and putting them where the list can read them. One pass over the ride's
/// rows in the order they were written, because two of the figures — time spent moving and energy —
/// are sums over the intervals between rows and cannot be had from aggregates over the rows alone.
/// <para>
/// One rule holds the pass together: <b>a sample holds until the next one arrives.</b> The speed on
/// a row is the speed for the interval that follows it, and so is the power. Any other reading has
/// to say what happened in between, and the wheel did not say.
/// </para>
/// </summary>
internal static class RideTotalsWriter
{
    /// <summary>
    /// Быстрее этого колесо не едет — 150 км/ч, в полтора раза сверх самого быстрого серийного.
    /// Ею ограничивается одно приращение одометра: больше этого за прошедшее время колесо проехать
    /// не могло, значит показание битое.
    /// </summary>
    private const double ImpossibleSpeedMetresPerSecond = 150.0 / 3.6;

    /// <summary>
    /// Скидка на короткий промежуток: между двумя показаниями может пройти доля секунды, и голая
    /// «скорость × время» отсекла бы честный шаг счётчика. Сто метров — заведомо больше любого шага
    /// (самый крупный у InMotion, десять метров) и заведомо меньше битого показания, которое
    /// промахивается на километры.
    /// </summary>
    private const long OdometerStepAllowanceMetres = 100;

    /// <summary>
    /// Итоги поездки, или <c>null</c>, если считать не из чего — ни одной строки в её окне.
    /// <para>
    /// Различие несущее (план 23 §5.5). До плана <c>NULL</c> в колонках итогов значил «ещё не
    /// посчитано, досчитаем позже»; теперь он мог бы значить и «кадров уже нет, восстановить
    /// нечем» — два смысла у одного признака. Разведено тем, что первый смысл перестал
    /// существовать: досчёт идёт при каждом открытии базы и раньше всякого чтения, поэтому
    /// «посчитаем позже» не доживает до экрана. После досчёта пустые итоги при закрытой поездке
    /// значат ровно одно — подробностей больше нет, остались только эти девять чисел, и тех нет.
    /// Ноли писать нельзя: поездка на ноль метров и поездка, чьи кадры вычистили, — разные вещи.
    /// </para>
    /// </summary>
    public static RideTotals? Compute(SqliteConnection connection, SqliteTransaction? tx, long rideId)
    {
        if (RideWindow.Read(connection, tx, rideId) is not { } window) return null;

        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT at, speed, power, current, pwm, totaldistance
              FROM telemetry WHERE {RideWindow.Filter} ORDER BY at, rowid;
            """;
        command.Transaction = tx;
        window.Bind(command);

        DateTimeOffset first = default, last = default, previousAt = default;
        double previousSpeed = 0, previousPower = 0;
        long previousOdometer = 0, distance = 0;
        DateTimeOffset previousOdometerAt = default;
        double moving = 0, energy = 0;
        double maxSpeed = 0, maxPwm = 0, maxPower = 0, maxCurrent = 0;
        int rows = 0;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var at = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0));
            double speed = reader.GetInt64(1) / 100.0;
            double power = reader.GetInt64(2) / 100.0;
            long odometer = reader.GetInt64(5);

            if (rows == 0) first = at;

            if (rows > 0 && at - previousAt <= RideTotals.MaxGap)
            {
                double seconds = (at - previousAt).TotalSeconds;
                if (Math.Abs(previousSpeed) > RideTotals.RidingSpeedKmh) moving += seconds;
                energy += previousPower * seconds;
            }

            // By absolute value, the same rule the alerts follow: braking hard is a peak too, and a
            // signed maximum would report the regeneration as a quiet moment.
            maxSpeed = Math.Max(maxSpeed, Math.Abs(speed));
            maxPower = Math.Max(maxPower, Math.Abs(power));
            maxCurrent = Math.Max(maxCurrent, Math.Abs(reader.GetInt64(3) / 100.0));
            maxPwm = Math.Max(maxPwm, Math.Abs(reader.GetInt64(4) / 100.0));

            // Дистанция — сумма положительных приращений одометра, а не разность его концов
            // (мастер-план §14). Разность ломается всюду, где счётчик пошёл вниз посреди поездки:
            // колесо перезагрузили, 16-битный счётчик переполнился, человек на ходу включил
            // «поправку 0.875» у Begode — настройка живая, спрашивается на каждом кадре
            // (GotwayDecoder.cs:115,437), и одометр разом теряет восьмую часть. После такого
            // разность уходит в минус и обнуляет весь пробег; сумма приращений теряет ровно один
            // шаг и считает дальше.
            //
            // Ноль здесь не показание: снимок пишется на любой разобранный кадр, а одометр приходит
            // лишь в одном из них — первые строки каждого подключения несут заглушку, неотличимую
            // от честного нуля. Переподключение открывает ту же щель посреди поездки: на каждую
            // попытку заводится свежий WheelState. Лечится тут, а не при записи, как у оригинала
            // (TripParser.kt, firstTotalDistance): строка в базе и выгрузка CSV обязаны говорить
            // ровно то, что сказало колесо.
            if (odometer > 0)
            {
                if (previousOdometer > 0)
                {
                    long step = odometer - previousOdometer;

                    // Потолок шага — путь на невозможной скорости за время с прошлого показания.
                    // Он и есть замена прежнему «максимуму по последним десяти строкам»: тот ловил
                    // только битый хвост, да и то лишь заниженный — завышенное битое показание
                    // максимум, наоборот, выбирал.
                    double sinceReading = (at - previousOdometerAt).TotalSeconds;
                    long allowed = Math.Max(
                        OdometerStepAllowanceMetres,
                        (long)(sinceReading * ImpossibleSpeedMetresPerSecond));

                    if (step > 0 && step <= allowed) distance += step;
                }

                // Точка отсчёта переставляется на любое показание, а не только на зачтённое: после
                // перезагрузки колеса или битого кадра счёт обязан продолжиться с того, что колесо
                // говорит теперь. Иначе одно испорченное показание отменило бы весь остаток
                // поездки. Время же держится за последним изменением, а не за последней строкой:
                // одометр тикает реже кадров, и запас пути обязан копиться, пока счётчик стоит.
                if (odometer != previousOdometer)
                {
                    previousOdometer = odometer;
                    previousOdometerAt = at;
                }
            }

            previousAt = last = at;
            previousSpeed = speed;
            previousPower = power;
            rows++;
        }

        if (rows == 0) return null;

        return new RideTotals(
            distance,
            last - first,
            TimeSpan.FromSeconds(moving),
            // Distance over time spent moving — not the mean of the speed readings. That mean is
            // what the original reports, and it answers a different question: how fast the wheel was
            // going in the average packet, which depends on how often packets arrive and how long
            // the stops were. Their own test log reads 29.7 km/h for 19.5 km in 880 minutes.
            moving > 0 ? distance / moving * 3.6 : 0,
            maxSpeed,
            maxPwm,
            maxPower,
            maxCurrent,
            energy / 3600.0);
    }

    public static void Store(SqliteConnection connection, SqliteTransaction? tx, long rideId, RideTotals totals)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE ride SET distance_m = $distance, duration_s = $duration, moving_s = $moving,
                            avg_speed = $avg, max_speed = $maxSpeed, max_pwm = $maxPwm,
                            max_power = $maxPower, max_current = $maxCurrent,
                            consumption_wh = $consumption
             WHERE id = $id;
            """;
        command.Transaction = tx;
        command.Parameters.AddWithValue("$distance", totals.DistanceMetres);
        command.Parameters.AddWithValue("$duration", (long)totals.Duration.TotalSeconds);
        command.Parameters.AddWithValue("$moving", (long)totals.Moving.TotalSeconds);
        command.Parameters.AddWithValue("$avg", Hundredths.Of(totals.AverageSpeedKmh));
        command.Parameters.AddWithValue("$maxSpeed", Hundredths.Of(totals.MaxSpeedKmh));
        command.Parameters.AddWithValue("$maxPwm", Hundredths.Of(totals.MaxPwm));
        command.Parameters.AddWithValue("$maxPower", Hundredths.Of(totals.MaxPowerW));
        command.Parameters.AddWithValue("$maxCurrent", Hundredths.Of(totals.MaxCurrentA));
        command.Parameters.AddWithValue("$consumption", Hundredths.Of(totals.ConsumptionWh));
        command.Parameters.AddWithValue("$id", rideId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Fills in every finished ride that has no totals: the ones recorded before this version
    /// existed, and the ones the crash recovery just closed. An empty <c>duration_s</c> is the only
    /// signal needed — it is also how a changed formula is rolled out, by clearing the column.
    /// <para>
    /// Runs at open, with nobody else on the file, <b>and before the purge</b>: телеметрию чистит
    /// срок хранения, и поездка, до которой досчёт не дошёл вовремя, останется без чисел навсегда
    /// (план 23 §5.5). Порядок держит <see cref="RideDatabase"/>.
    /// </para>
    /// <para>
    /// Поездка, чьи кадры уже вычищены, считается пройденной: считать нечего, и колонки остаются
    /// пустыми — это и есть её ответ. Проверяется она каждый раз заново, но это поиск по индексу,
    /// не находящий ничего.
    /// </para>
    /// </summary>
    public static int Backfill(SqliteConnection connection)
    {
        var pending = new List<long>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM ride WHERE ended_at IS NOT NULL AND duration_s IS NULL;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) pending.Add(reader.GetInt64(0));
        }

        int filled = 0;
        foreach (long id in pending)
        {
            if (Compute(connection, null, id) is not { } totals) continue;

            Store(connection, null, id, totals);
            filled++;
        }

        return filled;
    }
}
