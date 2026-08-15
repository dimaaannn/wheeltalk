using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Ports;
using Application = Android.App.Application;

namespace WheelTalk.Droid.Ble;

/// <summary>
/// ITransport over Android's BLE central API — the phone-side counterpart of the console app's
/// WindowsBleClient. No pairing is used or needed.
///
/// The notify/write characteristic pair is picked from whatever discovery actually finds (see
/// <see cref="SelectCharacteristics"/>), not hardcoded to one profile: KingSong/Begode/Veteran
/// share a single read+write characteristic (FFE1), InMotion V1 notifies on FFE4 and writes to
/// FFE9 (different services), and InMotion V2 sits on the Nordic UART service. UUIDs match
/// Constants.kt of the original. The rest of the connection ritual (CCCD 0x2902, discovery delay,
/// Close on drop) is unchanged.
///
/// GATT callbacks arrive on a binder thread, one at a time per connection, which is exactly what
/// the decoder needs — bytes are handed straight to DataReceived from the callback, in order,
/// without an intermediate queue.
/// </summary>
public sealed class AndroidBleClient : ITransport
{
    private static readonly Java.Util.UUID Ffe1Uuid = Java.Util.UUID.FromString("0000ffe1-0000-1000-8000-00805f9b34fb")!;
    private static readonly Java.Util.UUID Ffe4Uuid = Java.Util.UUID.FromString("0000ffe4-0000-1000-8000-00805f9b34fb")!;
    private static readonly Java.Util.UUID Ffe9Uuid = Java.Util.UUID.FromString("0000ffe9-0000-1000-8000-00805f9b34fb")!;
    private static readonly Java.Util.UUID NordicNotifyUuid = Java.Util.UUID.FromString("6e400003-b5a3-f393-e0a9-e50e24dcca9e")!;
    private static readonly Java.Util.UUID NordicWriteUuid = Java.Util.UUID.FromString("6e400002-b5a3-f393-e0a9-e50e24dcca9e")!;

    /// <summary>Client Characteristic Configuration descriptor — writing it is what actually starts notifications.</summary>
    private static readonly Java.Util.UUID CccdUuid = Java.Util.UUID.FromString("00002902-0000-1000-8000-00805f9b34fb")!;

    /// <summary>
    /// Wheels answer the first service-discovery request unreliably if it is fired the instant the
    /// link comes up — the stack is still settling connection parameters. Waiting a beat costs
    /// nothing and turns a five-second discovery timeout into a normal connect.
    /// </summary>
    private static readonly TimeSpan DiscoveryDelay = TimeSpan.FromMilliseconds(600);

    /// <summary>Long enough for connect + discovery + descriptor write, short enough to retry while the user waits.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Сколько ждём <c>OnMtuChanged</c>, прежде чем идти дальше на умолчании. Согласование MTU —
    /// не то, ради чего стоит держать человека у экрана: не ответили — подключаемся как есть, а
    /// команда, которой не хватит места, честно провалится в <see cref="BeginWrite"/>.
    /// </summary>
    private static readonly TimeSpan MtuTimeout = TimeSpan.FromSeconds(3);

    /// <summary>ATT-заголовок записи: три байта из MTU уходят на opcode и handle.</summary>
    private const int AttWriteOverhead = 3;

    /// <summary>MTU по умолчанию: 23 байта, то есть 20 байт полезной нагрузки на запись.</summary>
    private const int DefaultAttMtu = 23;

    /// <summary>
    /// Просим столько же, сколько оригинал (<c>BluetoothService.kt:178</c>). Не роскошь: команды
    /// InMotion V1 — 22 байта (16 байт тела, обязательный escape <c>A5</c> перед <c>0x55</c> в id,
    /// заголовок, контрольная и хвост), и на умолчании стек резал их до 20 молча — кадр оставался
    /// без контрольной и без <c>55 55</c>, колесо его не закрывало и не отвечало ничем, кроме
    /// «привета» BLE-модуля (разбор дампа vivo I2407 от 07.08.2026). Заодно снимает вопрос с
    /// расширенных кадров KingSong <c>0xD0</c>/<c>0xD1</c> (план 21 §6).
    /// </summary>
    private const int WantedAttMtu = 517;

