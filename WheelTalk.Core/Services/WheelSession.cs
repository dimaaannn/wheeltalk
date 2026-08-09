using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Detection;
using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Services;

/// <summary>
/// Owns the connection to one wheel, including keeping it: once a wheel has been asked for, the
/// session chases it until the rider says otherwise. Screens come and go without touching any of
/// this, which is why it lives in the core rather than behind a page.
///
/// Every connection gets a fresh <see cref="WheelState"/>, decoder and <see cref="WheelService"/>:
/// wheel state accumulates and has no reset, so reusing it across connections would mix two rides
/// together.
/// </summary>
public sealed partial class WheelSession : IDisposable
{
    private readonly ITransport _transport;
    private readonly IWheelConfig _config;
    private readonly IEventSink _eventSink;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WheelSession> _logger;
    private readonly ConnectionOptions _options;

    private readonly BehaviorSubject<ConnectionState> _state = new(ConnectionState.Disconnected);
    private readonly Subject<TelemetrySnapshot> _telemetry = new();

    private readonly Lock _chaseGate = new();

    private CancellationTokenSource? _keepConnected;
    private Task? _chase;
    private WheelService? _service;
    private IDisposable? _serviceTelemetry;

    // Тот же декодер, что внутри _service, — но под своим именем: сервис о пароле ничего не знает,
    // а лезть за декодером через него значило бы открыть его наружу ради одного вызова.
    private IPasswordProtected? _passwordProtected;

    /// <summary>Обёртка текущего подключения — держим её лишь затем, чтобы снять
    /// <see cref="OnFrameRecognized"/> в <see cref="TearDownService"/>. Живёт ровно столько же,
    /// сколько <see cref="_service"/>.</summary>
    private Decoder? _decoder;

    /// <summary>
    /// Состояние текущего подключения — сессия держит его ради одного: точки отсчёта «от старта»,
    /// которую наследует состояние следующей попытки (<see cref="BuildService"/>). Живёт до
    /// <see cref="DisconnectAsync"/>, то есть ровно столько же, сколько намерение быть на связи.
    /// </summary>
    private WheelState? _wheelState;

    /// <summary>Пауза между кадрами, о которой стоит написать в журнал.</summary>
    private static readonly TimeSpan NoticeableGap = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Когда декодер в последний раз узнал кадр этого протокола — не байты с транспорта и не
    /// разобранный снимок. Ноль — кадра не было ещё вовсе.
    /// <para>
    /// Раньше здесь стояло время снимка: снимок — вывод декодера, а сторож судит о <b>связи</b>.
    /// Колесо, которое мы слышим, но не понимаем, снимков не даёт — и получало приговор ровно через
    /// <see cref="ConnectionOptions.DataTimeout"/>. На InMotion P6 02.08.2026 это дало вечный цикл
    /// переподключений при исправной связи.
    /// </para>
    /// <para>
    /// Байты с транспорта чинили этот случай, но ловили и то, что нашим протоколом не является
    /// вовсе: KS-S22 08.08.2026 после третьего переподключения отвечал только девятью байтами
    /// «AT+ULKTE» раз в 2,4 с — не кадр ни по заголовку, ни по длине, — а сторож считал связь
    /// живой сколько угодно. Узнанный кадр (заголовок, длина, контрольная сумма сошлись —
    /// <see cref="Decoding.IWheelDecoder.FrameRecognized"/>) ловит оба случая правильно, не заходя
    /// внутрь смысла кадра.
    /// </para>
    /// </summary>
    private long _lastDataAt;

    private ITimer? _watchdog;

    /// <summary>
    /// За каким колесом сессия шла в последний раз — не то же, что <see cref="Address"/>: тот
    /// обнуляется отключением. Переживает <see cref="DisconnectAsync"/> намеренно: вернуться к тому
    /// же колесу после паузы — не смена колеса, и чистить подписчикам нечего.
    /// </summary>
    private string? _followedWheel;

    /// <summary>Отказов подряд — сбрасывается связью и новым подключением; см. <see cref="ChaseTroubled"/>.</summary>
    private int _failuresInARow;

    private readonly WheelDetector _detector;
    private readonly WheelProtocol? _replayProtocolOverride;

    public WheelSession(
        ITransport transport,
        IWheelConfig config,
        IEventSink eventSink,
        TimeProvider timeProvider,
        ConnectionOptions options,
        WheelDetector detector,
        ILoggerFactory loggerFactory,
        WheelProtocol? replayProtocolOverride = null)
    {
        _transport = transport;
        _config = config;
        _eventSink = eventSink;
        _timeProvider = timeProvider;
        _options = options;
        _detector = detector;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<WheelSession>();
        _replayProtocolOverride = replayProtocolOverride;

        transport.ConnectionLost += OnConnectionLost;
    }

