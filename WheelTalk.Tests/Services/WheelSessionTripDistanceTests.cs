using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Detection;
using WheelTalk.Core.Services;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Services;

/// <summary>
/// «Пробег от старта» и обрыв связи. Поездку заканчивает райдер, а не колесо, уехавшее из радиуса:
/// сессия строит новое <c>WheelState</c> на каждую попытку подключения (у оригинала <c>WheelData</c>
/// живёт всё время работы приложения), и без переноса точки отсчёта пробег обнулялся бы посреди
/// поездки на каждом автопереподключении.
/// </summary>
public class WheelSessionTripDistanceTests
{
    private const string Mac = "88:25:83:F5:75:4A";

    /// <summary>Кадр Sherman L с одометром — тот же, которым проверяется опознание протокола.</summary>
    private static readonly string[] Frame =
    [
        "dc5a5c53397afffe0aa400000df10000000a0b3d",
        "0e0e0000037a035217730064000e00b480c80000",
        "808080808080058080808080800ff30ff50ff50f",
        "f50ff50fef0ff20ff30ff30ff30ff30fed0ff30f",
        "f40ff5378c5145",
    ];

    [Fact]
    public async Task Trip_distance_survives_a_reconnect()
    {
        var (session, transport, time) = Build();

        await session.ConnectAsync(Mac);

        // Точка отсчёта берётся не с первого кадра, а с первого ненулевого одометра — второй кадр
        // и есть тот момент, когда она встаёт (WheelState.SetTotalDistance, как у оригинала).
        transport.Deliver(Frame);
        transport.Deliver(Frame);
        Assert.Equal(0, session.LastSnapshot!.DistanceFromStart);

        transport.DropLink();
        await Reconnected(session, time);

        transport.Deliver(Frame);

        // Без переноса точки отсчёта здесь был бы весь одометр колеса — «от старта» показал бы
        // тысячи километров сразу после переподключения.
        Assert.Equal(0, session.LastSnapshot!.DistanceFromStart);
    }

    /// <summary>Отключился райдер — поездка кончилась, и следующая считает пробег заново.</summary>
    [Fact]
    public async Task A_disconnect_by_the_rider_starts_the_trip_over()
    {
        var (session, transport, _) = Build();

        await session.ConnectAsync(Mac);
        transport.Deliver(Frame);
        transport.Deliver(Frame);

        await session.DisconnectAsync();
        await session.ConnectAsync(Mac);

        transport.Deliver(Frame);

        // Первый кадр новой поездки: одометр уже пришёл, а точка отсчёта ещё нет — она встанет
        // следующим кадром. Унаследованной точки здесь быть не должно.
        Assert.Equal(session.LastSnapshot!.TotalDistance, session.LastSnapshot.DistanceFromStart);
    }

    private static async Task Reconnected(WheelSession session, FakeTimeProvider time)
    {
        for (int i = 0; i < 20 && session.CurrentState != ConnectionState.Connected; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10);
        }

        Assert.Equal(ConnectionState.Connected, session.CurrentState);
    }

    private static (WheelSession Session, FakeTransport Transport, FakeTimeProvider Time) Build()
    {
        var transport = new FakeTransport();
        var time = new FakeTimeProvider();
        var session = new WheelSession(
            transport,
            new AppWheelConfig(),
            new NullEventSink(),
            time,
            new ConnectionOptions { RetryDelay = TimeSpan.FromSeconds(5) },
            new WheelDetector(NullLogger<WheelDetector>.Instance),
            NullLoggerFactory.Instance);

        return (session, transport, time);
    }
}
