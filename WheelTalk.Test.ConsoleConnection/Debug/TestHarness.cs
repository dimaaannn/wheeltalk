using Microsoft.Extensions.Logging;
using WheelTalk.Ble;
using WheelTalk.Core.Playback;
using WheelTalk.Core.Services;

namespace WheelTalk.Debug;

/// <summary>
/// High-level manual-test scenarios (§7 of the port plan), built once by Program.Build()
/// and invoked one at a time by uncommenting a call in Main.
/// </summary>
public sealed class TestHarness
{
    private readonly WindowsBleClient _bleClient;
    private readonly WheelService _wheelService;
    private readonly Decoder _decoder;
    private readonly ConsolePresenter _presenter;
    private readonly ILogger<TestHarness> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _wheelAddress;

    public TestHarness(WindowsBleClient bleClient, WheelService wheelService, Decoder decoder,
        ConsolePresenter presenter, ILoggerFactory loggerFactory, string wheelAddress)
    {
        _bleClient = bleClient;
        _wheelService = wheelService;
        _decoder = decoder;
        _presenter = presenter;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TestHarness>();
        _wheelAddress = wheelAddress;
    }

    /// <summary>
    /// Scenario 1 — scan the environment until Ctrl-C, printing each device found (Name + MAC
    /// ready to paste into appsettings.json + RSSI).
    /// </summary>
    public async Task Scan(CancellationToken ct)
    {
        _logger.LogInformation("Scan started — Ctrl-C to stop");
        try
        {
            await foreach (var device in _bleClient.ScanAsync(ct))
            {
                _logger.LogInformation("Scan.DeviceFound {Name} {Mac} {Rssi} dBm",
                    device.Name.Length == 0 ? "(unnamed)" : device.Name, device.Address, device.Rssi);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on Ctrl-C
        }
    }

    /// <summary>Scenario 2 — connect, dump every raw incoming frame as hex, then disconnect.</summary>
    public async Task RawDump(CancellationToken ct)
    {
        RequireWheelAddress();
        void OnRaw(byte[] bytes) => _logger.LogInformation("Frame.Received {Hex} ({Len} bytes)", Convert.ToHexString(bytes), bytes.Length);

        _bleClient.DataReceived += OnRaw;
        try
        {
            await _bleClient.ConnectAsync(_wheelAddress, ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        catch (OperationCanceledException)
        {
            // expected on Ctrl-C
        }
        finally
        {
            _bleClient.DataReceived -= OnRaw;
            await _bleClient.DisconnectAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Scenario 3 — connect and watch live telemetry until Ctrl-C. The line itself is drawn by
    /// <see cref="ConsolePresenter"/> (subscribed for every scenario), so there is nothing to
    /// print here.
    /// </summary>
    public async Task LiveSpeedPwmVoltage(CancellationToken ct)
    {
        RequireWheelAddress();
        try
        {
            await _bleClient.ConnectAsync(_wheelAddress, ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        catch (OperationCanceledException)
        {
            // expected on Ctrl-C
        }
        finally
        {
            await _bleClient.DisconnectAsync(CancellationToken.None);
        }
    }

    /// <summary>Scenario 4 — connect, turn the headlight on, pause, disconnect.</summary>
    public async Task HeadlightOn(CancellationToken ct)
    {
        RequireWheelAddress();
        await _bleClient.ConnectAsync(_wheelAddress, ct);
        try
        {
            await _wheelService.SetLight(true, ct);
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
        finally
        {
            await _bleClient.DisconnectAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Scenario 5 — record decoded telemetry to a CSV fixture file until Ctrl-C. Ride the wheel
    /// back and forth while it runs, then stop with Ctrl-C; the CSV (raw fixed-point fields per
    /// TelemetrySnapshot, one row per decoded snapshot, no throttling) is meant as recorded input
    /// for future decoder unit tests.
    /// </summary>
    public async Task RecordTelemetryCsv(CancellationToken ct)
    {
        RequireWheelAddress();
        string path = $"logs/telemetry_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        using var csv = new TelemetryCsvWriter(path);

        using var subscription = _wheelService.Telemetry.Subscribe(csv.WriteRow);
        _logger.LogInformation("Recording.Started {Path} — ride the wheel, Ctrl-C to stop", path);
        try
        {
            await _bleClient.ConnectAsync(_wheelAddress, ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        catch (OperationCanceledException)
        {
            // expected on Ctrl-C
        }
        finally
        {
            await _bleClient.DisconnectAsync(CancellationToken.None);
            _logger.LogInformation("Recording.Stopped {Rows} rows written to {Path}", csv.RowsWritten, path);
        }
    }

    /// <summary>Offline decoder check — replays a RAW_*.csv through Decoder.Feed, no BLE involved.</summary>
    public async Task ReplayRawFile(string path, CancellationToken ct)
    {
        var replay = new ReplayTransport(
            () => new StreamReader(path), TimeProvider.System, _loggerFactory.CreateLogger<ReplayTransport>());
        replay.DataReceived += _decoder.Feed;
        await replay.PlayAsync(realtime: true, ct);
    }

    private void RequireWheelAddress()
    {
        if (string.IsNullOrWhiteSpace(_wheelAddress))
        {
            throw new InvalidOperationException("WheelAddress is empty in appsettings.json — run Scan() first and paste the MAC in.");
        }
    }
}