    /// <summary>
    /// Pause before re-attempting a write the GATT stack refused outright ("busy" — one GATT
    /// operation runs at a time, and the wheel notifies about twenty times a second). Short: this
    /// is the failure roadmap "Пункт 9" traces the silent Beep to, and the fix is to keep the
    /// command rather than to wait long for a slot that usually opens within a frame or two.
    /// </summary>
    private static readonly TimeSpan WriteBusyRetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// How long one command may go on being refused before it is reported failed. Two seconds is
    /// forty attempts at the delay above — far past "the wheel was mid-notification", and still
    /// short enough that the rider gets an answer while their thumb is on the button. Needed
    /// because the stack's busy flag is cleared by the callback of whatever holds it, and after
    /// <see cref="WriteConfirmationTimeout"/> has given up on that callback there may be nothing
    /// left to clear it: without a deadline, every later write is refused forever, in silence.
    /// </summary>
    private static readonly TimeSpan WriteBusyDeadline = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a write waits for <c>OnCharacteristicWrite</c> after the stack accepted it before
    /// giving up. Bounds the case where the link drops between acceptance and confirmation — with
    /// no bound, that single write would stall every command queued behind it forever.
    /// </summary>
    private static readonly TimeSpan WriteConfirmationTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger<AndroidBleClient> _logger;
    private readonly BluetoothAdapter _adapter;
    private readonly SequentialWriteQueue _writeQueue;

    private BluetoothGatt? _gatt;
    private BluetoothGattCharacteristic? _notifyCharacteristic;
    private BluetoothGattCharacteristic? _writeCharacteristic;

    /// <summary>
    /// Каким типом записи говорит нынешний линк — его называет <see cref="SelectCharacteristics"/>
    /// вместе с парой характеристик. <c>volatile</c> по той же причине, что и
    /// <see cref="_attMtu"/>: пишет сессия, читают binder-потоки.
    /// </summary>
    private volatile GattWriteType _writeType = GattWriteType.NoResponse;

    private bool _disconnecting;

    /// <summary>
    /// The callback adapter for the connection currently in play. Android does not cancel callbacks
    /// already queued for delivery when <c>Close()</c> is called — a late one from a connection that
    /// has since been closed would otherwise find its own <c>ready</c> already completed and reach
    /// for <see cref="OnLinkLost"/>, tearing down the *next*, live connection (or, in
    /// <c>OnServicesDiscovered</c>, overwrite its characteristics). Comparing against this rather
    /// than the gatt object itself is deliberate: the very first Connected callback can arrive
    /// before <see cref="ConnectOnceAsync"/> has finished assigning <see cref="_gatt"/>, and a gatt
    /// comparison would reject a genuine callback in that window. The adapter, unlike
    /// <see cref="_gatt"/>, exists before the connect call is even made, so there is no such race
    /// here.
    /// <c>volatile</c> for the same reason as <see cref="_attMtu"/>: the session thread writes it,
    /// binder threads read it, and ARM's weak write ordering can otherwise show a binder thread a
    /// stale (still-non-null) value — which is precisely the bug this field exists to close, just
    /// rare and unreproducible instead of certain.
    /// </summary>
    private volatile GattCallbackAdapter? _activeCallback;

    // Согласованный MTU держится здесь, а не спрашивается у стека: getter'а на него в API нет,
    // OnMtuChanged — единственный способ узнать значение. volatile, потому что пишет binder-поток
    // колбэка, а читает поток очереди записи.
    private volatile int _attMtu = DefaultAttMtu;

    // «Команда не влезает» — свойство линка, а не команды: оно не изменится до переподключения.
    // Громко жалуемся один раз за линк, дальше только Debug из WheelService, иначе десять
    // строк ошибки в секунду похоронят настоящую причину.
    private int _tooLongReported;

    public event Action<byte[]>? DataReceived;
    public event Action? ConnectionLost;

