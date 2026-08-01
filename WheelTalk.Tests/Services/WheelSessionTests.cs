using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WheelTalk.Tests.TestSupport;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Detection;
using WheelTalk.Core.Ports;
using WheelTalk.Core.Services;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Services;

/// <summary>
/// The session is what keeps a wheel connected while the app runs, so what is worth pinning down
/// is its behaviour around failure: does it keep trying, does it stop when told, and does a
/// reconnect leave exactly one decoder feeding on the transport.
/// </summary>
public class WheelSessionTests
{
    /// <summary>One complete Sherman L telemetry packet, split into frames as BLE delivers it.</summary>
    private static readonly string[] ShermanLPacket =
    [
        "dc5a5c53397afffe0aa400000df10000000a0b3d",
        "0e0e0000037a035217730064000e00b480c80000",
        "808080808080058080808080800ff30ff50ff50f",
        "f50ff50fef0ff20ff30ff30ff30ff30fed0ff30f",
        "f40ff5378c5145",
    ];

    private const string Mac = "88:25:83:F5:75:4A";

    [Fact]
    public async Task Connecting_starts_a_session_and_publishes_telemetry()
    {
        var (session, transport, _) = Build();

        await session.ConnectAsync(Mac);
        var received = new List<TelemetrySnapshot>();
        using var subscription = session.Telemetry.Subscribe(received.Add);
        transport.Deliver(ShermanLPacket);

        Assert.Equal(ConnectionState.Connected, session.CurrentState);
        Assert.Single(received);
        Assert.Equal("Sherman L", received[0].Model);
        Assert.Equal(received[0], session.LastSnapshot);
    }

    [Fact]
    public async Task A_wheel_that_does_not_answer_is_retried_until_it_does()
    {
        var (session, transport, time) = Build();
        transport.RefuseConnections = true;

        await session.ConnectAsync(Mac);
        Assert.NotEqual(ConnectionState.Connected, session.CurrentState);

        transport.RefuseConnections = false;
        await WaitForState(session, ConnectionState.Connected, time);

        Assert.True(transport.ConnectAttempts > 1, "the session must keep trying on its own");
    }

    [Fact]
    public async Task A_lost_link_is_chased_and_the_last_readings_are_kept()
    {
        var (session, transport, time) = Build();
        await session.ConnectAsync(Mac);
        transport.Deliver(ShermanLPacket);
        var lastBeforeDrop = session.LastSnapshot;

        transport.DropLink();

        Assert.Equal(ConnectionState.Reconnecting, session.CurrentState);
        Assert.Equal(lastBeforeDrop, session.LastSnapshot);

        await WaitForState(session, ConnectionState.Connected, time);
    }

    [Fact]
    public async Task Reconnecting_leaves_one_decoder_on_the_transport()
    {
        var (session, transport, time) = Build();
        await session.ConnectAsync(Mac);
        transport.DropLink();
        await WaitForState(session, ConnectionState.Connected, time);

        var received = new List<TelemetrySnapshot>();
        using var subscription = session.Telemetry.Subscribe(received.Add);
        transport.Deliver(ShermanLPacket);

        // Two decoders on one transport would decode the same bytes twice — and the stale one
        // carries the previous ride's wheel state, so the duplicate would not even agree.
        Assert.Single(received);
    }

    [Fact]
    public async Task Disconnecting_stops_the_chase()
    {
        var (session, transport, time) = Build();
        transport.RefuseConnections = true;
        await session.ConnectAsync(Mac);

        await session.DisconnectAsync();
        int attemptsAtDisconnect = transport.ConnectAttempts;
        for (int i = 0; i < 5; i++)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(20);
        }