    /// <summary>Telemetry of the current wheel. Survives reconnects — subscribers never resubscribe.</summary>
    public IObservable<TelemetrySnapshot> Telemetry => _telemetry;

    /// <summary>Current state, and every change to it. Replays the latest value on subscribe.</summary>
    public IObservable<ConnectionState> State => _state;

    public ConnectionState CurrentState => _state.Value;

    public TelemetrySnapshot? LastSnapshot { get; private set; }

    public string? Address { get; private set; }

    /// <summary>
    /// Сессия пошла за другим колесом, чем прежде: прежний адрес и новый (прежний — <c>null</c> на
    /// самом первом подключении за запуск). Единственное место, которое об этом <b>знает</b>, —
    /// сессия, и потребители узнают отсюда, а не сравнением адресов у себя: каждое такое сравнение
    /// — догадка о чужом состоянии, и четвёртый потребитель заводит четвёртую (план 29 §29.1).
    /// <para>
    /// Переподключение к тому же колесу сменой <b>не является</b>: адрес не менялся, поездка
    /// продолжается, точка отсчёта «от старта» цела. Не является ею и обрыв с погоней — сессия
    /// по-прежнему идёт за тем же колесом.
    /// </para>
    /// <para>
    /// Поднимается синхронно из <see cref="ConnectAsync"/>, на потоке подключавшегося и раньше
    /// первого кадра нового колеса: подписчик чистит своё до того, как в него польются чужие
    /// данные. Кому нужен UI-поток — маршалит сам.
    /// </para>
    /// </summary>
    public event Action<string?, string>? WheelChanged;

    /// <summary>
    /// Погоня буксует: <see cref="ChaseTrouble.Threshold"/> отказов подряд, и стоит спросить о
    /// причине (план 11 §3.2). Ровно один раз за погоню — считает <see cref="ChaseTrouble"/>.
    /// <para>
    /// Сессия сама причин не знает и знать не может: выключенный адаптер, отозванное разрешение и
    /// выключенный локационный переключатель — вопросы к платформе, а не к транспорту. Её дело —
    /// сказать «попытки идут впустую», а спрашивать будет тот, у кого есть чем.
    /// </para>
    /// </summary>
    public event Action? ChaseTroubled;

    /// <summary>
    /// Протокол колеса — <b>не выбранный, а опознанный</b>: до первого кадра он неизвестен, и это
    /// нормальное состояние первой доли секунды после подключения. Заполняется
    /// <see cref="AutoDecoder"/> по заголовку кадра.
    /// </summary>
    public WheelProtocol? Protocol { get; private set; }

    /// <summary>
    /// Asks for a wheel and keeps asking. Returns once the first attempt has been made — success
    /// or failure both leave the session chasing, so callers do not need to retry themselves.
    /// <para>
    /// Исключение отсюда означает одно: колесо не наше — не опознано или опознано, но говорить с
    /// ним нечем. Погоня в этом случае не начинается и не продолжается: повторами чужое колесо
    /// своим не станет, а вечная попытка подключения к соседскому Ninebot — худшее, что можно
    /// сделать с батареей телефона.
    /// </para>
    /// </summary>
    public async Task ConnectAsync(string address, CancellationToken ct = default)
    {
        await DisconnectAsync(ct);

        Address = address;
        Protocol = null;
        _failuresInARow = 0;

        if (!string.Equals(_followedWheel, address, StringComparison.OrdinalIgnoreCase))
        {
            var previous = _followedWheel;
            _followedWheel = address;
            WheelChanged?.Invoke(previous, address);
        }

        _keepConnected = new CancellationTokenSource();
        _state.OnNext(ConnectionState.Connecting);

        await AttemptAsync(_keepConnected.Token);
        if (_state.Value != ConnectionState.Connected)
        {
            StartChasing();
        }
    }

    /// <summary>Gives up on the wheel entirely — the only way to stop the chase.</summary>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_keepConnected is null) return;

        await _keepConnected.CancelAsync();
        _keepConnected.Dispose();
        _keepConnected = null;
        lock (_chaseGate) _chase = null;

        TearDownService();

        // Здесь и только здесь кончается поездка — обрыв связи её не заканчивает, потому и точка
        // отсчёта «от старта» переживает погоню, но не это.
        _wheelState = null;

