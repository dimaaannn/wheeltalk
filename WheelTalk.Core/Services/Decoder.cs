using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Diagnostics;
using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Services;

/// <summary>
/// Wraps a protocol-specific <see cref="IWheelDecoder"/> + <see cref="WheelState"/> — accepts
/// raw bytes only, isolated from BLE (mirrors WheelData.decodeResponse's role as the byte-in
/// entry point). Protocol-agnostic: the composition root picks which <see cref="IWheelDecoder"/>
/// to construct (VeteranDecoder, GotwayDecoder, …) against a shared WheelState.
/// </summary>
public sealed partial class Decoder
{
    private readonly WheelState _state;
    private readonly IEventSink _eventSink;
    private readonly ILogger<Decoder> _logger;
    private readonly Subject<TelemetrySnapshot> _telemetry = new();

    /// <summary>
    /// Every successfully decoded snapshot, in arrival order. Emissions happen on whatever thread
    /// fed the bytes in (the BLE notification callback) — subscribers that touch a UI must marshal
    /// themselves, and slow subscribers stall the decode loop, so throttle with Rx operators
    /// rather than doing work per frame.
    /// </summary>
    public IObservable<TelemetrySnapshot> Telemetry => _telemetry;

    public IWheelDecoder ProtocolDecoder { get; }

    public Decoder(WheelState state, IWheelDecoder protocolDecoder, IEventSink eventSink, ILogger<Decoder> logger)
    {
        _state = state;
        ProtocolDecoder = protocolDecoder;
        _eventSink = eventSink;
        _logger = logger;
    }

    /// <summary>See <see cref="WheelState.ResetPeaks"/> — the only reset the "сброс максимумов" button needs.</summary>
    public void ResetPeaks() => _state.ResetPeaks();

    public void Feed(byte[] bytes)
    {
        // Hex-dumping every incoming frame is a hot path; skip the eager Convert.ToHexString
        // allocation entirely unless Trace is actually enabled (LoggerMessage source-gen only
        // guards the final write, not the arguments the caller computes before calling it).
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            LogFrameReceived(Convert.ToHexString(bytes), bytes.Length);
        }
        if (!ProtocolDecoder.Decode(bytes)) return;

        var snapshot = _state.ToSnapshot();
        LogFrameDecoded(snapshot);
        _telemetry.OnNext(snapshot);
        _eventSink.Publish(new WheelEvent.WheelDataAvailable(snapshot));
    }

    [LoggerMessage(EventId = LogEvents.Decoding.FrameReceivedId, EventName = LogEvents.Decoding.FrameReceivedName,
        Level = LogLevel.Trace, Message = "Frame.Received {Hex} ({Len} bytes)")]
    private partial void LogFrameReceived(string hex, int len);

    [LoggerMessage(EventId = LogEvents.Decoding.FrameDecodedId, EventName = LogEvents.Decoding.FrameDecodedName,
        Level = LogLevel.Trace, Message = "Frame.Decoded {Snapshot}")]
    private partial void LogFrameDecoded(TelemetrySnapshot snapshot);
}
