using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Ports;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace WheelTalk.Ble;

/// <summary>
/// ITransport implementation over Windows.Devices.Bluetooth (WinRT) — native Windows BLE
/// central, no external dependencies. No pairing/bonding is used or required — WheelLog talks to
/// these wheels by connecting directly to the address.
///
/// The notify/write characteristic pair is picked from whatever discovery finds (see
/// <see cref="SelectCharacteristicsAsync"/>), not hardcoded to one profile — same table as
/// <c>AndroidBleClient</c> (plan 21 phase 0.1): KingSong/Begode/Veteran share a single read+write
/// characteristic (FFE1), InMotion V1 notifies on FFE4 and writes to FFE9 (different services),
/// InMotion V2 sits on the Nordic UART service.
///
/// Begode wheels (e.g. MTen3) run an older Bluetooth stack than Veteran/Sherman L, and Windows'
/// WinRT BLE central — tuned around BLE 5 peripherals — is noticeably flakier against them:
/// GATT service/characteristic discovery right after connect can transiently fail, and Windows
/// sometimes tears down what it considers an idle link. Two mitigations below address this:
/// pinning a <see cref="GattSession"/> open (MaintainConnection = true) so Windows doesn't drop
/// the connection on its own initiative, and retrying GATT discovery a few times before giving up.
/// </summary>
public sealed class WindowsBleClient : ITransport, IAsyncDisposable
{
    private static readonly Guid _ffe1Uuid = Guid.Parse("0000ffe1-0000-1000-8000-00805f9b34fb");
    private static readonly Guid _ffe4Uuid = Guid.Parse("0000ffe4-0000-1000-8000-00805f9b34fb");
    private static readonly Guid _ffe9Uuid = Guid.Parse("0000ffe9-0000-1000-8000-00805f9b34fb");
    private static readonly Guid _nordicNotifyUuid = Guid.Parse("6e400003-b5a3-f393-e0a9-e50e24dcca9e");
    private static readonly Guid _nordicWriteUuid = Guid.Parse("6e400002-b5a3-f393-e0a9-e50e24dcca9e");
    private const int MaxGattAttempts = 3;
    private static readonly TimeSpan _gattRetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<WindowsBleClient> _logger;
    private BluetoothLEDevice? _device;
    private GattSession? _gattSession;
    private GattDeviceService? _notifyService;
    private GattDeviceService? _writeService;
    private GattCharacteristic? _notifyCharacteristic;
    private GattCharacteristic? _writeCharacteristic;
    private bool _disconnecting;

    public event Action<byte[]>? DataReceived;
    public event Action? ConnectionLost;

    public WindowsBleClient(ILogger<WindowsBleClient> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Converts a colon/dash-separated MAC string ("D4:5A:...") to WinRT's 48-bit ulong address.
    /// This is where WheelAddress's format is checked — on connect, not at startup, so that
    /// scanning (which is how you obtain the MAC in the first place) works with the setting still
    /// empty or half-filled.
    /// </summary>
    /// <exception cref="ArgumentException">Not 12 hex digits' worth of MAC.</exception>
    public static ulong MacToAddress(string mac)
    {
        string hex = mac.Replace(":", "").Replace("-", "");
        if (hex.Length != 12 || !ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong address))
        {
            throw new ArgumentException(
                $"'{mac}' is not a MAC address — expected 12 hex digits like \"88:25:83:F2:1A:98\". " +
                "Run the Scan scenario and copy an address from its output into WheelTalk:WheelAddress.",
                nameof(mac));
        }

        return address;
    }

    /// <summary>Converts a WinRT 48-bit ulong address back to a colon-separated MAC string.</summary>
    public static string AddressToMac(ulong address)
    {
        var bytes = new byte[6];
        for (int i = 5; i >= 0; i--)
        {
            bytes[i] = (byte)(address & 0xFF);
            address >>= 8;
        }
        return string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    /// <summary>
    /// Scans the environment until cancelled, yielding each peripheral seen. The watcher callback
    /// hands devices over through a channel rather than yielding directly — WinRT raises Received
    /// on its own thread whether or not anyone is currently pulling on the enumerator.
    /// </summary>
    public async IAsyncEnumerable<DiscoveredDevice> ScanAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
        };
        var found = Channel.CreateUnbounded<DiscoveredDevice>(new UnboundedChannelOptions { SingleReader = true });

        // Advertisement (ADV_IND) and scan response (SCAN_RSP) arrive as separate Received
        // events for the same address, and only one of them typically carries LocalName —
        // remember the last non-empty name per address so both events report it.
        var knownNames = new Dictionary<ulong, string>();

