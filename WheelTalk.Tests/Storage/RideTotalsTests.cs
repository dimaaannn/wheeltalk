using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Playback;
using WheelTalk.Storage;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Storage;

/// <summary>
/// The figures a ride is remembered by. Every one of them is a decision about dirty data — an
/// odometer that starts at nothing, a wheel standing at a light, a night on the charger — so every
/// test here is one of those situations rather than a formula rewritten in C#.
/// <para>
/// Rows are a second apart, where a real ride has five a second. That is so the arithmetic can be
/// read off the page; it also makes the one rounding in this code visible — a sample holds until
/// the next one arrives, so a stretch of riding is counted one row longer than it lasted. At 200 ms
/// that is invisible and at one second it is a tenth of these rides.
/// </para>
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class RideTotalsTests
{
    private const string Mac = "88:25:83:F5:75:4A";

    private static readonly DateTimeOffset Start =
        new(2026, 7, 28, 20, 5, 0, TimeSpan.FromHours(3));

    /// <summary>36 km/h in the hundredths the wheel sends.</summary>
    private const long Riding = 3600;

    /// <summary>
    /// The odometer is silent for the first frames on some protocols, and a zero there is not a
    /// reading — subtracting it would make this ride as long as the wheel's whole life. Taken from
    /// the original, where it is the same two lines.
    /// </summary>
    [Fact]
    public async Task Distance_starts_from_the_first_odometer_reading_that_says_anything()
    {
        var totals = await Record([
            (Riding, 0),          // колесо ещё не сказало, сколько проехало всего
            (Riding, 0),
            (Riding, 12_000),
            (Riding, 12_010),
            (Riding, 12_020),
        ]);

        // Без правила о первом ненулевом здесь было бы 12 020 метров — весь пробег колеса за жизнь.
        Assert.Equal(20, totals.DistanceMetres);
    }

    /// <summary>
    /// The gap is not only a start-of-ride thing: every automatic reconnect hands the decoder a
    /// fresh <c>WheelState</c> (<c>WheelSession.BuildService</c>), so a ride that survives a drop —
    /// "продолжение записи после обрыва в тот же файл" — reopens the same zero gap in the middle of
    /// the same file. <c>firstOdometer</c> is set once and never revisited, so a second placeholder
    /// later in the ride must be ignored rather than mistaken for a fresh start.
    /// </summary>
    [Fact]
    public async Task A_reconnect_mid_ride_does_not_reopen_the_zero_gap()
    {
        var totals = await Record([
            (Riding, 12_000), (Riding, 12_010), (Riding, 12_020),  // до обрыва
            (Riding, 0), (Riding, 0),                              // связь оборвалась, WheelState свежий
            (Riding, 12_030), (Riding, 12_040),                    // колесо снова назвало одометр
        ]);

        // Без защиты «однажды выставлено — не трогать» второй ноль читался бы как новый старт, и
        // пробег обрезался бы до 12 040 − 12 030 = 10 метров вместо честных 40.
        Assert.Equal(40, totals.DistanceMetres);
    }

    /// <summary>
    /// One garbled frame at the end must not become the whole ride. Ноль в последней строке — не
    /// показание, а заглушка, и приращений он не даёт ни до, ни после.
    /// </summary>
    [Fact]
    public async Task A_broken_last_row_does_not_take_the_distance_with_it()
    {
        var totals = await Record([
            (Riding, 12_000), (Riding, 12_010), (Riding, 12_020), (Riding, 12_030), (Riding, 12_040),
            (Riding, 12_050), (Riding, 12_060), (Riding, 12_070), (Riding, 12_080), (Riding, 0),
        ]);

        Assert.Equal(80, totals.DistanceMetres);
    }

    /// <summary>
    /// <b>Синтетика.</b> Колесо перезагрузили посреди поездки, и одометр пошёл с нуля. Разностью
    /// концов такая поездка выходит в 30 метров вместо 70: конец меньше начала, и <c>Math.Max</c>
    /// прежней формулы обрезал бы её до нуля. Сумма приращений теряет ровно один шаг — тот, где
    /// счётчик прыгнул вниз.
    /// </summary>
    [Fact]
    public async Task A_wheel_restarted_mid_ride_keeps_the_kilometres_it_had_already_done()
    {
        var totals = await Record([
            (Riding, 12_000), (Riding, 12_010), (Riding, 12_020), (Riding, 12_040),  // 40 м до перезагрузки
            (Riding, 10), (Riding, 20), (Riding, 30), (Riding, 40),                   // счётчик начался заново
        ]);

        Assert.Equal(70, totals.DistanceMetres);
    }

    /// <summary>
    /// <b>Синтетика.</b> Переполнение 16-битного счётчика — каждые 65,5 км. Ни один наш декодер
    /// сегодня не отдаёт одометр уже 32 бит (<c>GotwayDecoder.DecodeFrameB</c>,
    /// <c>VeteranDecoder</c>, <c>KingsongDecoder.DecodeLiveData</c>, обе ветки InMotion), так что
    /// замок стоит на будущее — и на то, что формула обязана переживать любой прыжок вниз, а не
    /// только знакомый.
    /// </summary>
    [Fact]
    public async Task A_sixteen_bit_odometer_rolling_over_does_not_wipe_the_ride()
    {
        var totals = await Record([
            (Riding, 65_500), (Riding, 65_510), (Riding, 65_530),  // 30 м до края 16 бит
            (Riding, 5), (Riding, 15), (Riding, 45),               // 65 535 позади, счёт пошёл с нуля
        ]);

        Assert.Equal(70, totals.DistanceMetres);
    }

    /// <summary>
    /// <b>Синтетика.</b> Битый кадр с <b>завышенным</b> одометром — то, чего прежняя защита не
    /// ловила вовсе: «максимум по последним десяти строкам» такое показание не отбрасывал, а
    /// выбирал, и поездка выходила длиной в мусор. Шаг больше того, что колесо могло проехать за
    /// прошедшее время, не засчитывается, а счёт продолжается с того, что колесо говорит дальше.
    /// </summary>
    [Fact]
    public async Task A_garbled_frame_with_a_huge_odometer_is_not_a_ride()
    {
        var totals = await Record([
            (Riding, 12_000), (Riding, 12_010), (Riding, 12_020),
            (Riding, 900_000_000),                                  // мусор в четырёх байтах
            (Riding, 12_030), (Riding, 12_040),
        ]);

        // Двадцать метров до мусора и десять после: сам прыжок и возврат с него не в счёт.
        Assert.Equal(30, totals.DistanceMetres);
    }

    /// <summary>
    /// Секунда простоя между показаниями — это запас пути, а не повод отсечь шаг. Одометр тикает
    /// реже кадров (у InMotion шаг десять метров и полтора опроса в секунду), и точка отсчёта по
    /// времени держится за последним <b>изменением</b> счётчика, а не за последней строкой.
    /// </summary>
    [Fact]
    public async Task An_odometer_that_ticks_slower_than_the_frames_is_not_clipped()
    {
        var totals = await Record([
            (Riding, 12_000), (Riding, 12_000), (Riding, 12_000), (Riding, 12_000),
            (Riding, 12_030),
        ]);

        Assert.Equal(30, totals.DistanceMetres);
    }

    /// <summary>
    /// Two minutes at a light are two minutes of the ride and none of the riding. Without the
    /// threshold "average speed" says more about the traffic than about the wheel.
    /// </summary>
    [Fact]
    public async Task Standing_still_counts_towards_the_ride_but_not_towards_the_riding()
    {
        var samples = new List<(long Speed, long Odometer)>();
        for (int i = 0; i < 5; i++) samples.Add((Riding, 12_000 + i * 10));
        for (int i = 0; i < 5; i++) samples.Add((0, 12_040));
        for (int i = 0; i < 5; i++) samples.Add((Riding, 12_040 + i * 10));

        var totals = await Record([.. samples]);

        Assert.Equal(TimeSpan.FromSeconds(14), totals.Duration);
        // Пять интервалов в первом отрезке, четыре во втором: последний отсчёт держится за собой
        // ничего, а последний едущий отсчёт перед остановкой — держится, таково правило.
        Assert.Equal(TimeSpan.FromSeconds(9), totals.Moving);
    }

    /// <summary>
    /// The point of the threshold in one number. The original averages the speed readings instead,
    /// which answers "how fast was the average packet" and depends on how often packets arrive and
    /// how long the stops were — in its own test log, 29.7 km/h for 19.5 km in fourteen hours.
    /// </summary>
    [Fact]
    public async Task The_average_speed_is_distance_over_time_moving_not_the_mean_of_the_readings()
    {
        var samples = new List<(long Speed, long Odometer)>();
        for (int i = 0; i < 10; i++) samples.Add((Riding, 12_000 + i * 10));   // 36 км/ч, 10 м в секунду
        for (int i = 0; i < 10; i++) samples.Add((0, 12_090));                 // и столько же стоя

        var totals = await Record([.. samples]);

        // 90 метров за 10 секунд, засчитанных как движение, — 32.4 км/ч. Настоящие 36 км/ч не
        // выходят ровно из-за той самой секунды удержания; на 200 мс это 35.6, на реальной поездке
        // в полчаса — незаметно. Среднее по отсчётам дало бы 18 км/ч, вдвое меньше.
        Assert.Equal(32.4, totals.AverageSpeedKmh, 1);
        Assert.Equal(90, totals.DistanceMetres);
    }

    /// <summary>
    /// A night on the charger is a gap in the rows, not eleven hours of riding at no power. The
    /// original excludes such gaps too, and without that the consumption of any ride left connected
    /// overnight comes out at nothing.
    /// </summary>
    [Fact]
    public async Task A_gap_in_the_rows_is_not_time_spent_riding()
    {
        using var temp = new TempDatabase();
        await using (var store = temp.Store(temp.Open()))
        {
            store.BeginRide();

            // Десять секунд под нагрузкой в киловатт: девять интервалов по 1000 Вт·с.
            for (int i = 0; i < 10; i++)
            {
                store.Write(Mac, "Veteran", Sample(Riding, 12_000 + i * 10, power: 100_000), Start.AddSeconds(i));
            }

            // Час тишины и ещё один отсчёт: через дыру не интегрируется ничего.
            store.Write(Mac, "Veteran", Sample(0, 12_090, power: 100_000), Start.AddHours(1));
            await store.CloseRideAsync();
        }

        var totals = Totals(temp);

        Assert.Equal(9 * 1000.0 / 3600.0, totals.ConsumptionWh, 2);
        Assert.Equal(TimeSpan.FromSeconds(9), totals.Moving);
        // Полное время — честное: дыра была, и она часть того, сколько это заняло.
        Assert.Equal(TimeSpan.FromHours(1), totals.Duration);
    }

    /// <summary>
    /// Braking hard is a peak. A signed maximum reports the moment of heaviest regeneration as the
    /// quietest one — the same reason the alerts look at the absolute value.
    /// </summary>
    [Fact]
    public async Task The_peaks_are_by_magnitude_so_regeneration_cannot_hide_one()
    {
        var totals = await Record([
            (Riding, 12_000),
            (Riding, 12_010),
        ], power: [5_000, -800_000], current: [100, -6_000]);

        Assert.Equal(8000.0, totals.MaxPowerW, 1);
        Assert.Equal(60.0, totals.MaxCurrentA, 1);
    }

    /// <summary>
    /// Rides recorded before the totals existed, and rides the phone did not survive, are filled in
    /// at the next open — which is also the way back if a formula here turns out wrong: clear the
    /// column and the rows it is computed from are all still there.
    /// </summary>
    [Fact]
    public async Task Totals_missing_from_a_ride_are_worked_out_at_the_next_open()
    {
        using var temp = new TempDatabase();
        await using (var store = temp.Store(temp.Open()))
        {
            store.BeginRide();
            for (int i = 0; i < 5; i++)
            {
                store.Write(Mac, "Veteran", Sample(Riding, 12_000 + i * 10), Start.AddSeconds(i));
            }

            await store.CloseRideAsync();
        }

        temp.Execute("UPDATE ride SET duration_s = NULL, distance_m = NULL;");
        temp.Open();

        Assert.Equal(40L, temp.Scalar("SELECT distance_m FROM ride;"));
        Assert.Equal(4L, temp.Scalar("SELECT duration_s FROM ride;"));
    }

    /// <summary>
    /// A wheel that never reported an odometer leaves nothing to divide by, and that has to read as
    /// "no figure" rather than as a ride of no consumption.
    /// </summary>
    [Fact]
    public async Task Consumption_per_kilometre_is_nothing_at_all_when_there_are_no_kilometres()
    {
        var totals = await Record([(0, 0), (0, 0)]);

        Assert.Equal(0, totals.DistanceMetres);
        Assert.Equal(0, totals.AverageSpeedKmh);
        Assert.Null(totals.ConsumptionWhPerKm);
    }

    /// <summary>
    /// <b>Живая запись</b>, Sherman L 28.07.2026 — две минуты кадров, как их прислало колесо. На
    /// чистой записи сумма приращений обязана сойтись с честной разностью концов: новая формула
    /// отличается от прежней только там, где счётчик пошёл вниз, а здесь он не пошёл. Замок ловит
    /// обратное — потолок шага, отсекающий что-то на настоящих данных.
    /// </summary>
    [Fact]
    public async Task A_real_ride_adds_up_to_exactly_what_its_odometer_says()
    {
        var snapshots = await RealShermanRide();
        long fromWheel =
            snapshots[^1].TotalDistance - snapshots.First(s => s.TotalDistance > 0).TotalDistance;
        Assert.True(fromWheel > 0, $"одометр обязан был сдвинуться, а сдвинулся на {fromWheel} м");

        using var temp = new TempDatabase();
        await using (var store = temp.Store(temp.Open()))
        {
            store.BeginRide();
            for (int i = 0; i < snapshots.Count; i++)
            {
                store.Write(Mac, "Veteran", snapshots[i], Start.AddMilliseconds(200 * i));
            }

            await store.CloseRideAsync();
        }

        Assert.Equal(fromWheel, Totals(temp).DistanceMetres);
    }

    private static async Task<List<TelemetrySnapshot>> RealShermanRide()
    {
        var harness = DecoderHarness.ForVeteran(config => config.GotwayNegative = "0");
        var snapshots = new List<TelemetrySnapshot>();

        string fixture = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "shermanl_raw_ride_20260728.csv");
        var transport = new ReplayTransport(
            () => new StreamReader(fixture), TimeProvider.System, NullLogger<ReplayTransport>.Instance);
        transport.DataReceived += frame =>
        {
            harness.Decoder.Feed(frame);
            var snapshot = harness.Snapshot();
            if (snapshot.VoltageRaw != 0) snapshots.Add(snapshot);
        };
        await transport.PlayAsync(realtime: false);

        return snapshots;
    }

    private static async Task<RideTotals> Record(
        (long Speed, long Odometer)[] samples,
        long[]? power = null,
        long[]? current = null)
    {
        using var temp = new TempDatabase();
        await using (var store = temp.Store(temp.Open()))
        {
            store.BeginRide();
            for (int i = 0; i < samples.Length; i++)
            {
                var snapshot = Sample(
                    samples[i].Speed, samples[i].Odometer,
                    power?[i] ?? 0, current?[i] ?? 0);
                store.Write(Mac, "Veteran", snapshot, Start.AddSeconds(i));
            }

            await store.CloseRideAsync();
        }

        return Totals(temp);
    }

    /// <summary>Read back the way the list screen reads it, not out of the columns by hand.</summary>
    private static RideTotals Totals(TempDatabase temp) =>
        new RideExporter(temp.Open()).Rides().Single().Totals
            ?? throw new InvalidOperationException("A closed ride must have totals.");

    private static TelemetrySnapshot Sample(
        long speed, long odometer, long power = 0, long current = 0) =>
        new()
        {
            SpeedRaw = (int)speed,
            TotalDistance = odometer,
            PowerRaw = (int)power,
            CurrentRaw = (int)current,
            VoltageRaw = 8400,
            WheelType = WheelType.Veteran,
        };
}