    public AndroidBleClient(ILogger<AndroidBleClient> logger, TimeProvider timeProvider)
    {
        _logger = logger;
        _writeQueue = new SequentialWriteQueue(BeginWrite, timeProvider,
            WriteBusyRetryDelay, WriteBusyDeadline, WriteConfirmationTimeout);

        var manager = (BluetoothManager?)Application.Context.GetSystemService(Context.BluetoothService);
        _adapter = manager?.Adapter
            ?? throw new InvalidOperationException("This device has no Bluetooth adapter.");
    }

    public bool IsBluetoothEnabled => _adapter.IsEnabled;

    public async IAsyncEnumerable<DiscoveredDevice> ScanAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var scanner = _adapter.BluetoothLeScanner
            ?? throw new InvalidOperationException("Bluetooth is off — no LE scanner available.");

        var found = Channel.CreateUnbounded<DiscoveredDevice>(new UnboundedChannelOptions { SingleReader = true });
        var callback = new ScanCallbackAdapter(found.Writer, _logger);
        var settings = new ScanSettings.Builder()!
            .SetScanMode(Android.Bluetooth.LE.ScanMode.LowLatency)!
            .Build();

        scanner.StartScan(filters: null, settings: settings, callback: callback);
        try
        {
            await foreach (var device in found.Reader.ReadAllAsync(ct))
            {
                yield return device;
            }
        }
        finally
        {
            scanner.StopScan(callback);
        }
    }

    /// <summary>
    /// One attempt, and failure is reported rather than retried. Deciding whether to try again is
    /// WheelSession's job and nobody else's: when the transport retried too, one chase turned into
    /// three connects, each able to raise ConnectionLost and ask for a chase of its own.
    /// </summary>
    public Task<IReadOnlyList<DiscoveredService>> ConnectAsync(string address, CancellationToken ct = default) =>
        ConnectAsync(address, waitForWheel: false, ct);

    /// <summary>
    /// Ожидание колеса, которого сейчас может не быть рядом, — способ оригинального WheelLog
    /// (<c>autoConnectPeripheral</c>): адрес отдаётся BLE-контроллеру, и тот сам поднимает линк,
    /// когда колесо появится. Радио при этом не занято прямым поиском — до перехода на этот путь
    /// погоня за выключенным колесом держала его активным ~12 с из каждых ~17 бессрочно.
    /// </summary>
    public Task<IReadOnlyList<DiscoveredService>> WaitForWheelAsync(string address, CancellationToken ct = default) =>
        ConnectAsync(address, waitForWheel: true, ct);

    private async Task<IReadOnlyList<DiscoveredService>> ConnectAsync(string address, bool waitForWheel, CancellationToken ct)
    {
        // Android insists on upper-case MACs and throws IllegalArgumentException otherwise.
        var device = _adapter.GetRemoteDevice(address.ToUpperInvariant())
            ?? throw new InvalidOperationException($"'{address}' is not a usable Bluetooth address.");

        try
        {
            await ConnectOnceAsync(device, waitForWheel, ct);
        }
        catch
        {
            // A half-open link, a discovery that timed out, a stale service cache — whatever went
            // wrong, the GATT client it left behind is what makes the *next* attempt fail with
            // status 133, so it goes now. Cleaning up is transport hygiene, not a retry.
            await DisconnectAsync(CancellationToken.None);
            throw;
        }

        _logger.LogInformation("Ble.Connected {Mac}", address);
        return Discovered();
    }

    /// <summary>
    /// Что нашлось на колесе — службы со своими характеристиками. Транспорт только перечисляет:
    /// какое это колесо и умеем ли мы с ним говорить, решает <c>WheelDetector</c> в ядре.
    /// </summary>
    private IReadOnlyList<DiscoveredService> Discovered()
    {
        var services = _gatt?.Services;
        if (services is null) return [];

        return
        [
            .. services.Select(service => new DiscoveredService(
                service.Uuid?.ToString() ?? "",
                [.. (service.Characteristics ?? []).Select(c => c.Uuid?.ToString() ?? "")]))
        ];
    }

    private async Task ConnectOnceAsync(BluetoothDevice device, bool waitForWheel, CancellationToken ct)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new GattCallbackAdapter(this, ready, _logger);
        _activeCallback = callback;

        // Два режима, как у оригинала: autoConnect: false — подключиться сейчас и честно доложить
        // о неудаче (первая попытка, пока человек ждёт у экрана); autoConnect: true — пассивное
        // ожидание без таймаута, отменяемое только токеном (погоня за пропавшим колесом).
        _gatt = device.ConnectGatt(Application.Context, autoConnect: waitForWheel, callback, BluetoothTransports.Le)
            ?? throw new InvalidOperationException($"Could not open a GATT connection to {device.Address}.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!waitForWheel) timeout.CancelAfter(ConnectTimeout);

        using (timeout.Token.Register(() =>
        {
            if (ct.IsCancellationRequested)
            {
                ready.TrySetCanceled(ct);
            }
            else
            {
                ready.TrySetException(new TimeoutException(
                    $"The wheel did not become ready within {ConnectTimeout.TotalSeconds:F0} s."));
            }
        }))
        {
            await ready.Task;
        }
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _disconnecting = true;
        try
        {
            CloseGatt();
            _logger.LogInformation("Ble.Disconnected");
        }
        finally
        {
            _disconnecting = false;
        }

        return Task.CompletedTask;
    }

    private void CloseGatt()
    {
        var gatt = _gatt;
        _gatt = null;
        _notifyCharacteristic = null;
        _writeCharacteristic = null;
        // Следующий линк начнёт переговоры о MTU заново — унаследованное значение соврало бы о
        // размере записи ровно в том случае, когда переговоры не состоятся. Тип записи — по тому же
        // правилу: он свойство линка, а не клиента, и наследовать его нечему.
        _attMtu = DefaultAttMtu;
        _writeType = GattWriteType.NoResponse;
        Interlocked.Exchange(ref _tooLongReported, 0);

        // Обнулено до Disconnect()/Close() — колбэки этого адаптера, ещё стоящие в очереди Android,
        // после этой строки узнают себя чужими и выйдут молча (см. remark у _activeCallback).
        _activeCallback = null;

        // Порядок важен: сначала характеристики обнулены, и только потом очередь узнаёт об обрыве —
        // тогда команда, которую насос успел вынуть, гарантированно упирается в пустой линк, а не
        // уходит в радио, которого уже нет.
        _writeQueue.Abandon();

        if (gatt is null) return;

        gatt.Disconnect();
        // Close() releases the client interface. Skipping it leaks the connection slot and the
        // next connect attempt fails with status 133 for no visible reason.
        gatt.Close();
    }

    /// <summary>
    /// The link went down by itself. Android keeps the (now useless) GATT client alive until it is
    /// closed, and a stale client is exactly what makes the next connect attempt fail, so the
    /// cleanup happens here rather than waiting for someone to call DisconnectAsync.
    /// </summary>
    private void OnLinkLost()
    {
        if (_disconnecting) return;

        _logger.LogWarning("Ble.ConnectionLost");
        CloseGatt();
        ConnectionLost?.Invoke();
    }

    /// <summary>
    /// Queues the write and returns once it is actually confirmed delivered — not once it has been
    /// handed to the GATT stack. <c>WheelService.SendCommand</c> logs Cmd.Sent only after
    /// this returns, which is the whole point: before the queue, the write's return value (or the
    /// int status on API 33+) was discarded, so a command the stack refused while the wheel was
    /// mid-notification vanished with the log still claiming it went out (roadmap "Пункт 9").
    /// </summary>
    public Task WriteAsync(byte[] cmd, CancellationToken ct = default)
    {
        if (_gatt is null || _writeCharacteristic is null)
        {
            throw new WriteLinkLostException();
        }

        return _writeQueue.Enqueue(cmd);
    }

    /// <summary>
    /// The queue's one raw-write attempt. Re-reads <see cref="_gatt"/>/<see cref="_writeCharacteristic"/>
    /// rather than capturing them at <see cref="WriteAsync"/> time — a write can sit queued behind
    /// another one for a moment, and the link can drop in that window.
    /// </summary>
    private bool BeginWrite(byte[] cmd)
    {
        var gatt = _gatt;
        var characteristic = _writeCharacteristic;
        if (gatt is null || characteristic is null)
        {
            // Not "busy" — the link is gone, and it staying gone would otherwise retry forever.
            throw new WriteLinkLostException();
        }

        // Стек не отказывает в записи длиннее MTU — он молча отправляет первые MTU-3 байт. Кадр
        // приходит колесу без хвоста, колесо молчит, а в журнале стоит «Cmd.Sent». Проверка здесь
        // и нигде больше: только транспорт знает согласованный размер.
        int limit = _attMtu - AttWriteOverhead;
        if (cmd.Length > limit)
        {
            if (Interlocked.Exchange(ref _tooLongReported, 1) == 0)
            {
                _logger.LogError("Ble.WriteTooLong — команда {Length} Б не влезает в запись {Limit} Б (MTU {Mtu})",
                    cmd.Length, limit, _attMtu);
            }

            throw new WriteTooLongException(cmd.Length, limit);
        }

        // Тип записи — свойство линка (см. SelectCharacteristics), а не литерал: InMotion пишется с
        // подтверждением, остальные — без.
        var writeType = _writeType;

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            // BluetoothStatusCodes.Success == 0 (Android 33+ API contract); comparing to the
            // literal avoids depending on whether that enum is bound in this SDK level.
            int status = gatt.WriteCharacteristic(characteristic, cmd, (int)writeType);
            return status == 0;
        }

