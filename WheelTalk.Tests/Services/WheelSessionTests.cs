using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WheelTalk.Tests.TestSupport;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Detection;
using WheelTalk.Core.Ports;
using WheelTalk.Core.Services;

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

    /// <summary>Второе колесо — Тенчик из той же ночи 09.08.2026, когда их впервые сменили за один запуск.</summary>
    private const string OtherMac = "88:25:83:F2:1A:98";

    /// <summary>Отпечаток дерева GATT InMotion V2 (см. WheelDetectorTests) — заставляет сессию
    /// выбрать <c>InMotionDecoderV2_1</c> напрямую, а не гадать протокол по кадру: заголовок
    /// <c>AA AA</c> у V1 и V2 общий, и по кадру их не различить.</summary>
    private static readonly DiscoveredService[] InMotionV2Tree =
    [
        new("00001800-0000-1000-8000-00805f9b34fb",
            ["00002a00-0000-1000-8000-00805f9b34fb", "00002a01-0000-1000-8000-00805f9b34fb",
                "00002a04-0000-1000-8000-00805f9b34fb", "00002aa6-0000-1000-8000-00805f9b34fb"]),
        new("00001801-0000-1000-8000-00805f9b34fb", []),
        new("6e400001-b5a3-f393-e0a9-e50e24dcca9e",
            ["6e400002-b5a3-f393-e0a9-e50e24dcca9e", "6e400003-b5a3-f393-e0a9-e50e24dcca9e"]),
    ];

    /// <summary>Отпечаток дерева GATT KingSong (см. WheelDetectorTests) — сессия выбирает
    /// <c>KingsongDecoder</c> напрямую: колесо молчит, пока его не спросят, и по кадру протокол не
    /// выбрать (§21 порядка работ).</summary>
    private static readonly DiscoveredService[] KingSongTree =
    [
        new("00001800-0000-1000-8000-00805f9b34fb",
            ["00002a00-0000-1000-8000-00805f9b34fb", "00002a01-0000-1000-8000-00805f9b34fb",
                "00002a02-0000-1000-8000-00805f9b34fb", "00002a03-0000-1000-8000-00805f9b34fb",
                "00002a04-0000-1000-8000-00805f9b34fb"]),
        new("00001801-0000-1000-8000-00805f9b34fb", ["00002a05-0000-1000-8000-00805f9b34fb"]),
        new("0000180a-0000-1000-8000-00805f9b34fb",
            ["00002a23-0000-1000-8000-00805f9b34fb", "00002a24-0000-1000-8000-00805f9b34fb",
                "00002a25-0000-1000-8000-00805f9b34fb", "00002a26-0000-1000-8000-00805f9b34fb",
                "00002a27-0000-1000-8000-00805f9b34fb", "00002a28-0000-1000-8000-00805f9b34fb",
                "00002a29-0000-1000-8000-00805f9b34fb", "00002a2a-0000-1000-8000-00805f9b34fb",
                "00002a50-0000-1000-8000-00805f9b34fb"]),
        new("0000fff0-0000-1000-8000-00805f9b34fb",
            ["0000fff1-0000-1000-8000-00805f9b34fb", "0000fff2-0000-1000-8000-00805f9b34fb",
                "0000fff3-0000-1000-8000-00805f9b34fb", "0000fff4-0000-1000-8000-00805f9b34fb",
                "0000fff5-0000-1000-8000-00805f9b34fb"]),
        new("0000ffe0-0000-1000-8000-00805f9b34fb", ["0000ffe1-0000-1000-8000-00805f9b34fb"]),
    ];

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

    /// <summary>
    /// <b>Наш сон — не молчание колеса.</b> Пока система морозит процесс (сон экрана, Doze), кадры
    /// не обрабатываются и время последнего кадра стареет вместе со сном, хотя колесо шлёт исправно.
    /// Сторож, разбуженный одним запоздалым тиком, принимал секунды <b>нашего</b> сна за молчание
    /// колеса и рвал живую связь: владелец просыпался на плашку «переподключение», которая через
    /// пару секунд чинилась сама (11.08.2026, тянулось с прошлых версий).
    /// <para>
    /// Улику опознаёт сам сторож: разрыв между его тиками много больше их периода — это время, в
    /// которое не работали мы. Молчание считается заново, колесу даётся обычный таймаут на кадр.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_frozen_process_is_not_a_silent_wheel()
    {
        var time = new SleepyTimeProvider();
        var (session, transport) = Build(time, new ConnectionOptions { DataTimeout = TimeSpan.FromSeconds(15) });

        await session.ConnectAsync(Mac);
        transport.Deliver(ShermanLPacket);
        Assert.Equal(ConnectionState.Connected, session.CurrentState);

        // Телефон уснул на пять минут: часы ушли, тиков не было, кадры остались неразобранными.
        time.Sleep(TimeSpan.FromMinutes(5));
        time.Tick();

        Assert.Equal(ConnectionState.Connected, session.CurrentState);

        // И кадр после пробуждения приходит в пределах обычного таймаута — связь была жива всё это
        // время, рвать её было не за что.
        time.Sleep(TimeSpan.FromSeconds(10));
        transport.Deliver(ShermanLPacket);
        time.Tick();

        Assert.Equal(ConnectionState.Connected, session.CurrentState);

        await session.DisconnectAsync();
    }

    /// <summary>
    /// Амнистия — фора, а не помилование: если и после пробуждения кадров нет дольше таймаута,
    /// связь рвётся честно, следующим же тиком. Иначе уснувший телефон стал бы способом никогда не
    /// заметить настоящее стойло.
    /// </summary>
    [Fact]
    public async Task Silence_after_the_sleep_still_counts_as_a_drop()
    {
        var time = new SleepyTimeProvider();
        var (session, transport) = Build(time, new ConnectionOptions
        {
            DataTimeout = TimeSpan.FromSeconds(15),
            RetryDelay = TimeSpan.FromSeconds(5),
        });

        await session.ConnectAsync(Mac);
        transport.Deliver(ShermanLPacket);

        time.Sleep(TimeSpan.FromMinutes(5));
        time.Tick();
        Assert.Equal(ConnectionState.Connected, session.CurrentState);

        // Фора кончилась, а колесо так и молчит — это уже его молчание, а не наш сон.
        time.Sleep(TimeSpan.FromSeconds(16));
        time.Tick();

        Assert.NotEqual(ConnectionState.Connected, session.CurrentState);

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

    /// <summary>
    /// Колесо, которое мы слышим, но телеметрии от него ещё не сложилось, — тоже на связи. Сторож
    /// кормится узнанным кадром декодера, а не разобранным снимком: снимок — вывод декодера, а
    /// сторож судит о разговоре. Кадр ниже — настоящий carType-ответ InMotion P6 из
    /// <c>replay/inmotion-p6-first-contact.csv</c> (раскладка в docs/inmotion-p6-protocol.md):
    /// заголовок, длина и контрольная сумма сошлись, он называет колесо (series 13 / type 1 → P6),
    /// но телеметрии не несёт — <c>Decode</c> возвращает <c>false</c>, и <c>LastSnapshot</c>
    /// остаётся пуст. Раньше такой кадр сторожа не кормил вовсе, и P6 02.08.2026 уходил в вечный
    /// цикл переподключений при исправной связи.
    /// </summary>
    [Fact]
    public async Task A_recognised_frame_with_no_telemetry_still_counts_as_a_live_link()
    {
        var (session, transport, time) = Build(new ConnectionOptions { DataTimeout = TimeSpan.FromSeconds(15) });
        transport.Services = InMotionV2Tree;

        await session.ConnectAsync(Mac);

        for (int i = 0; i < 10; i++)
        {
            transport.Deliver("aaaa11088201020d0101010094");
            time.Advance(TimeSpan.FromSeconds(10));
        }

        Assert.Null(session.LastSnapshot);
        Assert.Equal(ConnectionState.Connected, session.CurrentState);
        await session.DisconnectAsync();
    }

    /// <summary>
    /// 08.08.2026: KS-S22 после третьего переподключения отвечал только девятью байтами
    /// «AT+ULKTE» раз в 2,4 с — не кадр ни по заголовку (<c>AA 55</c>), ни по длине
    /// (<c>KingsongDecoder.IsWheelFrame</c>). Раньше это всё равно кормило сторожа байтами с
    /// транспорта, и зависание не лечилось само; узнанный кадр отличает эту немоту от разговора.
    /// </summary>
    [Fact]
    public async Task A_module_that_only_echoes_noise_is_treated_as_a_drop()
    {
        var (session, transport, time) = Build(new ConnectionOptions { DataTimeout = TimeSpan.FromSeconds(15) });
        transport.Services = KingSongTree;

        await session.ConnectAsync(Mac);

        for (int i = 0; i < 7; i++)
        {
            transport.Deliver("41542b554c4b544500");
            time.Advance(TimeSpan.FromSeconds(2.4));
        }

        await WaitForState(session, ConnectionState.Reconnecting, time);

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

    /// <summary>
    /// «Колесо сменилось» знает сессия, и только она (план 29 §29.1). До этого события знание
    /// вычисляли трое — экран панели, плитки и имя колеса, — каждый своим сравнением адресов, и
    /// ночь 09.08.2026 дала четыре бага одной природы: путь через поиск чью-нибудь из этих проверок
    /// да минует.
    /// </summary>
    [Fact]
    public async Task Going_after_another_wheel_is_announced_once()
    {
        var (session, _, _) = Build();
        var changes = new List<(string? Previous, string Current)>();
        session.WheelChanged += (previous, current) => changes.Add((previous, current));

        await session.ConnectAsync(Mac);
        await session.ConnectAsync(OtherMac);

        Assert.Equal([(null, Mac), (Mac, OtherMac)], changes);

        await session.DisconnectAsync();
    }

    /// <summary>
    /// Переподключение к тому же колесу — не смена: поездка продолжается, точка отсчёта «от старта»
    /// цела (планы 15/23), и чистить подписчикам нечего. Регистр адреса значения не имеет — тот же
    /// порог, что был у проверки в <c>MainActivity.Render</c>.
    /// </summary>
    [Fact]
    public async Task Coming_back_to_the_same_wheel_is_not_a_change()
    {
        var (session, transport, time) = Build();
        int changes = 0;
        session.WheelChanged += (_, _) => changes++;

        await session.ConnectAsync(Mac);

        // Три пути к тому же колесу: обрыв с погоней, «отключить» и снова подключиться, и тот же
        // адрес другим регистром — так его отдаёт скан.
        transport.DropLink();
        await WaitForState(session, ConnectionState.Connected, time);

        await session.DisconnectAsync();
        await session.ConnectAsync(Mac.ToLowerInvariant());

        Assert.Equal(1, changes);

        await session.DisconnectAsync();
    }

    /// <summary>
    /// План 11 §3.2: после нескольких отказов подряд стоит спросить о причине — три ситуации
    /// («колесо выключено», «Bluetooth выключен», «разрешение отозвано») выглядят одинаково, и
    /// вечная погоня с враньём на экране случается ровно во второй и третьей. Сессия причин не
    /// знает и знать не может — её дело сказать «попытки идут впустую».
    /// </summary>
    [Fact]
    public async Task A_chase_that_keeps_failing_says_so_once()
    {
        var (session, transport, time) = Build();
        int troubles = 0;
        session.ChaseTroubled += () => troubles++;
        transport.RefuseConnections = true;

        await session.ConnectAsync(Mac);
        for (int second = 0; second < 20 && transport.ConnectAttempts < ChaseTrouble.Threshold + 3; second++)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(20);
        }

        Assert.True(transport.ConnectAttempts > ChaseTrouble.Threshold,
            $"погоня не дошла до порога: попыток {transport.ConnectAttempts}");

        // Один раз за погоню, а не на каждой попытке: спрашивать причину двести раз в час — та
        // самая лишняя работа, ради которой порог и заведён.
        Assert.Equal(1, troubles);

        await session.DisconnectAsync();
    }

    /// <summary>Связь есть — счёт начинается заново: следующая порция неудач будет про новую беду.</summary>
    [Fact]
    public async Task A_link_that_comes_back_resets_the_count_of_failures()
    {
        var (session, transport, time) = Build();
        int troubles = 0;
        session.ChaseTroubled += () => troubles++;

        // Два отказа, затем связь: до порога не дошло, и после успеха счёт обнулён.
        transport.RefuseConnections = true;
        await session.ConnectAsync(Mac);
        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(20);

        transport.RefuseConnections = false;
        await WaitForState(session, ConnectionState.Connected, time);

        Assert.Equal(0, troubles);

        await session.DisconnectAsync();
    }

    private static (WheelSession Session, FakeTransport Transport, FakeTimeProvider Time) Build(
        ConnectionOptions? options = null)
    {
        var time = new FakeTimeProvider();
        var (session, transport) = Build(time, options);

        return (session, transport, time);
    }

    /// <summary>Та же сессия на чужих часах — там, где замку нужна своя модель времени.</summary>
    private static (WheelSession Session, FakeTransport Transport) Build(
        TimeProvider time, ConnectionOptions? options = null)
    {
        var transport = new FakeTransport();
        var session = new WheelSession(
            transport,
            new AppWheelConfig(),
            new NullEventSink(),
            time,
            options ?? new ConnectionOptions { RetryDelay = TimeSpan.FromSeconds(5) },
            new WheelDetector(NullLogger<WheelDetector>.Instance),
            NullLoggerFactory.Instance);

        return (session, transport);
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
