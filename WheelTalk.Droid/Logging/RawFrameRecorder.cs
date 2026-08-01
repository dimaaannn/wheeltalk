using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WheelTalk.Droid.Configuration;
using WheelTalk.Core.Logging;
using WheelTalk.Core.Ports;
using WheelTalk.Core.Services;

namespace WheelTalk.Droid.Logging;

/// <summary>
/// Every BLE frame as it arrived, before the decoder touched it. Off by default and switched on by
/// <c>Logging:RawDump</c>, the way the original gates it — at twenty-odd frames a second it costs
/// about a megabyte per ten minutes, and nothing rotates it.
/// <para>
/// This is the only recording that can prove the decoder right: it is the format the replay
/// transport reads, so a ride taken on the phone can be played back into the decoder on a PC.
/// </para>
/// </summary>
public sealed partial class RawFrameRecorder : IDisposable
{
    /// <summary>Frames arrive around twenty-three times a second — this is a flush every few seconds.</summary>
    private const int FlushEveryLines = 100;

    private readonly ITransport _transport;
    private readonly WheelSession _session;
    private readonly LoggingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RawFrameRecorder> _logger;
    private readonly Lock _gate = new();

    private BufferedLogFile? _file;
    private string _mac = "";
    private bool _listening;

    public RawFrameRecorder(
        ITransport transport,
        WheelSession session,
        IOptions<LoggingOptions> options,
        TimeProvider timeProvider,
        ILogger<RawFrameRecorder> logger)
    {
        _transport = transport;
        _session = session;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>True while frames are being written — what the recording screen shows.</summary>
    public bool IsRecording => _listening;

    /// <summary>The dump being written, once the first frame has arrived and named it.</summary>
    public string? FileName
    {
        get
        {
            lock (_gate) return _file is null ? null : Path.GetFileName(_file.Path);
        }
    }

    /// <summary>
    /// Brings the dump in line with <c>Logging:RawDump</c>. Called at startup and again whenever the
    /// switch is touched: read once in a constructor, the setting would need a restart to take
    /// effect, and a switch that does nothing until the app is killed is not a switch.
    /// <para>
    /// The subscription is to the transport, which outlives individual connections, so a reconnect
    /// resumes into the same file instead of starting a new one.
    /// </para>
    /// </summary>
    public void Apply()
    {
        if (_options.RawDump) Start();
        else Stop();
    }

    public void Start()
    {
        if (_listening) return;

        _listening = true;
        _transport.DataReceived += Write;
    }

    /// <summary>
    /// Stops and **closes the file**. Leaving it open would keep the last few frames in the buffer
    /// where nothing flushes them, and the point of switching the dump off mid-session is usually
    /// that you want to take what is already written.
    /// </summary>
    public void Stop()
    {
        if (!_listening) return;

        _listening = false;
        _transport.DataReceived -= Write;
        CloseFile();
    }

    public void Dispose()
    {
        _transport.DataReceived -= Write;
        _listening = false;
        CloseFile();
    }

    private void CloseFile()
    {
        lock (_gate)
        {
            if (_file is null) return;
            LogStopped(_file.Path, _file.LinesWritten);
            _file.Dispose();
            _file = null;
        }
    }

    private void Write(byte[] frame)
    {
        string mac = _session.Address ?? "";
        if (mac.Length == 0) return;

        lock (_gate)
        {
            if (_file is not null && mac != _mac)
            {
                LogStopped(_file.Path, _file.LinesWritten);
                _file.Dispose();
                _file = null;
            }

            if (_file is null)
            {
                _mac = mac;
                _file = new BufferedLogFile(RideFiles.RawDump(mac, _timeProvider.GetLocalNow()), FlushEveryLines);
                LogStarted(_file.Path);
            }

            _file.Write(RawFrameLog.FormatLine(_timeProvider.GetLocalNow(), frame));
        }
    }

    [LoggerMessage(EventId = 1510, EventName = "Raw.DumpStarted", Level = LogLevel.Information,
        Message = "Raw.DumpStarted {Path}")]
    private partial void LogStarted(string path);

    [LoggerMessage(EventId = 1511, EventName = "Raw.DumpStopped", Level = LogLevel.Information,
        Message = "Raw.DumpStopped {Path} {Frames} frames")]
    private partial void LogStopped(string path, int frames);
}
