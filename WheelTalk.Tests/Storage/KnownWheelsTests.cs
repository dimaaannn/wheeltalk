using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Storage;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Storage;

/// <summary>
/// Отметка «подключались вот тогда» — единственное, что делает колесо привязанным (план 24 §А).
/// Проверяется то, за что отвечает база: отметка переживает перезапуск, повторное подключение не
/// плодит строк и не трогает опознанный протокол, а забывание уносит отметку, но не поездки.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class KnownWheelsTests
{
    private const string Mac = "88:25:83:F5:75:4A";
    private const string Other = "C0:FF:EE:00:11:22";

    private static readonly DateTimeOffset Monday = new(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Friday = new(2026, 8, 7, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_wheel_connected_to_is_remembered_across_restarts()
    {
        using var temp = new TempDatabase();
        Wheels(temp).Remember(Mac, "Veteran", Monday);

        // Другое открытие базы — то же, что следующий запуск приложения.
        var remembered = Assert.Single(Wheels(temp).All());
        Assert.Equal(Mac, remembered.Mac);
        Assert.Equal(Monday, remembered.LastConnectedAt);
    }

    [Fact]
    public void Connecting_again_moves_the_stamp_instead_of_adding_a_row()
    {
        using var temp = new TempDatabase();
        var wheels = Wheels(temp);

        wheels.Remember(Mac, "Veteran", Monday);
        wheels.Remember(Mac, "Veteran", Friday);

        Assert.Equal(Friday, Assert.Single(wheels.All()).LastConnectedAt);
        Assert.Equal(1, temp.Count("wheel"));
    }

    /// <summary>
    /// Протокол на подключении известен не всегда: Veteran и Begode называет только первый кадр.
    /// Пустая строка не должна затирать уже опознанное — иначе каждое подключение стирало бы то,
    /// что записал поток телеметрии.
    /// </summary>
    [Fact]
    public void A_connection_that_does_not_know_the_protocol_yet_leaves_the_known_one_alone()
    {
        using var temp = new TempDatabase();
        temp.Open();
        temp.Execute($"INSERT INTO wheel (mac, protocol) VALUES ('{Mac}', 'Veteran');");

        Wheels(temp).Remember(Mac, "", Monday);

        Assert.Equal("Veteran", temp.Scalar($"SELECT protocol FROM wheel WHERE mac = '{Mac}';"));
    }

    /// <summary>
    /// Колесо, писавшее поездку до этой версии, привязанным не считается: строка в <c>wheel</c>
    /// заводится потоком, а отметку ставит только подключение.
    /// </summary>
    [Fact]
    public void Wheels_without_a_stamp_are_not_in_the_list_and_the_newest_is_first()
    {
        using var temp = new TempDatabase();
        temp.Open();
        temp.Execute("INSERT INTO wheel (mac, protocol) VALUES ('AA:BB:CC:DD:EE:FF', 'Gotway');");

        var wheels = Wheels(temp);
        wheels.Remember(Mac, "Veteran", Monday);
        wheels.Remember(Other, "KingSong", Friday);

        Assert.Equal([Other, Mac], wheels.All().Select(w => w.Mac));
    }

    /// <summary>
    /// Забывание снимает отметку, а не строку колеса: на неё ссылаются поездки и весь поток, и
    /// удаление строки унесло бы историю, о которой никто не просил.
    /// </summary>
    [Fact]
    public void Forgetting_a_wheel_keeps_its_rides()
    {
        using var temp = new TempDatabase();
        var wheels = Wheels(temp);
        wheels.Remember(Mac, "Veteran", Monday);
        temp.Execute(
            $"""
             INSERT INTO ride (wheel_id, started_at, utc_offset_minutes)
             VALUES ((SELECT id FROM wheel WHERE mac = '{Mac}'), 1754200000000, 180);
             """);

        wheels.Forget(Mac);

        Assert.Empty(wheels.All());
        Assert.Equal(1, temp.Count("ride"));
        Assert.Equal(1, temp.Count("wheel"));
    }

    /// <summary>Чужие колёса забывание не касается ни при каких условиях (план 24, §А3).</summary>
    [Fact]
    public void Forgetting_one_wheel_leaves_the_others_bound()
    {
        using var temp = new TempDatabase();
        var wheels = Wheels(temp);
        wheels.Remember(Mac, "Veteran", Monday);
        wheels.Remember(Other, "KingSong", Friday);

        wheels.Forget(Mac);

        Assert.Equal(Other, Assert.Single(wheels.All()).Mac);
    }

    private static KnownWheels Wheels(TempDatabase temp) =>
        new(temp.Open(), NullLogger<KnownWheels>.Instance);
}