        // A given device re-announces several times a second; report an address the first time
        // it is seen, and again if its name resolves later — otherwise it's just the same info
        // repeating.
        var reportedNames = new Dictionary<ulong, string>();

        watcher.Received += (_, args) =>
        {
            string? liveName = args.Advertisement.LocalName;
            if (!string.IsNullOrEmpty(liveName))
            {
                knownNames[args.BluetoothAddress] = liveName;
            }

            string name = knownNames.TryGetValue(args.BluetoothAddress, out var known) ? known : "";

            bool alreadyReported = reportedNames.TryGetValue(args.BluetoothAddress, out var lastReportedName);
            if (alreadyReported && lastReportedName == name)
            {
                return;
            }

            reportedNames[args.BluetoothAddress] = name;
            found.Writer.TryWrite(new DiscoveredDevice(name, AddressToMac(args.BluetoothAddress), args.RawSignalStrengthInDBm));
        };

        watcher.Start();
        try
        {
            await foreach (var device in found.Reader.ReadAllAsync(ct))
            {
                yield return device;
            }
        }
        finally
        {
            watcher.Stop();
        }
    }

    /// <summary>
    /// Connects directly to the wheel's address (no pairing), resolves the FFE0 service and
    /// FFE1 characteristic, and subscribes to notifications. Pins a GattSession open and
    /// retries GATT discovery a few times — see the class remarks on why this matters for
    /// older-Bluetooth-stack wheels like Begode's.
    /// </summary>
    /// <remarks>
    /// Дерева служб не возвращает — пустой список. Этот клиент ищет сразу FFE0/FFE1 и всего дерева
    /// не перечисляет, а опознание семейства (<c>WheelDetector</c>) нужно <c>WheelSession</c>,
    /// которой на Windows нет: консольная отладка ходит в транспорт напрямую. Появится сессия —
    /// понадобится и перечисление, это <c>GetGattServicesAsync</c> вместо запроса по одному UUID.
    /// </remarks>
    public async Task<IReadOnlyList<DiscoveredService>> ConnectAsync(string address, CancellationToken ct = default)
    {
        ulong btAddress = MacToAddress(address);
        _device = await BluetoothLEDevice.FromBluetoothAddressAsync(btAddress).AsTask(ct)
            ?? throw new InvalidOperationException(
                $"Could not resolve BLE device for address {address}. If this keeps happening on an " +
                "older BLE 4.x wheel, run Scan() first so Windows has recently seen the address.");

        _device.ConnectionStatusChanged += (sender, _) =>
        {
            _logger.LogInformation("Ble.ConnectionStatusChanged {Status}", sender.ConnectionStatus);
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected && !_disconnecting)
            {
                ConnectionLost?.Invoke();
            }
        };

        try
        {
            _gattSession = await GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId).AsTask(ct);
            _gattSession.MaintainConnection = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not pin a GattSession open — continuing without it");
        }

        var servicesResult = await RetryGattAsync(
            () => _device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(ct),
            r => r.Status, "GetGattServicesAsync", ct);
        if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
        {
            throw new InvalidOperationException($"GATT service discovery found nothing after {MaxGattAttempts} attempts (status: {servicesResult.Status})");
        }

        var pair = await SelectCharacteristicsAsync(servicesResult.Services, ct)
            ?? throw new InvalidOperationException(
                "No known characteristic profile found (FFE1, or FFE4+FFE9, or Nordic UART) — this wheel's GATT tree doesn't match any ported protocol.");

        // Not `using` — disposing a GattDeviceService while its GattCharacteristic is still in use
        // invalidates that characteristic on Begode's older BLE stack (WriteAsync/notify teardown
        // then throw ObjectDisposedException). The pair's services are kept alive until
        // DisconnectAsync; every other discovered service is done with immediately.
        _notifyService = pair.NotifyService;
        _writeService = pair.WriteService;
        foreach (var service in servicesResult.Services)
        {
            if (service != _notifyService && service != _writeService) service.Dispose();
        }

        _notifyCharacteristic = pair.Notify;
        _writeCharacteristic = pair.Write;
        _notifyCharacteristic.ValueChanged += OnCharacteristicValueChanged;

        var notifyStatus = await RetryGattAsync(
            () => _notifyCharacteristic
                .WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)
                .AsTask(ct),
            s => s, "WriteClientCharacteristicConfigurationDescriptorAsync(Notify)", ct);
        if (notifyStatus != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Failed to enable notify after {MaxGattAttempts} attempts (status: {notifyStatus})");
        }

        _logger.LogInformation("Ble.Connected {Mac}", address);
        return [];
    }

    private readonly record struct CharacteristicPair(
        GattDeviceService NotifyService, GattCharacteristic Notify,
        GattDeviceService WriteService, GattCharacteristic Write);

    /// <summary>
    /// Picks the notify/write pair from the discovered service tree — plan 21 phase 0.1, same
    /// priority order as <c>AndroidBleClient.SelectCharacteristics</c>: FFE1 first (it alone would
    /// also match under an InMotion V1 tree that additionally exposes FFE4/FFE9, but no profile in
    /// the table exposes both FFE1 and Nordic UART, so a single priority list is enough).
    /// </summary>
    private async Task<CharacteristicPair?> SelectCharacteristicsAsync(IReadOnlyList<GattDeviceService> services, CancellationToken ct)
    {
        var byUuid = new Dictionary<Guid, (GattDeviceService Service, GattCharacteristic Characteristic)>();
        foreach (var service in services)
        {
            var result = await RetryGattAsync(
                () => service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(ct),
                r => r.Status, $"GetCharacteristicsAsync({service.Uuid})", ct);
            if (result.Status != GattCommunicationStatus.Success) continue;

            foreach (var characteristic in result.Characteristics)
            {
                byUuid.TryAdd(characteristic.Uuid, (service, characteristic));
            }
        }

        if (byUuid.TryGetValue(_ffe1Uuid, out var ffe1))
        {
            return new CharacteristicPair(ffe1.Service, ffe1.Characteristic, ffe1.Service, ffe1.Characteristic);
        }

        if (byUuid.TryGetValue(_ffe4Uuid, out var ffe4) && byUuid.TryGetValue(_ffe9Uuid, out var ffe9))
        {
            return new CharacteristicPair(ffe4.Service, ffe4.Characteristic, ffe9.Service, ffe9.Characteristic);
        }

        if (byUuid.TryGetValue(_nordicNotifyUuid, out var notify) && byUuid.TryGetValue(_nordicWriteUuid, out var write))
        {
            return new CharacteristicPair(notify.Service, notify.Characteristic, write.Service, write.Characteristic);
        }

        return null;
    }

    /// <summary>
    /// Retries a GATT operation up to <see cref="MaxGattAttempts"/> times when it completes
    /// without throwing but reports a non-Success status — the common failure mode against
    /// older/simpler BLE peripherals right after connecting.
    /// </summary>
    private async Task<T> RetryGattAsync<T>(Func<Task<T>> action, Func<T, GattCommunicationStatus> statusOf,
        string what, CancellationToken ct)
    {
        T result = await action();
        for (int attempt = 1; attempt < MaxGattAttempts && statusOf(result) != GattCommunicationStatus.Success; attempt++)
        {
            _logger.LogWarning("{What} attempt {Attempt}/{Max} returned {Status}, retrying", what, attempt, MaxGattAttempts, statusOf(result));
            await Task.Delay(_gattRetryDelay, ct);
            result = await action();
        }
        return result;
    }

    private void OnCharacteristicValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var reader = DataReader.FromBuffer(args.CharacteristicValue);
        var bytes = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(bytes);
        DataReceived?.Invoke(bytes);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        _disconnecting = true;
        if (_notifyCharacteristic is not null)
        {
            _notifyCharacteristic.ValueChanged -= OnCharacteristicValueChanged;
            try
            {
                await _notifyCharacteristic
                    .WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None)
                    .AsTask(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to disable notify during disconnect (device likely already gone)");
            }
            _notifyCharacteristic = null;
        }
        _writeCharacteristic = null;

        if (_writeService != _notifyService) _writeService?.Dispose();
        _writeService = null;
        _notifyService?.Dispose();
        _notifyService = null;

        _gattSession?.Dispose();
        _gattSession = null;

        _device?.Dispose();
        _device = null;
        _disconnecting = false;
        _logger.LogInformation("Ble.Disconnected");
    }

    public async Task WriteAsync(byte[] cmd, CancellationToken ct = default)
    {
        if (_writeCharacteristic is null)
        {
            throw new InvalidOperationException("Not connected — call ConnectAsync first");
        }

        using var writer = new DataWriter();
        writer.WriteBytes(cmd);
        var status = await _writeCharacteristic
            .WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse)
            .AsTask(ct);
        if (status != GattCommunicationStatus.Success)
        {
            _logger.LogWarning("Cmd.WriteFailed {Status} {Hex}", status, Convert.ToHexString(cmd));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
