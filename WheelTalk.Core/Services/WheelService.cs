using Microsoft.Extensions.Logging;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Diagnostics;
using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Services;

/// <summary>
/// Upper-boundary orchestrator: wires ITransport.DataReceived into Decoder.Feed, and turns
/// WheelCommand / convenience calls into bytes written back through ITransport. Also relays
/// the protocol decoder's own initiative writes (Decoder.ProtocolDecoder.WriteRequested —
/// Begode's handshake polling, delayed two-step commands) so callers never need to know which
/// protocol is active. Registered as part of the DI container (see <c>AddWheelBusinessLogic</c>)
/// and resolved from Program.Main's composition root rather than constructed by hand.
/// </summary>
public sealed partial class WheelService : IDisposable
{
    private readonly ITransport _transport;
    private readonly Decoder _decoder;
    private readonly ILogger<WheelService> _logger;
    private readonly IDisposable _telemetrySubscription;

    /// <summary>
    /// Most recent snapshot, for callers that need a value the moment they appear (a screen being
    /// opened mid-ride) instead of waiting for the next frame. Null until the first decode.
    /// </summary>
    public TelemetrySnapshot? LastSnapshot { get; private set; }

    /// <summary>Decoded telemetry as a stream — see <see cref="Decoder.Telemetry"/> for threading.</summary>
    public IObservable<TelemetrySnapshot> Telemetry => _decoder.Telemetry;

    public WheelService(ITransport transport, Decoder decoder, ILogger<WheelService> logger)
    {
        _transport = transport;
        _decoder = decoder;
        _logger = logger;

        transport.DataReceived += OnDataReceived;
        _telemetrySubscription = decoder.Telemetry.Subscribe(snapshot => LastSnapshot = snapshot);
        decoder.ProtocolDecoder.WriteRequested += OnProtocolWriteRequested;
    }

    private void OnDataReceived(byte[] bytes) => _decoder.Feed(bytes);

    /// <summary>
    /// Detaches from the transport. A reconnect builds a fresh service around a fresh decoder
    /// (wheel state carries no reset), and the transport outlives both — so without this the old
    /// service would keep feeding its stale decoder alongside the new one.
    /// </summary>
    public void Dispose()
    {
        _transport.DataReceived -= OnDataReceived;
        _decoder.ProtocolDecoder.WriteRequested -= OnProtocolWriteRequested;
        _telemetrySubscription.Dispose();

        // Most decoders are stateless beyond WheelState and need nothing here — InMotion is the
        // first that owns a real OS resource (its keep-alive ITimer, ticking for the life of the
        // connection rather than just at bootstrap like Gotway/KingSong's handshakes), so it
        // implements IDisposable. A leaked timer would keep firing and writing to a transport
        // nobody is listening to anymore until the process reclaims it.
        if (_decoder.ProtocolDecoder is IDisposable disposableDecoder) disposableDecoder.Dispose();
    }

    private void OnProtocolWriteRequested(byte[] bytes) => _ = WriteSafe(bytes);

    /// <summary>
    /// Relays a decoder-initiated write (Begode's "V"/"N" handshake polling, or the delayed second
    /// half of a two-step command) through the same transport — and so the same single-flight queue
    /// on Android — as user commands. Cmd.Sent is logged here, after the await, for the same reason
    /// as in <see cref="SendCommand"/>: this is the only place that actually knows the write was
    /// confirmed, and the decoder that requested it has no way to know that.
    /// </summary>
    private async Task WriteSafe(byte[] bytes)
    {
        try
        {
            await _transport.WriteAsync(bytes);
        }
        catch (WriteLinkLostException)
        {
            // Не поломка, а следствие обрыва — и обрыв уже записан тем, кто его заметил. Без этой
            // ветки каждый такт опроса ложился в журнал ошибкой с трассировкой: 02.08.2026 такая
            // строка стояла ровно там, где читающий ищет причину обрыва, и была не причиной.
            LogProtocolWriteAbandoned(Convert.ToHexString(bytes));
            return;
        }
        catch (Exception ex)
        {
            LogProtocolWriteFailed(ex, Convert.ToHexString(bytes));
            return;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            LogProtocolCmdSent(Convert.ToHexString(bytes));
        }
    }