        Assert.Equal(ConnectionState.Disconnected, session.CurrentState);
        Assert.Equal(attemptsAtDisconnect, transport.ConnectAttempts);
    }

    /// <summary>
    /// Одна погоня за колесом, сколько бы раз связь ни рвалась. Неудачная попытка сама способна
    /// поднять ConnectionLost, а тот просит погнаться снова — на выходе 28.07.2026 так набралось
    /// шесть параллельных циклов, каждый со своим GATT-клиентом к одному и тому же колесу.
    /// </summary>
    [Fact]
    public async Task Repeated_drops_do_not_multiply_the_retry_loops()
    {
        var (session, transport, time) = Build();
        await session.ConnectAsync(Mac);

        transport.RefuseConnections = true;
        for (int i = 0; i < 5; i++)
        {
            transport.DropLink();
        }

        // Циклы запускаются через Task.Run: без этой паузы они ещё не дошли до своего Task.Delay,
        // и виртуальное время просто не найдёт кого будить.
        await Task.Delay(200);
        int attemptsBefore = transport.ConnectAttempts;
        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        // Один цикл — одна попытка за интервал. Пять циклов дали бы пять.
        Assert.InRange(transport.ConnectAttempts - attemptsBefore, 0, 2);

        await session.DisconnectAsync();
    }

    /// <summary>
    /// Повторы живут только здесь, поэтому здесь же должен быть и предохранитель от долбёжки:
    /// пауза растёт с каждой неудачей. Без него выключенное колесо получает попытку каждые
    /// полсекунды, а лог — 215 строк в секунду, как на выходе 28.07.2026, где кольцевой буфер
    /// проворачивался за семь с половиной секунд и в нём нельзя было разобрать ничего другого.
    /// </summary>
    [Fact]
    public async Task The_pause_between_attempts_grows_instead_of_hammering_a_dead_wheel()
    {
        var (session, transport, time) = Build(new ConnectionOptions
        {
            FirstRetryDelay = TimeSpan.FromSeconds(1),
            RetryDelay = TimeSpan.FromSeconds(16),
        });
        transport.RefuseConnections = true;

        await session.ConnectAsync(Mac);
        int attemptsAtStart = transport.ConnectAttempts;

        // Двадцать секунд по секунде. Паузы идут 1, 2, 4, 8, 16 — в двадцать секунд помещаются
        // четыре попытки. Без роста их было бы двадцать.
        for (int second = 0; second < 20; second++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(30);
        }

        Assert.InRange(transport.ConnectAttempts - attemptsAtStart, 3, 6);

        await session.DisconnectAsync();
    }

    /// <summary>
    /// Погоня стоит радио только на прямых попытках, и их ровно две: первая — из
    /// <c>ConnectAsync</c>, пока человек ждёт у экрана, вторая — сразу после обрыва, когда прямой
    /// коннект быстрее всего чинит полуоткрытый линк. Всё дальнейшее — пассивное ожидание
    /// транспорта (<c>WaitForWheelAsync</c>, у Android это autoConnect), как у оригинала:
    /// выключенное колесо не должно стоить батареи.
    /// </summary>
    [Fact]
    public async Task The_chase_goes_passive_after_one_direct_retry()
    {
        var (session, transport, time) = Build(new ConnectionOptions
        {
            FirstRetryDelay = TimeSpan.FromSeconds(1),
            RetryDelay = TimeSpan.FromSeconds(4),
        });
        transport.RefuseConnections = true;

        await session.ConnectAsync(Mac);
        Assert.Equal(0, transport.PassiveWaits);

        for (int second = 0; second < 10; second++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(30);
        }

        Assert.True(transport.PassiveWaits >= 1);
        Assert.Equal(2, transport.ConnectAttempts - transport.PassiveWaits);

        await session.DisconnectAsync();
    }

    /// <summary>
    /// То, ради чего сторож заведён: связь может считаться живой, когда её давно нет. Проверено на
    /// телефоне 30.07.2026 — выключенное колесо оставалось «подключённым» 68 минут, потому что
    /// GATT об обрыве не сообщил, а больше об этом судить было некому.
    /// </summary>
    [Fact]
    public async Task Silence_on_a_live_link_counts_as_a_drop()
    {
        var (session, transport, time) = Build(new ConnectionOptions
        {
            DataTimeout = TimeSpan.FromSeconds(15),
            RetryDelay = TimeSpan.FromSeconds(5),
        });

        await session.ConnectAsync(Mac);
        transport.Deliver(ShermanLPacket);
        Assert.Equal(ConnectionState.Connected, session.CurrentState);

        // Четырнадцать секунд молчания — ещё не обрыв: колесо шлёт пять раз в секунду, но пачка
        // может задержаться, и рвать связь на каждой задержке значит рвать её постоянно.
        time.Advance(TimeSpan.FromSeconds(14));
        Assert.Equal(ConnectionState.Connected, session.CurrentState);

        // А вот дальше порога — обрыв, и он разыгрывается как настоящий: та же погоня, то же
        // состояние, что при разрыве от транспорта.
        time.Advance(TimeSpan.FromSeconds(2));
        await WaitForState(session, ConnectionState.Reconnecting, time);

        await session.DisconnectAsync();
    }

    /// <summary>Кадры идут — сторож молчит, сколько бы времени ни прошло.</summary>
    [Fact]
    public async Task A_wheel_that_keeps_talking_is_never_dropped()
    {
        var (session, transport, time) = Build(new ConnectionOptions { DataTimeout = TimeSpan.FromSeconds(15) });

        await session.ConnectAsync(Mac);

        for (int i = 0; i < 10; i++)
        {
            transport.Deliver(ShermanLPacket);
            time.Advance(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(ConnectionState.Connected, session.CurrentState);
        await session.DisconnectAsync();
    }

    /// <summary>Ноль выключает сторожа — соглашение оригинала для всех порогов.</summary>
    [Fact]
    public async Task Zero_turns_the_watchdog_off()
    {
        var (session, transport, time) = Build(new ConnectionOptions { DataTimeout = TimeSpan.Zero });

        await session.ConnectAsync(Mac);
        transport.Deliver(ShermanLPacket);
        time.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(ConnectionState.Connected, session.CurrentState);
        await session.DisconnectAsync();
    }

    private static (WheelSession Session, FakeTransport Transport, FakeTimeProvider Time) Build(
        ConnectionOptions? options = null)
    {
        var transport = new FakeTransport();
        var time = new FakeTimeProvider();
        var session = new WheelSession(
            transport,
            new AppWheelConfig(),
            new NullEventSink(),
            time,
            options ?? new ConnectionOptions { RetryDelay = TimeSpan.FromSeconds(5) },
            new WheelDetector(NullLogger<WheelDetector>.Instance),
            NullLoggerFactory.Instance);

        return (session, transport, time);
    }

    /// <summary>
    /// Drives the retry loop: virtual time only moves when pushed, and the loop needs a real
    /// moment to act on it, so the two are alternated until the state arrives.
    /// </summary>
    private static async Task WaitForState(WheelSession session, ConnectionState expected, FakeTimeProvider time)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (session.CurrentState == expected) return;

            time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(20);
        }

        Assert.Fail($"session stayed in {session.CurrentState} instead of reaching {expected}");
    }
}
