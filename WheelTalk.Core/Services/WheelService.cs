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
    /// <summary>
    /// Ceiling on how many protocol-initiated writes the handshake window (see
    /// <see cref="TryConsumeHandshakeLogBudget"/>) may promote to Info. A handshake that never
    /// completes is exactly the case being diagnosed, and InMotion's keep-alive re-fires every
    /// 25 ms (<c>InMotionDecoder</c>'s timer) — without a ceiling that alone would turn "no data"
    /// into an unbounded stream of Info lines instead of a short, readable trail.
    /// </summary>
    private const int HandshakeLogBudget = 20;

    private readonly ITransport _transport;
    private readonly Decoder _decoder;
    private readonly ILogger<WheelService> _logger;
    private readonly IDisposable _telemetrySubscription;
    private int _handshakeLogsRemaining = HandshakeLogBudget;

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
            if (HandshakeAwareLevel() is { } lostLevel) LogProtocolWriteAbandoned(lostLevel, Convert.ToHexString(bytes));
            return;
        }
        catch (WriteTooLongException ex)
        {
            // Тоже свойство линка, а не команды: до переподключения не изменится, и транспорт уже
            // сказал о нём один раз в полный голос.
            if (HandshakeAwareLevel() is { } longLevel) LogProtocolWriteTooLong(longLevel, Convert.ToHexString(bytes), ex.Length, ex.Limit);
            return;
        }
        catch (Exception ex)
        {
            LogProtocolWriteFailed(ex, Convert.ToHexString(bytes));
            return;
        }

        if (HandshakeAwareLevel() is { } sentLevel) LogProtocolCmdSent(sentLevel, Convert.ToHexString(bytes));
    }

    /// <summary>
    /// Каким уровнем писать эту запись — и писать ли вообще. Debug обычно, Info пока идёт окно
    /// рукопожатия (см. <see cref="TryConsumeHandshakeLogBudget"/>); <c>null</c> — уровень выключен,
    /// строки не будет.
    /// <para>
    /// Один метод вместо <c>if/else</c> на каждом месте вызова: сообщение, <c>EventId</c> и
    /// <c>EventName</c> живут только в атрибуте <c>[LoggerMessage]</c> — раздвоить их правкой одной
    /// ветки здесь уже нельзя.
    /// </para>
    /// <para>
    /// <c>null</c> нужен ради <c>Convert.ToHexString</c> на месте вызова: генератор снимет саму
    /// запись выключенного уровня, а вот шестнадцатеричную строку для неё вызывающий уже построит.
    /// Опрос InMotion идёт каждые 25 мс, и в поле это мусор, который никто не прочтёт.
    /// </para>
    /// <para>
    /// Порядок в первой ветке важен: сперва спрашиваем, включён ли Info, и только потом тратим
    /// бюджет. Иначе окно рукопожатия сгорало бы на строках, которых никто не написал.
    /// </para>
    /// </summary>
    private LogLevel? HandshakeAwareLevel()
    {
        if (_logger.IsEnabled(LogLevel.Information) && TryConsumeHandshakeLogBudget()) return LogLevel.Information;

        return _logger.IsEnabled(LogLevel.Debug) ? LogLevel.Debug : null;
    }

    /// <summary>
    /// Пока <see cref="LastSnapshot"/> пуст, разговор ещё не состоялся — 08.08.2026 разбор упёрся
    /// именно в это: по журналу на Info было не отличить «не спросили» от «спросили, а колесо не
    /// услышало». Каждая запись в этом окне стоит строки Info вместо обычного Debug; как только
    /// придёт первый снимок, окно закрывается само. Опрос по таймеру (InMotion, каждые 25 мс) не
    /// должен захлестнуть отчёт — отсюда ограниченный бюджет строк на окно.
    /// </summary>
    private bool TryConsumeHandshakeLogBudget()
    {
        if (LastSnapshot is not null) return false;
        if (_handshakeLogsRemaining <= 0) return false;

        _handshakeLogsRemaining--;
        return true;
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

    /// <summary>Level is a parameter, not fixed to Debug: <see cref="HandshakeAwareLevel"/> promotes
    /// this to Info during the handshake window — one message definition either way.</summary>
    [LoggerMessage(EventId = LogEvents.Service.ProtocolWriteAbandonedId, EventName = LogEvents.Service.ProtocolWriteAbandonedName,
        Message = "Protocol-initiated write abandoned — link gone {Hex}")]
    private partial void LogProtocolWriteAbandoned(LogLevel level, string hex);

    /// <summary>Level is a parameter for the same reason as <see cref="LogProtocolWriteAbandoned"/>.</summary>
    [LoggerMessage(EventId = LogEvents.Service.ProtocolWriteTooLongId, EventName = LogEvents.Service.ProtocolWriteTooLongName,
        Message = "Protocol-initiated write does not fit ({Length} B > {Limit} B) {Hex}")]
    private partial void LogProtocolWriteTooLong(LogLevel level, string hex, int length, int limit);

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
    /// user-initiated one, just from a different origin (no <see cref="WheelCommand"/> to report).
    /// Level is a parameter for the same reason as <see cref="LogProtocolWriteAbandoned"/>.</summary>
    [LoggerMessage(EventId = LogEvents.Service.CmdSentProtocolId, EventName = LogEvents.Service.CmdSentProtocolName,
        Message = "Cmd.Sent {Hex}")]
    private partial void LogProtocolCmdSent(LogLevel level, string hex);
}
