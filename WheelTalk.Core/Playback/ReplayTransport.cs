using Microsoft.Extensions.Logging;
using WheelTalk.Core.Logging;
using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Playback;

/// <summary>
/// A wheel made of a recorded raw dump. Everything above the transport — decoder, session, alerts,
/// screens, the ride log itself — runs exactly as it does over BLE, which is what makes the app
/// testable indoors and inside an emulator, where there is no radio at all.
/// <para>
/// The file is opened through a factory rather than by path: the core has no business knowing
/// where a host keeps its files, and playback has to reopen the dump every time it loops.
/// </para>
/// </summary>
public sealed partial class ReplayTransport : ITransport
{
    private readonly Func<TextReader> _openDump;
    private readonly TimeProvider _timeProvider;
    private readonly double _speed;
    private readonly ILogger<ReplayTransport> _logger;

    private CancellationTokenSource? _playing;

    public event Action<byte[]>? DataReceived;

    /// <summary>A file never drops out — the event exists only to satisfy the interface.</summary>
    event Action? ITransport.ConnectionLost
    {
        add { }
        remove { }
    }

    /// <param name="speed">
    /// Во сколько раз быстрее записанного играть. Меньше единицы — медленнее, и это не прихоть:
    /// раскрут до потолка занимает на записи двенадцать секунд, а тревога за это время проходит
    /// весь путь от первого сигнала до сплошного так быстро, что стадии сливаются в одну и
    /// проверить их нечем.
    /// </param>
    public ReplayTransport(
        Func<TextReader> openDump,
        TimeProvider timeProvider,
        ILogger<ReplayTransport> logger,
        double speed = 1.0)
    {
        _openDump = openDump;
        _timeProvider = timeProvider;
        _speed = speed > 0 ? speed : 1.0;
        _logger = logger;
    }

    public bool IsReplay => true;

    public IAsyncEnumerable<DiscoveredDevice> ScanAsync(CancellationToken ct = default) =>
        AsyncEnumerable.Empty<DiscoveredDevice>();

    /// <summary>
    /// Connecting starts playback and returns, the way a real connection does — the session takes
    /// a completed <c>ConnectAsync</c> as "connected" and expects frames to arrive on their own.
    /// The address is ignored: the dump is whichever wheel was recorded.
    /// <para>
    /// Дерева служб у записи нет и быть не может — возвращается пустой список. Опознание протокола
    /// от этого не страдает: заголовки кадров в дампе те же, что в эфире, и по ним декодер решает
    /// сам.
    /// </para>
    /// </summary>
    public Task<IReadOnlyList<DiscoveredService>> ConnectAsync(string address, CancellationToken ct = default)
    {
        _playing?.Cancel();
        _playing = new CancellationTokenSource();
        var token = _playing.Token;

        _ = Task.Run(() => PlayUntilStoppedAsync(token), token);
        return Task.FromResult<IReadOnlyList<DiscoveredService>>([]);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _playing?.Cancel();
        _playing?.Dispose();
        _playing = null;
        return Task.CompletedTask;
    }

    public Task WriteAsync(byte[] cmd, CancellationToken ct = default)
    {
        LogDiscardedWrite(Convert.ToHexStringLower(cmd));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Plays the dump once, honouring the recorded gaps between frames so the decoder — and the
    /// alert engine above it, which measures peaks over a time window — see the pacing of a real
    /// ride. Set <paramref name="realtime"/> to false to run through the file at full speed.
    /// </summary>
    public async Task PlayAsync(bool realtime = true, CancellationToken ct = default)
    {
        using var reader = _openDump();
        TimeSpan? previous = null;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!RawFrameLog.TryParseLine(line, out var time, out byte[] frame))
            {
                if (!string.IsNullOrWhiteSpace(line)) LogMalformedLine(line);
                continue;
            }

            if (realtime && previous is { } prior)
            {
                var gap = time - prior;
                // A dump spans pauses — the wheel switched off, the app backgrounded. Waiting them
                // out would just look like a hung replay.
                if (gap > TimeSpan.Zero && gap < TimeSpan.FromSeconds(1))
                {
                    await Task.Delay(gap / _speed, _timeProvider, ct);
                }
            }
            previous = time;

            DataReceived?.Invoke(frame);
        }
    }

    /// <summary>
    /// Loops the dump, because a recording runs out long before anyone is done looking at the
    /// screen. Each pass reopens the file and starts its pacing over.
    /// </summary>
    private async Task PlayUntilStoppedAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await PlayAsync(realtime: true, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // expected — disconnected
        }
        catch (Exception ex)
        {
            LogPlaybackFailed(ex);
        }
    }

    [LoggerMessage(EventId = 1400, EventName = "Replay.DiscardedWrite", Level = LogLevel.Information,
        Message = "Replay.DiscardedWrite {Hex} — nothing to send to")]
    private partial void LogDiscardedWrite(string hex);

    [LoggerMessage(EventId = 1401, EventName = "Replay.MalformedLine", Level = LogLevel.Warning,
        Message = "Replay.MalformedLine {Line}")]
    private partial void LogMalformedLine(string line);

    [LoggerMessage(EventId = 1402, EventName = "Replay.PlaybackFailed", Level = LogLevel.Error,
        Message = "Replay.PlaybackFailed")]
    private partial void LogPlaybackFailed(Exception ex);
}