    public async Task SendCommand(WheelCommand cmd, CancellationToken ct = default)
    {
        var protocolDecoder = _decoder.ProtocolDecoder;
        byte[]? bytes = cmd switch
        {
            WheelCommand.Beep => protocolDecoder.BuildWheelBeep(),
            WheelCommand.SetLight l => protocolDecoder.BuildSetLightState(l.Enabled),
            WheelCommand.SwitchFlashlight => protocolDecoder.BuildSwitchFlashlight(),
            WheelCommand.SetPedalsMode p => protocolDecoder.BuildUpdatePedalsMode(p.Mode),
            WheelCommand.ResetTrip => protocolDecoder.BuildResetTrip(),
            WheelCommand.Calibrate => protocolDecoder.BuildCalibrate(),
            _ => null,
        };

        if (bytes is null)
        {
            LogCmdSkipped(cmd);
            return;
        }

        // Cmd.Sent means delivered, not "asked to be sent" — a transport that only accepts the
        // write locally (AndroidBleClient's queue, confirmed through OnCharacteristicWrite) makes
        // that distinction real; one that reports success unconditionally (WindowsBleClient today)
        // makes it a no-op. Either way, the log must not claim delivery before WriteAsync says so:
        // logging first and writing second is exactly how "Cmd.Sent Beep" ended up in the log for
        // a beep the wheel never received (roadmap "Пункт 9").
        try
        {
            await _transport.WriteAsync(bytes, ct);
        }
        catch (Exception ex)
        {
            LogCmdFailed(ex, cmd, Convert.ToHexString(bytes));
            throw;
        }

        // The hex dump itself is still only worth formatting if something will consume it.
        if (_logger.IsEnabled(LogLevel.Information))
        {
            LogCmdSent(cmd, Convert.ToHexString(bytes));
        }
    }

    /// <summary>«Сброс максимумов» — see <see cref="Decoder.ResetPeaks"/>. Purely local, no BLE write.</summary>
    public void ResetPeaks() => _decoder.ResetPeaks();

    public Task SetLight(bool enabled, CancellationToken ct = default) => SendCommand(new WheelCommand.SetLight(enabled), ct);
    public Task Beep(CancellationToken ct = default) => SendCommand(new WheelCommand.Beep(), ct);
    public Task SetPedalsMode(int mode, CancellationToken ct = default) => SendCommand(new WheelCommand.SetPedalsMode(mode), ct);
    public Task ResetTrip(CancellationToken ct = default) => SendCommand(new WheelCommand.ResetTrip(), ct);
    public Task Calibrate(CancellationToken ct = default) => SendCommand(new WheelCommand.Calibrate(), ct);

    [LoggerMessage(EventId = LogEvents.Service.ProtocolWriteFailedId, EventName = LogEvents.Service.ProtocolWriteFailedName,
        Level = LogLevel.Error, Message = "Protocol-initiated write failed {Hex}")]
    private partial void LogProtocolWriteFailed(Exception ex, string hex);

    [LoggerMessage(EventId = LogEvents.Service.ProtocolWriteAbandonedId, EventName = LogEvents.Service.ProtocolWriteAbandonedName,
        Level = LogLevel.Debug, Message = "Protocol-initiated write abandoned — link gone {Hex}")]
    private partial void LogProtocolWriteAbandoned(string hex);

    [LoggerMessage(EventId = LogEvents.Service.CmdSkippedId, EventName = LogEvents.Service.CmdSkippedName,
        Level = LogLevel.Warning, Message = "Cmd.Skipped {Command} (no-op for the active protocol)")]
    private partial void LogCmdSkipped(WheelCommand command);

    [LoggerMessage(EventId = LogEvents.Service.CmdSentId, EventName = LogEvents.Service.CmdSentName,
        Level = LogLevel.Information, Message = "Cmd.Sent {Command} {Hex}")]
    private partial void LogCmdSent(WheelCommand command, string hex);

    [LoggerMessage(EventId = LogEvents.Service.CmdFailedId, EventName = LogEvents.Service.CmdFailedName,
        Level = LogLevel.Warning, Message = "Cmd.Failed {Command} {Hex}")]
    private partial void LogCmdFailed(Exception ex, WheelCommand command, string hex);

    /// <summary>Same event identity as <see cref="LogCmdSent"/> — LogEvents.Service.CmdSentId's own
    /// remark documents this: a decoder-initiated write is the same conceptual event as a
    /// user-initiated one, just from a different origin (no <see cref="WheelCommand"/> to report).</summary>
    [LoggerMessage(EventId = LogEvents.Service.CmdSentProtocolId, EventName = LogEvents.Service.CmdSentProtocolName,
        Level = LogLevel.Debug, Message = "Cmd.Sent {Hex}")]
    private partial void LogProtocolCmdSent(string hex);
}
