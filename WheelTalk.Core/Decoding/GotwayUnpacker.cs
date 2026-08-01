using Microsoft.Extensions.Logging;
using WheelTalk.Core.Diagnostics;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of GotwayAdapter.gotwayUnpacker 1:1 (GotwayAdapter.java:728-789) — frame assembly
/// automaton for the Gotway/Begode serial-over-BLE protocol. Header 55 AA, fixed 24-byte
/// frame, footer 5A 5A 5A 5A at bytes 20-23. No CRC (see the protocol notes at the bottom
/// of GotwayAdapter.java). Also reassembles two known "garbage" byte patterns the BLE
/// adapter occasionally inserts (55 AA 5A 55 AA and 55 AA 5A 5A 55 AA).
/// </summary>
public sealed partial class GotwayUnpacker
{
    private enum UnpackerState
    {
        Unknown,
        Collecting,
        Done,
    }

    // Not independently DI-resolved (always `new`'d by GotwayDecoder) — shares the owning
    // decoder's typed logger category rather than needing its own ILogger<GotwayUnpacker>.
    private readonly ILogger<GotwayDecoder> _logger;
    private List<byte> _buffer = new();
    private UnpackerState _state = UnpackerState.Unknown;
    private int _oldc = -1;

    public GotwayUnpacker(ILogger<GotwayDecoder> logger)
    {
        _logger = logger;
    }

    public byte[] GetBuffer() => _buffer.ToArray();

    public bool AddChar(byte c)
    {
        if (_state == UnpackerState.Collecting)
        {
            _buffer.Add(c);
            _oldc = c;
            int size = _buffer.Count;

            if (size > 20 && size <= 24 && c != 0x5A)
            {
                LogInvalidFooter();
                _state = UnpackerState.Unknown;
                return false;
            }
            if (size == 24)
            {
                _state = UnpackerState.Done;
                LogFrameValid();
                return true;
            }
            if (size == 5) // found some garbage in protocol, packet 55aa5a and packet 55aa5a5a
            {
                var buf = _buffer;
                if (buf[0] == 0x55 && buf[1] == 0xAA && buf[2] == 0x5A && buf[3] == 0x55 && buf[4] == 0xAA)
                {
                    LogGarbageReassembled();
                    _buffer = new List<byte> { 0x55, 0xAA };
                }
            }
            if (size == 6)
            {
                var buf = _buffer;
                if (buf[0] == 0x55 && buf[1] == 0xAA && buf[2] == 0x5A && buf[3] == 0x5A && buf[4] == 0x55 && buf[5] == 0xAA)
                {
                    LogGarbageReassembled();
                    _buffer = new List<byte> { 0x55, 0xAA };
                }
            }
        }
        else
        {
            if (c == 0xAA && _oldc == 0x55)
            {
                LogHeaderFound();
                _buffer = new List<byte> { 0x55, 0xAA };
                _state = UnpackerState.Collecting;
            }
            _oldc = c;
        }
        return false;
    }

    [LoggerMessage(EventId = LogEvents.Unpacking.InvalidFooterId, EventName = LogEvents.Unpacking.InvalidFooterName,
        Level = LogLevel.Warning, Message = "Invalid frame footer (expected 5A 5A 5A 5A)")]
    private partial void LogInvalidFooter();

    [LoggerMessage(EventId = LogEvents.Unpacking.FrameValidId, EventName = LogEvents.Unpacking.FrameValidName,
        Level = LogLevel.Trace, Message = "Valid frame received")]
    private partial void LogFrameValid();

    [LoggerMessage(EventId = LogEvents.Unpacking.GarbageReassembledId, EventName = LogEvents.Unpacking.GarbageReassembledName,
        Level = LogLevel.Debug, Message = "Found garbage packet, reassembling")]
    private partial void LogGarbageReassembled();

    [LoggerMessage(EventId = LogEvents.Unpacking.HeaderFoundId, EventName = LogEvents.Unpacking.HeaderFoundName,
        Level = LogLevel.Trace, Message = "Frame header found (55 AA), collecting data")]
    private partial void LogHeaderFound();
}
