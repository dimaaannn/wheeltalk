using Microsoft.Extensions.Logging;
using WheelTalk.Core.Diagnostics;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of InmotionAdapterV2.InmotionUnpackerV2 1:1 (InmotionAdapterV2.java:2406-2481) — frame
/// assembly automaton for InMotion V2's Nordic-UART protocol. Header <c>AA AA</c>, no footer marker
/// (unlike V1's trailing <c>55 55</c>) — the frame's own length field (byte 3, right after the
/// header and a one-byte flags field) tells the unpacker exactly how many more bytes to collect,
/// checksum included. <c>0xA5</c> escapes any literal <c>0xAA</c>/<c>0xA5</c> byte in the stream —
/// note this is a narrower escape set than V1's (V1 also escapes <c>0x55</c>, for its footer; V2 has
/// no footer to protect).
/// </summary>
public sealed partial class InMotionV2Unpacker
{
    private enum UnpackerState
    {
        Unknown,
        FlagSearch,
        LenSearch,
        Collecting,
        Done,
    }

    // Not independently DI-resolved (always `new`'d by InMotionDecoderV2) — shares the owning
    // decoder's typed logger category, same as GotwayUnpacker/InMotionUnpacker.
    private readonly ILogger<InMotionDecoderV2> _logger;
    private List<byte> _buffer = [];
    private byte _oldc;
    private int _len;
    private UnpackerState _state = UnpackerState.Unknown;

    public InMotionV2Unpacker(ILogger<InMotionDecoderV2> logger)
    {
        _logger = logger;
    }

    public byte[] GetBuffer() => [.. _buffer];

    public bool AddChar(byte c)
    {
        if (c != 0xA5 || _oldc == 0xA5)
        {
            switch (_state)
            {
                case UnpackerState.Collecting:
                    _buffer.Add(c);
                    if (_buffer.Count == _len + 5)
                    {
                        _state = UnpackerState.Done;
                        LogFrameValid();
                        _oldc = 0; // matches the original's early return past its own trailing `oldc = c`.
                        return true;
                    }
                    break;

                case UnpackerState.LenSearch:
                    _buffer.Add(c);
                    _len = c;
                    _state = UnpackerState.Collecting;
                    _oldc = c;
                    break;

                case UnpackerState.FlagSearch:
                    _buffer.Add(c);
                    _state = UnpackerState.LenSearch;
                    _oldc = c;
                    break;

                default:
                    if (c == 0xAA && _oldc == 0xAA)
                    {
                        _buffer = [0xAA, 0xAA];
                        _state = UnpackerState.FlagSearch;
                    }
                    _oldc = c;
                    break;
            }
        }
        else
        {
            _oldc = c;
        }
        return false;
    }

    [LoggerMessage(EventId = LogEvents.Unpacking.InMotionV2FrameValidId, EventName = LogEvents.Unpacking.InMotionV2FrameValidName,
        Level = LogLevel.Trace, Message = "Valid InMotion V2 frame received")]
    private partial void LogFrameValid();
}