#pragma warning disable CA1422 // pre-33 API, still the only one available on the target device
        characteristic.WriteType = writeType;
        characteristic.SetValue(cmd);
        return gatt.WriteCharacteristic(characteristic);
#pragma warning restore CA1422
    }

    private void OnFrame(byte[] bytes) => DataReceived?.Invoke(bytes);

    /// <summary>
    /// Пара характеристик линка и тип записи, которым по нему говорят. Третье поле здесь потому,
    /// что тип записи — свойство выбранной пары, а не знание о протоколе: ядру и декодерам о нём
    /// знать нечего (мастер-план §8а, <c>docs/polling-architecture-review.md</c> §5.2).
    /// </summary>
    private readonly record struct CharacteristicPair(
        BluetoothGattCharacteristic Notify,
        BluetoothGattCharacteristic Write,
        GattWriteType WriteType);

    /// <summary>
    /// Picks the notify/write pair from whatever discovery found — plan 21 phase 0.1. Checked in
    /// this order because it is the only order that cannot misfire: FFE1 alone would also match
    /// under an InMotion V1 tree that additionally exposes FFE4/FFE9, but no profile in the table
    /// exposes both FFE1 and Nordic UART, so a single priority list is enough, no family lookup
    /// needed here.
    /// <para>
    /// Тип записи ложится на те же три ветви ветвь в ветвь (план 36 Л2, мастер-план §8а):
    /// <b>FFE1</b> — Begode/Veteran/KingSong, без подтверждения; <b>FFE4/FFE9</b> — InMotion V1 и
    /// <b>Nordic UART</b> — InMotion V2, <b>с подтверждением</b>. Так же делит их DarknessBot, и
    /// InMotion — единственная марка, которую он выделил (51 запись нового адаптера и 13 старого,
    /// все с подтверждением, против пяти прочих марок без). Подтверждаемая запись сама держит
    /// темп: следующая не уйдёт, пока колесо не ответило, — потому это <b>кандидат №1 в причину
    /// отвала V14</b>. Гипотеза до замера.
    /// </para>
    /// <para>
    /// <c>GattWriteType.Default</c> — это и есть запись с подтверждением (ATT Write Request); имя
    /// со словом «Response» носит противоположный тип.
    /// </para>
    /// </summary>
    private static CharacteristicPair? SelectCharacteristics(BluetoothGatt gatt)
    {
        BluetoothGattCharacteristic? Find(Java.Util.UUID uuid) =>
            (gatt.Services ?? []).Select(s => s.GetCharacteristic(uuid)).FirstOrDefault(c => c is not null);

        if (Find(Ffe1Uuid) is { } ffe1) return new CharacteristicPair(ffe1, ffe1, GattWriteType.NoResponse);

        if (Find(Ffe4Uuid) is { } ffe4 && Find(Ffe9Uuid) is { } ffe9)
        {
            return new CharacteristicPair(ffe4, ffe9, GattWriteType.Default);
        }

        if (Find(NordicNotifyUuid) is { } nordicNotify && Find(NordicWriteUuid) is { } nordicWrite)
        {
            return new CharacteristicPair(nordicNotify, nordicWrite, GattWriteType.Default);
        }

        return null;
    }

    /// <summary>
    /// Тип записи, который характеристика и вправду объявляет. Подтверждаемую запись просить можно
    /// только у той, у кого поднят <see cref="GattProperty.Write"/>: у колеса с урезанным профилем
    /// такая запись не пройдёт вовсе, и команды пропали бы молча. Не объявляет — остаёмся на записи
    /// без подтверждения и говорим об этом одной строкой.
    /// </summary>
    private static GattWriteType SupportedWriteType(BluetoothGattCharacteristic write, GattWriteType wanted, ILogger logger)
    {
        if (wanted != GattWriteType.Default || write.Properties.HasFlag(GattProperty.Write)) return wanted;

        logger.LogWarning("Ble.WriteTypeFallback — характеристика {Uuid} не объявляет запись с подтверждением, шлём без",
            write.Uuid);
        return GattWriteType.NoResponse;
    }

    /// <summary>Advertisement callback — hands devices over through a channel, deduplicated by address.</summary>
    private sealed class ScanCallbackAdapter(ChannelWriter<DiscoveredDevice> writer, ILogger logger) : ScanCallback
    {
        // A device re-announces several times a second, and its name often arrives only in a later
        // scan response — report an address once, then again if the name resolves.
        private readonly Dictionary<string, string> _reportedNames = [];

        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
        {
            string? address = result?.Device?.Address;
            if (address is null) return;

            string name = result!.Device!.Name ?? "";
            if (_reportedNames.TryGetValue(address, out string? lastName) && lastName == name) return;

            _reportedNames[address] = name;
            writer.TryWrite(new DiscoveredDevice(name, address, result.Rssi));
        }

        public override void OnScanFailed(ScanFailure errorCode)
        {
            logger.LogWarning("Scan.Failed {Error}", errorCode);
            writer.TryComplete(new InvalidOperationException($"BLE scan failed: {errorCode}"));
        }
    }

    /// <summary>
    /// Drives connect → service discovery → notification subscription, completing
    /// <paramref name="ready"/> only once frames can actually arrive.
    /// </summary>
    private sealed class GattCallbackAdapter(AndroidBleClient client, TaskCompletionSource ready, ILogger logger)
        : BluetoothGattCallback
    {
        // Спрашивали ли MTU мы сами. Часть стеков и часть колёс затевают переговоры первыми, и
        // непрошеный OnMtuChanged посреди подключения завершил бы `ready` раньше подписки — то
        // есть до того, как кадры вообще могут прийти. Ставится и читается только в колбэках,
        // а они приходят по одному в binder-потоке.
        private bool _mtuAsked;

        /// <summary>
        /// True once this adapter has been superseded — <see cref="CloseGatt"/> nulls
        /// <see cref="_activeCallback"/> before it ever tears down the gatt itself, so a callback
        /// Android still had queued for delivery lands here rather than acting on state (or,
        /// worse, characteristics) that now belong to a different, live connection.
        /// </summary>
        private bool IsStale([CallerMemberName] string? callback = null)
        {
            if (ReferenceEquals(this, client._activeCallback)) return false;

            logger.LogDebug("Ble.StaleCallback {Callback}", callback);
            return true;
        }

        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
        {
            if (IsStale()) return;

            // Число рядом с именем не для красоты: здесь Android отдаёт коды HCI, а не ATT, и
            // привязка зовёт их именами ATT. Так `8` — таймаут супервизии линка (колесо уехало,
            // выключилось, модуль перезагрузился) — читается как `InsufficientAuthorization`, и
            // разбор дампа от 07.08.2026 ушёл искать несуществующий отказ в правах.
            logger.LogInformation("Ble.ConnectionStateChanged {Status} ({Code}) {State}", status, (int)status, newState);

            if (newState == ProfileState.Connected)
            {
                // Off the callback thread and after a short pause: discovery requested from inside
                // the state-change callback, the instant the link comes up, is what the wheel was
                // failing to answer. MAUI did this with MainThread.BeginInvokeOnMainThread — the
                // native equivalent is a Handler bound to the main looper (опись §1.2).
                _ = Task.Delay(DiscoveryDelay)
                    .ContinueWith(_ => new Handler(Looper.MainLooper!).Post(() => gatt?.DiscoverServices()));
                return;
            }

            if (newState == ProfileState.Disconnected)
            {
                // Before the connection is ready this is a failed attempt the caller is awaiting;
                // afterwards it is the wheel dropping out mid-session, which nobody is awaiting and
                // which the client has to clean up after on its own.
                if (!ready.TrySetException(new InvalidOperationException($"Disconnected before becoming ready (status {status})")))
                {
                    client.OnLinkLost();
                }
            }
        }

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
        {
            if (IsStale()) return;

            logger.LogInformation("Ble.ServicesDiscovered {Status} {Count}", status, gatt?.Services?.Count ?? 0);

            var pair = gatt is null ? null : SelectCharacteristics(gatt);
            if (pair is not { Notify: var notify, Write: var write, WriteType: var writeType })
            {
                // Не отказ транспорта, а чужой профиль. Раньше здесь падало подключение, и сессия
                // принималась гоняться за устройством, с которым говорить нечем. Теперь линк
                // считается поднятым, дерево уходит наверх, и решение принимает WheelDetector: он
                // назовёт семейство (или скажет, что не узнал), а сессия отключится без повторов.
                logger.LogInformation("Ble.NoKnownProfile — ни FFE1, ни FFE4/FFE9, ни Nordic UART, решение за детектором");
                ready.TrySetResult();
                return;
            }

            client._notifyCharacteristic = notify;
            client._writeCharacteristic = write;
            client._writeType = SupportedWriteType(write, writeType, logger);
            logger.LogInformation("Ble.WriteType {WriteType}", client._writeType);
            gatt!.SetCharacteristicNotification(notify, enable: true);

            // SetCharacteristicNotification alone only opens the local side: without writing the
            // CCCD the wheel never starts sending, and the connection looks healthy but silent.
            var cccd = notify.GetDescriptor(CccdUuid);
            if (cccd is null)
            {
                ready.TrySetException(new InvalidOperationException("Characteristic FFE1 has no CCCD descriptor"));
                return;
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                gatt.WriteDescriptor(cccd, BluetoothGattDescriptor.EnableNotificationValue!.ToArray());
            }
            else
            {
#pragma warning disable CA1422 // pre-33 API, still the only one available on the target device
                cccd.SetValue(BluetoothGattDescriptor.EnableNotificationValue!.ToArray());
                gatt.WriteDescriptor(cccd);
#pragma warning restore CA1422
            }
        }

        public override void OnDescriptorWrite(BluetoothGatt? gatt, BluetoothGattDescriptor? descriptor, GattStatus status)
        {
            if (IsStale()) return;

            if (status == GattStatus.Success)
            {
                // Asks the radio for the shortest connection interval it will grant. It cannot make
                // the wheel send more often, but it removes the wait between a packet being ready
                // and the phone collecting it — which is what shows up as lag in the readings.
                bool granted = gatt?.RequestConnectionPriority(GattConnectionPriority.High) ?? false;
                logger.LogInformation("Ble.HighPriorityRequested {Granted}", granted);

                RequestMtu(gatt);
            }
            else
            {
                ready.TrySetException(new InvalidOperationException($"Enabling notifications failed (status {status})"));
            }
        }

        /// <summary>
        /// Последний шаг бутстрапа: без него запись остаётся 20-байтовой, а команды InMotion V1 —
        /// 22-байтовыми (см. <see cref="WantedAttMtu"/>). Стоит здесь, а не раньше: стек ведёт одну
        /// GATT-операцию за раз, и запрос до подтверждения CCCD столкнулся бы с ним.
        /// <para>
        /// Отказ и молчание не срывают подключение — <c>ready</c> завершается в любом случае. Это
        /// шаг бутстрапа, а он не должен становиться местом, где бутстрап встаёт: колесо с кадрами
        /// ≤ 20 байт (KingSong, Gotway) прекрасно живёт и на умолчании.
        /// </para>
        /// </summary>
        private void RequestMtu(BluetoothGatt? gatt)
        {
            if (gatt?.RequestMtu(WantedAttMtu) != true)
            {
                // Число берётся из живого поля, а не из умолчания: непрошеный OnMtuChanged мог уже
                // поднять размер, и тогда «остаёмся на 23» разошлось бы с тем, что уходит в эфир.
                logger.LogWarning("Ble.MtuRequestRefused — остаёмся на {Mtu} Б", client._attMtu);
                ready.TrySetResult();
                return;
            }

            _mtuAsked = true;

            // TrySetResult идемпотентен: кто придёт первым — ответ стека или этот срок, — тот и
            // завершит подключение. Отдельного состояния для гонки не нужно.
            //
            // Число берётся из живого поля по той же причине, что и в MtuRequestRefused: непрошеный
            // OnMtuChanged мог поднять размер до срабатывания таймера.
            _ = Task.Delay(MtuTimeout).ContinueWith(_ =>
            {
                if (ready.TrySetResult()) logger.LogWarning("Ble.MtuTimeout — ответа нет, идём на {Mtu} Б", client._attMtu);
            }, TaskScheduler.Default);
        }

        public override void OnMtuChanged(BluetoothGatt? gatt, int mtu, GattStatus status)
        {
            // Ответ прошлого линка не смеет говорить за нынешний: Close() уже поставленные в
            // очередь колбэки не отменяет. Порядок «линк оборвался → подняли новый → на нём MTU не
            // дали → прилетел чужой 517» вернул бы ровно ту поломку, против которой всё это
            // написано: 22 байта прошли бы проверку и ушли обрезанными.
            if (IsStale()) return;

            logger.LogInformation("Ble.MtuChanged {Mtu} {Status}", mtu, status);

            // Успех может прийти и с меньшим размером, чем просили, — верим стеку, но не ниже
            // гарантированного минимума.
            if (status == GattStatus.Success && mtu > DefaultAttMtu)
            {
                client._attMtu = mtu;
                // Размер записи вырос — прежняя жалоба про «не влезает» больше не о нём. Без
                // сброса опоздавший на MtuTimeout ответ оставлял бы защёлку взведённой: первый
                // промах в переходном окне съедал единственный громкий разбор, и настоящая
                // нехватка на этом линке потом шла бы только в Debug.
                Interlocked.Exchange(ref client._tooLongReported, 0);
            }

            // Размер записи верен в любом случае, а вот завершать подключение вправе только ответ
            // на наш собственный запрос — см. _mtuAsked.
            if (_mtuAsked) ready.TrySetResult();
        }

        /// <summary>
        /// Drives <see cref="SequentialWriteQueue"/>: this is the confirmation a write's acceptance
        /// promised earlier, and the only thing that lets the queue start the next command. Unlike
        /// <c>OnCharacteristicChanged</c>, this callback's signature has not changed across API
        /// levels, so one override covers both the pre-33 and 33+ write paths in <c>BeginWrite</c>.
        /// </summary>
        public override void OnCharacteristicWrite(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status)
        {
            if (IsStale()) return;

            bool success = status == GattStatus.Success;
            if (!success)
            {
                logger.LogWarning("Ble.Cmd.WriteRejected {Status}", status);
            }

            client._writeQueue.Complete(success, success ? null : new InvalidOperationException($"GATT write failed (status {status})"));
        }

        // Android 13 split this callback in two: the byte[] overload below on 33+, the one reading
        // the characteristic's own buffer on everything older. The device this app is developed
        // against runs Android 11, so both are live code.
        public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value)
        {
            if (IsStale()) return;

            client.OnFrame(value);
        }

#pragma warning disable CA1422, CS0672
        public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic)
        {
            if (IsStale()) return;

            byte[]? value = characteristic?.GetValue();
            if (value is not null)
            {
                client.OnFrame(value);
            }
        }
#pragma warning restore CA1422, CS0672
    }
}
