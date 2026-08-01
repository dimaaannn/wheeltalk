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
/// Что сессия делает с колесом, которое ей не подходит. Решение владельца 31.07.2026: не опознали
/// или опознали неподдержанное — сказать и отключиться, **не пытаясь подключиться снова**. Вечная
/// погоня за соседским Ninebot — это разряженный телефон и ничего больше.
/// </summary>
public class WheelDetectionSessionTests
{
    private const string Mac = "88:25:83:F5:75:4A";

    private static string Uuid(string shortForm) => $"0000{shortForm}-0000-1000-8000-00805f9b34fb";

    private static DiscoveredService Service(string shortForm, params string[] characteristics) =>
        new(Uuid(shortForm), [.. characteristics.Select(Uuid)]);

    /// <summary>Дерево Sherman L — первый отпечаток Begode.</summary>
    private static DiscoveredService[] Gotway() =>
    [
        Service("1800", "2a00", "2a01", "2a02", "2a03", "2a04"),
        Service("1801", "2a05"),
        Service("180a", "2a23", "2a24", "2a25", "2a26", "2a27", "2a28", "2a29", "2a2a", "2a50"),
        Service("ffe0", "ffe1"),
    ];

    /// <summary>То же дерево минус 180a — и это уже Ninebot, которого мы не умеем.</summary>
    private static DiscoveredService[] Ninebot() =>
    [
        Service("1800", "2a00", "2a01", "2a02", "2a03", "2a04"),
        Service("1801", "2a05"),
        Service("ffe0", "ffe1"),
    ];

    /// <summary>Дерево посредника («третий глаз», план 20): Begode-посредник, метка ffa8.</summary>
    private static DiscoveredService[] GotwayProxy() =>
    [
        Service("1800", "2a00", "2a01", "2a04", "2aa6"),
        Service("1801"),
        Service("ffa0", "ffa1", "ffa8"),
        Service("ffe0", "ffe1"),
    ];

    [Fact]
    public async Task A_wheel_we_can_talk_to_connects()
    {
        var (session, transport, _) = Build();
        transport.Services = Gotway();

        await session.ConnectAsync(Mac);

        Assert.Equal(ConnectionState.Connected, session.CurrentState);
    }

    /// <summary>
    /// Посредник добавляет свою службу (ffa0) и меняет подпись 1800/1801 — прямое дерево колеса
    /// уже не совпадает, но второй проход детектора (план 20 §2) узнаёт его по отдельной таблице,
    /// и сессия подключается ровно так же, как к настоящему колесу.
    /// </summary>
    [Fact]
    public async Task A_wheel_reached_through_a_proxy_still_connects()
    {
        var (session, transport, _) = Build();
        transport.Services = GotwayProxy();

        await session.ConnectAsync(Mac);

        Assert.Equal(ConnectionState.Connected, session.CurrentState);
    }

    /// <summary>
    /// Сравнение точное и для таблицы посредников: одна лишняя служба поверх отпечатка посредника —
    /// и дерево не совпадает ни с одним колесом, ни с одним посредником.
    /// </summary>
    [Fact]
    public async Task A_proxy_tree_with_one_extra_service_is_refused()
    {
        var (session, transport, time) = Build();
        transport.Services = [.. GotwayProxy(), Service("1234", "5678")];

        await Assert.ThrowsAsync<WheelNotRecognisedException>(() => session.ConnectAsync(Mac));

        Assert.Equal(ConnectionState.Disconnected, session.CurrentState);
        await AssertNoFurtherAttempts(transport, time);
    }

    [Fact]
    public async Task An_unrecognised_device_is_refused_and_not_chased()
    {
        var (session, transport, time) = Build();
        transport.Services = [Service("1234", "5678")];

        await Assert.ThrowsAsync<WheelNotRecognisedException>(() => session.ConnectAsync(Mac));

        Assert.Equal(ConnectionState.Disconnected, session.CurrentState);
        await AssertNoFurtherAttempts(transport, time);
    }

    [Fact]
    public async Task A_wheel_whose_protocol_is_not_ported_is_named_rather_than_chased()
    {
        var (session, transport, time) = Build();
        transport.Services = Ninebot();

        var refusal = await Assert.ThrowsAsync<WheelNotSupportedException>(() => session.ConnectAsync(Mac));

        // Именно названо: человеку показывают «Ninebot», а не «непонятное устройство».
        Assert.Equal(WheelFamily.Ninebot, refusal.Family);
        Assert.Equal(ConnectionState.Disconnected, session.CurrentState);
        await AssertNoFurtherAttempts(transport, time);
    }

    [Fact]
    public async Task A_refused_wheel_leaves_no_link_behind()
    {
        var (session, transport, _) = Build();
        transport.Services = Ninebot();

        await Assert.ThrowsAsync<WheelNotSupportedException>(() => session.ConnectAsync(Mac));

        Assert.False(transport.IsConnected);
    }

    /// <summary>
    /// У записанной поездки дерева нет вовсе, и это не отказ: протокол там называют кадры дампа,
    /// такие же, как в эфире.
    /// </summary>
    [Fact]
    public async Task A_replayed_dump_has_no_gatt_tree_and_still_connects()
    {
        var (session, transport, _) = Build();
        transport.Services = [];

        await session.ConnectAsync(Mac);

        Assert.Equal(ConnectionState.Connected, session.CurrentState);
    }

    /// <summary>Протокол не выбран заранее — он появляется вместе с первым кадром.</summary>
    [Fact]
    public async Task The_protocol_is_unknown_until_the_wheel_speaks()
    {
        var (session, transport, _) = Build();
        transport.Services = Gotway();

        await session.ConnectAsync(Mac);
        Assert.Null(session.Protocol);

        transport.Deliver([
            "dc5a5c53397afffe0aa400000df10000000a0b3d",
            "0e0e0000037a035217730064000e00b480c80000",
            "808080808080058080808080800ff30ff50ff50f",
            "f50ff50fef0ff20ff30ff30ff30ff30fed0ff30f",
            "f40ff5378c5145",
        ]);

        Assert.Equal(WheelProtocol.Veteran, session.Protocol);
    }

    /// <summary>
    /// Отказ обязан быть окончательным: гоним время далеко вперёд и убеждаемся, что новых попыток
    /// нет. Одной проверки состояния мало — погоня живёт своим циклом и сработала бы позже.
    /// </summary>
    private static async Task AssertNoFurtherAttempts(FakeTransport transport, FakeTimeProvider time)
    {
        int attempts = transport.ConnectAttempts;

        for (int i = 0; i < 5; i++)
        {
            time.Advance(TimeSpan.FromSeconds(30));
            await Task.Delay(10);
        }

        Assert.Equal(attempts, transport.ConnectAttempts);
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
