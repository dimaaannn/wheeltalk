using Microsoft.Extensions.Time.Testing;
using WheelTalk.Core.Contracts;
using WheelTalk.Storage;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Storage;

/// <summary>
/// Срок хранения потока и порядок, в котором он применяется. Решения владельца 03.08.2026, план 23
/// §5.1 п. 5–6 и §5.4: телеметрия живёт сутки, чистка сносит всё старше срока и в поездки не
/// смотрит, а поездки не чистятся вовсе.
/// <para>
/// <b>Главное здесь — не срок, а очерёдность.</b> Поездка закрывается последним кадром, итоги
/// считаются по кадрам, и оба шага обязаны пройти раньше чистки. Опередит она — закрывать поездку
/// станет нечем, а итоги не из чего считать, и потеряется это молча.
/// </para>
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class TelemetryRetentionTests
{
    private const string Mac = "88:25:83:F5:75:4A";

    private static readonly DateTimeOffset Start =
        new(2026, 7, 28, 20, 5, 0, TimeSpan.FromHours(3));

    /// <summary>Сутки — значение по умолчанию; здесь оно названо явно, чтобы тест читался целиком.</summary>
    private static StorageOptions Day() => new() { TelemetryRetention = TimeSpan.FromHours(24) };

    [Fact]
    public async Task What_is_older_than_the_term_goes_and_what_is_younger_stays()
    {
        using var temp = new TempDatabase();
        await using (var store = temp.Store(temp.Open()))
        {
            for (int i = 0; i < 5; i++) store.Write(Mac, "Veteran", Sample(), Start.AddSeconds(i));
            for (int i = 0; i < 3; i++) store.Write(Mac, "Veteran", Sample(), Start.AddHours(30).AddSeconds(i));
            await store.FlushAsync();
        }

        Assert.Equal(8, temp.Count("telemetry"));

        // Через сорок часов после первых отсчётов: старые за сроком, свежие — нет.
        temp.Open(Day(), new FakeTimeProvider(Start.AddHours(40)));

        Assert.Equal(3, temp.Count("telemetry"));
        Assert.Equal(0, temp.Count("telemetry", $"at < {Start.AddHours(16).ToUnixTimeMilliseconds()}"));
    }

    /// <summary>
    /// Гейт шага 3: суточная чистка не трогает таблицу поездок. Очистки раздельны и друг о друге не
    /// знают — удалились данные, значит удалились; поездка и её итоги остаются, и после этого от
    /// покатушки живут ровно девять чисел.
    /// </summary>
    [Fact]
    public async Task The_purge_does_not_touch_the_rides_table()
    {
        using var temp = new TempDatabase();
        await using (var store = temp.Store(temp.Open()))
        {
            store.BeginRide();
            for (int i = 0; i < 10; i++)
            {
                store.Write(Mac, "Veteran", Sample(odometer: 12_000 + i * 10), Start.AddSeconds(i));
            }

            await store.CloseRideAsync();
        }

        var database = temp.Open(Day(), new FakeTimeProvider(Start.AddDays(3)));

        Assert.Equal(0, temp.Count("telemetry"));
        Assert.Equal(1, temp.Count("ride"));

        var ride = Assert.Single(new RideExporter(database).Rides());
        Assert.Equal(0, ride.Rows);
        Assert.NotNull(ride.Totals);
        Assert.Equal(90, ride.Totals.DistanceMetres);
    }

    /// <summary>
    /// НЕСУЩЕЕ ПРАВИЛО, КОТОРОЕ ЛЕГКО ПОТЕРЯТЬ (план 23 §5.4). Приложение убили посреди покатушки и
    /// не запускали три дня. При открытии базы поездку надо закрыть последним кадром и досчитать
    /// итоги — и только потом чистить. Пройди чистка первой, поездка осталась бы без конца и без
    /// чисел навсегда, и никакой ошибки при этом не случилось бы.
    /// </summary>
    [Fact]
    public async Task A_ride_is_closed_and_totalled_before_the_purge_takes_its_rows()
    {
        using var temp = new TempDatabase();
        await using (var store = temp.Store(temp.Open()))
        {
            store.BeginRide();
            for (int i = 0; i < 10; i++)
            {
                store.Write(Mac, "Veteran", Sample(odometer: 12_000 + i * 10), Start.AddSeconds(i));
            }

            await store.FlushAsync();
        }

        // Приложение убито: закрыть поездку было некому.
        temp.Execute("UPDATE ride SET ended_at = NULL, duration_s = NULL, distance_m = NULL;");

        var database = temp.Open(Day(), new FakeTimeProvider(Start.AddDays(3)));

        Assert.Equal(0, temp.Count("telemetry"));

        var ride = Assert.Single(new RideExporter(database).Rides());
        Assert.Equal(Start.AddSeconds(9), ride.EndedAt);
        Assert.NotNull(ride.Totals);
        Assert.Equal(90, ride.Totals.DistanceMetres);
        Assert.Equal(TimeSpan.FromSeconds(9), ride.Totals.Duration);
    }

    /// <summary>
    /// Пустые итоги при закрытой поездке значат ровно одно: подробностей больше нет. Второй смысл —
    /// «ещё не посчитано» — не доживает до экрана, потому что досчёт идёт при каждом открытии базы
    /// и раньше всякого чтения (план 23 §5.5). Ноли писать нельзя: поездка на ноль метров и
    /// поездка, чьи кадры вычистили, — разные вещи, и в списке они выглядели бы одинаково.
    /// </summary>
    [Fact]
    public async Task A_ride_whose_rows_are_gone_has_no_totals_rather_than_zeroes()
    {
        using var temp = new TempDatabase();
        await using (var store = temp.Store(temp.Open()))
        {
            store.BeginRide();
            for (int i = 0; i < 5; i++) store.Write(Mac, "Veteran", Sample(), Start.AddSeconds(i));
            await store.CloseRideAsync();
        }

        // Поездка из сборки до появления итогов, чей поток вычистили ещё в прошлый запуск: строка
        // есть, чисел нет, и восстановить их уже не из чего.
        temp.Execute("UPDATE ride SET duration_s = NULL, distance_m = NULL;");
        temp.Execute("DELETE FROM telemetry;");
        var database = temp.Open(Day(), new FakeTimeProvider(Start.AddDays(3)));

        Assert.Null(Assert.Single(new RideExporter(database).Rides()).Totals);
    }

    private static TelemetrySnapshot Sample(long odometer = 987_654) => new()
    {
        SpeedRaw = 3600,
        VoltageRaw = 15012,
        TotalDistance = odometer,
        TemperatureRaw = 3400,
        WheelType = WheelType.Veteran,
    };
}
