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
    private bool _disconnecting;

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
            throw new InvalidOperationException("Not connected — call ConnectAsync first");
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
            throw new InvalidOperationException("Link dropped before the queued write reached the radio");
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            // BluetoothStatusCodes.Success == 0 (Android 33+ API contract); comparing to the
            // literal avoids depending on whether that enum is bound in this SDK level.
            int status = gatt.WriteCharacteristic(characteristic, cmd, (int)GattWriteType.NoResponse);
            return status == 0;
        }

#pragma warning disable CA1422 // pre-33 API, still the only one available on the target device
        characteristic.WriteType = GattWriteType.NoResponse;
        characteristic.SetValue(cmd);
        return gatt.WriteCharacteristic(characteristic);
#pragma warning restore CA1422
    }

    private void OnFrame(byte[] bytes) => DataReceived?.Invoke(bytes);

    private readonly record struct CharacteristicPair(BluetoothGattCharacteristic Notify, BluetoothGattCharacteristic Write);

    /// <summary>
    /// Picks the notify/write pair from whatever discovery found — plan 21 phase 0.1. Checked in
    /// this order because it is the only order that cannot misfire: FFE1 alone would also match
    /// under an InMotion V1 tree that additionally exposes FFE4/FFE9, but no profile in the table
    /// exposes both FFE1 and Nordic UART, so a single priority list is enough, no family lookup
    /// needed here.
    /// </summary>
    private static CharacteristicPair? SelectCharacteristics(BluetoothGatt gatt)
    {
        BluetoothGattCharacteristic? Find(Java.Util.UUID uuid) =>
            (gatt.Services ?? []).Select(s => s.GetCharacteristic(uuid)).FirstOrDefault(c => c is not null);

        if (Find(Ffe1Uuid) is { } ffe1) return new CharacteristicPair(ffe1, ffe1);

        if (Find(Ffe4Uuid) is { } ffe4 && Find(Ffe9Uuid) is { } ffe9) return new CharacteristicPair(ffe4, ffe9);

        if (Find(NordicNotifyUuid) is { } nordicNotify && Find(NordicWriteUuid) is { } nordicWrite)
        {
            return new CharacteristicPair(nordicNotify, nordicWrite);
        }

        return null;
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
        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
        {
            logger.LogInformation("Ble.ConnectionStateChanged {Status} {State}", status, newState);

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
            logger.LogInformation("Ble.ServicesDiscovered {Status} {Count}", status, gatt?.Services?.Count ?? 0);

            var pair = gatt is null ? null : SelectCharacteristics(gatt);
            if (pair is not { Notify: var notify, Write: var write })
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
            if (status == GattStatus.Success)
            {
                // Asks the radio for the shortest connection interval it will grant. It cannot make
                // the wheel send more often, but it removes the wait between a packet being ready
                // and the phone collecting it — which is what shows up as lag in the readings.
                bool granted = gatt?.RequestConnectionPriority(GattConnectionPriority.High) ?? false;
                logger.LogInformation("Ble.HighPriorityRequested {Granted}", granted);

                ready.TrySetResult();
            }
            else
            {
                ready.TrySetException(new InvalidOperationException($"Enabling notifications failed (status {status})"));
            }
        }

        /// <summary>
        /// Drives <see cref="SequentialWriteQueue"/>: this is the confirmation a write's acceptance
        /// promised earlier, and the only thing that lets the queue start the next command. Unlike
        /// <c>OnCharacteristicChanged</c>, this callback's signature has not changed across API
        /// levels, so one override covers both the pre-33 and 33+ write paths in <c>BeginWrite</c>.
        /// </summary>
        public override void OnCharacteristicWrite(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status)
        {
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
            client.OnFrame(value);
        }

#pragma warning disable CA1422, CS0672
        public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic)
        {
            byte[]? value = characteristic?.GetValue();
            if (value is not null)
            {
                client.OnFrame(value);
            }
        }
#pragma warning restore CA1422, CS0672
    }
}