        Address = null;
        await _transport.DisconnectAsync(ct);
        _state.OnNext(ConnectionState.Disconnected);
        LogSessionStopped();
    }

    /// <summary>
    /// Отправляет команду колесу. Сервиса нет в двух случаях — не подключены вовсе и идёт погоня
    /// после обрыва, — и оба это отказ, а не успех: судьба команды видна вызывающему ровно по этой
    /// задаче (шторка красит кнопку по ней), поэтому молча завершённая задача светила бы
    /// «доставлено» в пустоту. План 11 §3.5.
    /// </summary>
    public Task SendCommand(WheelCommand command, CancellationToken ct = default)
    {
        var service = _service;
        if (service is null)
        {
            LogCmdNoLink(command);
            return Task.FromException(new WheelNotConnectedException(command));
        }

        return service.SendCommand(command, ct);
    }

    /// <summary>«Сброс максимумов» — see <see cref="WheelService.ResetPeaks"/>. No-op with nothing connected.</summary>
    public void ResetPeaks() => _service?.ResetPeaks();

    /// <summary>
    /// Колесо не пустило: пароля нет либо он не подошёл (<see cref="Decoding.IPasswordProtected"/>).
    /// Связь при этом исправна — кадры идут, линк живой, — поэтому это не состояние связи, а
    /// отдельный ответ на отдельный вопрос: экран показывает по нему причину и путь к настройке.
    /// </summary>
    public bool AwaitingPassword => _passwordProtected?.AwaitingPassword ?? false;

    /// <summary>
    /// Пароль сменили в настройках — просим декодер начать разговор заново. Переподключение здесь
    /// было бы лишним: линк живой, а протоколу нужен всего лишь новый кадр пароля.
    /// <para>
    /// Тихо ничего не делает, когда протокол пароля не спрашивает или сервиса нет вовсе, — вызвать
    /// это по кнопке «сохранить» из настроек безопасно в любой момент.
    /// </para>
    /// </summary>
    public void RestartAuthentication() => _passwordProtected?.RestartAuthentication();

    public void Dispose()
    {
        _transport.ConnectionLost -= OnConnectionLost;
        _keepConnected?.Cancel();
        _keepConnected?.Dispose();
        TearDownService();
        _telemetry.Dispose();
        _state.Dispose();
    }

    /// <summary>
    /// One connection attempt. Отказ линка — дело житейское: он пишется в журнал, и сессия
    /// пробует снова. Отказ <b>опознания</b> — другое дело: он бросается наружу и погоню
    /// прекращает, см. <see cref="ConnectAsync"/>.
    /// </summary>
    private async Task AttemptAsync(CancellationToken ct, bool waitForWheel = false)
    {
        IReadOnlyList<DiscoveredService> discovered;
        try
        {
            discovered = waitForWheel
                ? await _transport.WaitForWheelAsync(Address!, ct)
                : await _transport.ConnectAsync(Address!, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogAttemptFailed(ex, Address!);
            NoteFailure();
            return;
        }

        var (refusal, family) = Refusal(discovered);
        if (refusal is not null)
        {
            // Отключаемся сами и до конца: линк с чужим колесом держать незачем, а погоня за ним
            // не должна начаться ни сейчас, ни по обрыву — DisconnectAsync снимает и её.
            await DisconnectAsync(CancellationToken.None);
            throw refusal;
        }

        var service = BuildService(family);
        _service = service;
        _serviceTelemetry = service.Telemetry.Subscribe(OnSnapshot);
        Interlocked.Exchange(ref _lastDataAt, _timeProvider.GetTimestamp());
        StartWatchdog();

        // Связь есть — счёт отказов начинается заново: следующая порция неудач будет уже про новую
        // беду, а не про ту, о которой человеку однажды сказали.
        _failuresInARow = 0;
        _state.OnNext(ConnectionState.Connected);
        LogSessionStarted(Address!);
    }

    /// <summary>
    /// Ещё одна попытка впустую. Событие поднимается ровно на пороге и только на нём — решает
    /// <see cref="ChaseTrouble.ShouldAskWhy"/>, чистая функция под тестом; здесь остаётся счёт.
    /// </summary>
    private void NoteFailure()
    {
        _failuresInARow++;
        if (ChaseTrouble.ShouldAskWhy(_failuresInARow)) ChaseTroubled?.Invoke();
    }

    /// <summary>
    /// Отказ (или <c>null</c>, если можно говорить с этим колесом) вместе с опознанным семейством —
    /// <see cref="BuildService"/> использует его, чтобы выбрать декодер напрямую там, где нюхать
    /// заголовок кадра недостаточно (план 21 §0.2: InMotion V1 и V2 оба шлют <c>AA AA</c>).
    /// <para>
    /// Пустое дерево — не отказ: так отвечает реплей записанной поездки, где GATT не было вовсе.
    /// Протокол там опознаётся по кадрам, ровно как и на живом колесе, и семейство остаётся
    /// <c>null</c> — для реплея решает <see cref="AutoDecoder"/>, как и раньше.
    /// </para>
    /// </summary>
    private (Exception? Refusal, WheelFamily? Family) Refusal(IReadOnlyList<DiscoveredService> discovered)
    {
        if (discovered.Count == 0) return (null, null);

        if (_detector.Detect(discovered) is not { } family) return (new WheelNotRecognisedException(), null);

        return WheelFamilies.IsSupported(family) ? (null, family) : (new WheelNotSupportedException(family), null);
    }

    private WheelService BuildService(WheelFamily? family)
    {
        var state = new WheelState(_config, _timeProvider);

        // Поездку заканчивает райдер, а не обрыв связи: новое состояние продолжает считать «от
        // старта» с той же точки, иначе автопереподключение обнуляло бы пробег посреди поездки.
        // Наследуется только она: максимумы уходят вместе с состоянием, как и раньше.
        state.SetStartTotalDistance(_wheelState?.StartTotalDistance ?? 0);
        _wheelState = state;

        var protocolDecoder = BuildProtocolDecoder(family, state);

        // Живёт ровно столько же, сколько сервис: TearDownService снимает ссылку вместе с ним,
        // иначе декодер прошлого подключения отвечал бы про пароль за новое.
        _passwordProtected = protocolDecoder as IPasswordProtected;

        var decoder = new Decoder(state, protocolDecoder, _eventSink, _loggerFactory.CreateLogger<Decoder>());
        _decoder = decoder;
        decoder.FrameRecognized += OnFrameRecognized;
        return new WheelService(_transport, decoder, _loggerFactory.CreateLogger<WheelService>());
    }

    /// <summary>
    /// Families the GATT tree names outright, so their decoder is picked directly rather than
    /// through <see cref="AutoDecoder"/>'s sniffing (plan 21 §0.2). Two different reasons to be
    /// here:
    /// <list type="bullet">
    ///   <item>InMotion V1/V2 — их заголовок <c>AA AA</c> общий, по кадру их не различить вовсе.</item>
    ///   <item>KingSong — заголовок <c>AA 55</c> как раз однозначен, но <b>колесо молчит, пока его
    ///   не спросят</b>, а спрашивает декодер: ждать кадра, чтобы выбрать того, кто этот кадр
    ///   вызовет, — тупик. Живой KS-16S 03.08.2026 в нём и стоял: слушали кадр, которого никто не
    ///   просил, двадцать секунд до сбора диагностики.</item>
    /// </list>
    /// Gotway/Veteran остаются на нюхе: общий профиль FFE0/FFE1 не различает их в принципе, а
    /// говорить первым там незачем — оба шлют кадры сами.
    /// </summary>
    private static WheelProtocol? DirectProtocolFor(WheelFamily family) => family switch
    {
        WheelFamily.KingSong => WheelProtocol.KingSong,
        WheelFamily.InMotion => WheelProtocol.InMotion,
        // V2-1, а не V2: по дереву GATT колесо вне таблицы carType (P6) неотличимо от V11/V12, а
        // сам carType приходит уже после рукопожатия — значит выбор делается не здесь, а внутри
        // InMotionDecoderV2_1, когда колесо назовёт себя. Для моделей из таблицы оригинала он
        // прозрачен и отдаёт всё нетронутому InMotionDecoderV2.
        WheelFamily.InMotionV2 => WheelProtocol.InMotionV2_1,
        _ => null,
    };

    private IWheelDecoder BuildProtocolDecoder(WheelFamily? family, WheelState state)
    {
        // family is null in exactly one case that reaches here: an empty GATT tree, i.e. a replay
        // (Refusal already threw for a live, non-empty tree that didn't match any known family).
        // Replay has no GATT tree to name a family from at all, so InMotion V1/V2 — both AA AA,
        // indistinguishable by frame content — fall back to whatever the replay was told to assume
        // (_replayProtocolOverride, wired from a replay-only setting; see replay/README.md). Every
        // other protocol's dumps carry their own header and need no override, which is why this
        // stays null for them and AutoDecoder's sniffing below still runs.
        WheelProtocol? direct = family is { } known ? DirectProtocolFor(known) : _replayProtocolOverride;
        if (direct is { } protocol)
        {
            Protocol = protocol;
            return WheelDecoderFactory.Create(protocol, state, _config, _timeProvider, _loggerFactory);
        }

        // Протокол не выбирается, а опознаётся по первому кадру — AutoDecoder. Сессия узнаёт о нём
        // тогда же, когда и он: до этого момента говорить, что перед нами Veteran, попросту не из
        // чего.
        var auto = new AutoDecoder(state, _config, _timeProvider, _loggerFactory);
        auto.Detected += p => Protocol = p;
        return auto;
    }

    /// <summary>
    /// Кормит сторожа — и только его: сам разбор кадра уже сделан декодером, у которого своя
    /// подписка на транспорт (<see cref="WheelService"/>). Сессии довольно факта «декодер узнал
    /// кадр», не важно, байты какого содержания в нём были.
    /// </summary>
    private void OnFrameRecognized(byte[] bytes)
    {
        long previous = Interlocked.Exchange(ref _lastDataAt, _timeProvider.GetTimestamp());

        // Провал короче сторожевого порога он не поймает, а знать о нём стоит: именно из таких
        // пауз складывается «данные дёргаются», и по журналу это должно быть видно без раскопок в
        // базе поездок. Два десятка пропущенных пакетов — уже не дрожание. Молчание во время
        // погони — не провал, а её нормальное состояние, потому и только на связи.
        if (previous == 0 || _state.Value != ConnectionState.Connected) return;

        var gap = _timeProvider.GetElapsedTime(previous);
        if (gap >= NoticeableGap) LogDataResumed(Address ?? "", (int)gap.TotalSeconds);
    }

    private void OnSnapshot(TelemetrySnapshot snapshot)
    {
        LastSnapshot = snapshot;
        _telemetry.OnNext(snapshot);
    }

    /// <summary>
    /// Сторож данных: связь считается живой, только пока идут кадры. Заведён потому, что
    /// «подключено» и «данные идут» — разные вещи, и расходятся они молча: 30.07.2026 выключенное
    /// колесо оставалось подключённым 68 минут, GATT об обрыве не сообщил, погоня не начиналась, а
    /// в журнале не было ни строки. Тот же сторож есть у оригинала
    /// (<c>BluetoothService.startReconnectTimer</c>) — там он проверяет то же самое раз в
    /// пятнадцать секунд.
    /// <para>
    /// Обрыв разыгрывается как настоящий: тот же <see cref="OnConnectionLost"/>, что и от
    /// транспорта, — значит и состояние, и погоня, и экран ведут себя одинаково, независимо от
    /// того, кто первым заметил пропажу.
    /// </para>
    /// </summary>
    private void StartWatchdog()
    {
        StopWatchdog();
        if (_options.DataTimeout <= TimeSpan.Zero) return;

        _watchdog = _timeProvider.CreateTimer(_ => CheckFrames(), null,
            _options.DataTimeout, _options.DataTimeout);
    }

    private void StopWatchdog()
    {
        _watchdog?.Dispose();
        _watchdog = null;
    }

    private void CheckFrames()
    {
        if (_state.Value != ConnectionState.Connected) return;

        var silence = _timeProvider.GetElapsedTime(Interlocked.Read(ref _lastDataAt));
        if (silence < _options.DataTimeout) return;

        LogDataStalled(Address ?? "", (int)silence.TotalSeconds);
        _transport.DisconnectAsync().ContinueWith(_ => { }, TaskScheduler.Default);
        OnConnectionLost();
    }

    private void OnConnectionLost()
    {
        if (_keepConnected is null) return;

        // The service goes, the last snapshot stays: screens keep showing the final readings while
        // the session works on getting the wheel back.
        TearDownService();
        _state.OnNext(ConnectionState.Reconnecting);
        LogConnectionLost(Address ?? "");
        StartChasing();
    }

    /// <summary>
    /// Retries until the wheel answers or the rider disconnects. Deliberately sequential: one
    /// attempt at a time, each starting only after the previous one finished, so a wheel that is
    /// simply switched off does not accumulate half-open connections.
    /// <para>
    /// And exactly one chase at a time. A failed attempt can itself raise ConnectionLost, which
    /// asks for another chase — so without this guard the loops breed: the field test of
    /// 28.07.2026 came back with six of them running at once, each opening its own GATT client
    /// against the same wheel.
    /// </para>
    /// <para>
    /// This is the only retry loop in the app. Transports are asked exactly once per attempt and
    /// are expected to report failure rather than quietly trying again — two independent retry
    /// mechanisms over one link is the other half of what bred those loops.
    /// </para>
    /// </summary>
    private void StartChasing()
    {
        var cts = _keepConnected;
        if (cts is null) return;

        lock (_chaseGate)
        {
            if (_chase is { IsCompleted: false }) return;
            _chase = Chase(cts);
        }
    }

    private Task Chase(CancellationTokenSource cts) =>
        Task.Run(async () =>
        {
            var delay = Min(_options.FirstRetryDelay, _options.RetryDelay);

            // Первая попытка — прямая: обрыв на ходу это чаще всего полуоткрытый линк, и прямой
            // коннект через полсекунды чинит его быстрее любого ожидания. Все последующие —
            // пассивное ожидание колеса (см. ITransport.WaitForWheelAsync): так переподключается
            // оригинал, и так выключенное колесо перестаёт стоить батареи. Пауза между попытками
            // остаётся на случай, когда и ожидание отказывает сразу — например, при выключенном
            // Bluetooth, — иначе цикл превратился бы в долбёжку.
            bool directTried = false;
            try
            {
                while (!cts.IsCancellationRequested && _state.Value != ConnectionState.Connected)
                {
                    await Task.Delay(delay, _timeProvider, cts.Token);
                    if (cts.IsCancellationRequested) return;

                    _state.OnNext(ConnectionState.Reconnecting);
                    await AttemptAsync(cts.Token, waitForWheel: directTried);
                    directTried = true;
                    delay = Min(delay + delay, _options.RetryDelay);
                }
            }
            catch (OperationCanceledException)
            {
                // expected — the rider disconnected
            }
        }, cts.Token);

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private void TearDownService()
    {
        StopWatchdog();
        if (_decoder is not null) _decoder.FrameRecognized -= OnFrameRecognized;
        _decoder = null;
        _serviceTelemetry?.Dispose();
        _serviceTelemetry = null;

        _passwordProtected = null;

        _service?.Dispose();
        _service = null;
    }

    [LoggerMessage(EventId = 1304, EventName = "Wheel.DataStalled", Level = LogLevel.Warning,
        Message = "Wheel.DataStalled {Mac} — кадров нет {Seconds} с, считаем связь потерянной")]
    private partial void LogDataStalled(string mac, int seconds);

    [LoggerMessage(EventId = 1305, EventName = "Wheel.DataResumed", Level = LogLevel.Information,
        Message = "Wheel.DataResumed {Mac} — кадры пошли после {Seconds} с тишины")]
    private partial void LogDataResumed(string mac, int seconds);

    // Протокола здесь больше нет: на момент старта сессии он ещё не известен — его назовёт
    // Protocol.Detected, когда придёт первый кадр.
    [LoggerMessage(EventId = 1300, EventName = "Wheel.SessionStarted", Level = LogLevel.Information,
        Message = "Wheel.SessionStarted {Mac}")]
    private partial void LogSessionStarted(string mac);

    [LoggerMessage(EventId = 1301, EventName = "Wheel.SessionStopped", Level = LogLevel.Information,
        Message = "Wheel.SessionStopped")]
    private partial void LogSessionStopped();

    [LoggerMessage(EventId = 1302, EventName = "Wheel.ConnectionLost", Level = LogLevel.Warning,
        Message = "Wheel.ConnectionLost {Mac} — reconnecting")]
    private partial void LogConnectionLost(string mac);

    [LoggerMessage(EventId = 1303, EventName = "Wheel.AttemptFailed", Level = LogLevel.Warning,
        Message = "Wheel.AttemptFailed {Mac}")]
    private partial void LogAttemptFailed(Exception ex, string mac);

    /// <summary>
    /// Не ошибка приложения, а состояние мира: колесо выключено или ещё не пойманы. Пишется всё
    /// равно — без этой строки нажатие вообще не оставляло следа, и «кнопка не работает» было не с
    /// чем сверить.
    /// </summary>
    [LoggerMessage(EventId = 1306, EventName = "Wheel.CmdNoLink", Level = LogLevel.Warning,
        Message = "Wheel.CmdNoLink {Command} — связи нет, команда не отправлена")]
    private partial void LogCmdNoLink(WheelCommand command);
}
