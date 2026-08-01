using System.IO.Hashing;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Diagnostics;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of VeteranAdapter.veteranUnpacker 1:1 (VeteranAdapter.java:338-433) — byte-by-byte
/// frame assembly automaton. Header DC 5A 5C, length byte, payload, optional CRC32 trailer.
/// </summary>
public sealed partial class VeteranUnpacker
{
    private enum UnpackerState
    {
        Unknown,
        Collecting,
        LenSearch,
        Done,
    }

    // Not independently DI-resolved (always `new`'d by VeteranDecoder) — shares the owning
    // decoder's typed logger category rather than needing its own ILogger<VeteranUnpacker>.
    private readonly ILogger<VeteranDecoder> _logger;
    private bool _usingCrc;
    private List<byte> _buffer = new();
    private int _old1;
    private int _old2;
    private int _len;
    private UnpackerState _state = UnpackerState.Unknown;

    /// <summary>Throttles <see cref="LogCrcFail"/> to the transition into a failing streak instead
    /// of once per frame while CRC keeps failing.</summary>
    private bool _crcFailStreak;

    public VeteranUnpacker(ILogger<VeteranDecoder> logger)
    {
        _logger = logger;
    }

    public byte[] GetBuffer() => _buffer.ToArray();

    /// <summary>Feed one byte; returns true when a complete, CRC-valid (if applicable) frame is ready.</summary>
    public bool AddChar(byte c)
    {
        switch (_state)
        {
            case UnpackerState.Collecting:
            {
                int bsize = _buffer.Count;
                if ((bsize == 22 && c != 0x00)
                    || (bsize == 30 && !(c == 0x00 || c == 0x07))
                    || (bsize == 23 && (c & 0xFE) != 0x00))
                {
                    _state = UnpackerState.Done;
                    LogLenVerifyFailed();
                    Reset();
                    return false;
                }
                _buffer.Add(c);
                if (bsize == _len + 3)
                {
                    _state = UnpackerState.Done;
                    LogFrameValid(_len);
                    Reset();
                    if (_len > 38 || _usingCrc) // new format with crc32
                    {
                        var frame = GetBuffer();
                        uint calcCrc = Crc32.HashToUInt32(frame.AsSpan(0, _len));
                        long providedCrc = MathsUtil.IntFromBytesBE(frame, _len);
                        if (calcCrc == providedCrc)
                        {
                            _usingCrc = true;
                            _crcFailStreak = false;
                            LogCrcOk();
                            return true;
                        }
                        // Throttled to the transition into a failing streak — a sustained CRC
                        // mismatch would otherwise re-log the same Warning on every frame.
                        if (!_crcFailStreak)
                        {
                            LogCrcFail();
                            _crcFailStreak = true;
                        }
                        return false;
                    }
                    return true; // old format without crc32
                }
                break;
            }

            case UnpackerState.LenSearch:
                _buffer.Add(c);
                _len = c & 0xff;
                _state = UnpackerState.Collecting;
                _old2 = _old1;
                _old1 = c;
                break;

            default:
                if (c == 0x5C && _old1 == 0x5A && _old2 == 0xDC)
                {
                    _buffer = new List<byte> { 0xDC, 0x5A, 0x5C };
                    _state = UnpackerState.LenSearch;
                }
                else if (c == 0x5A && _old1 == 0xDC)
                {
                    _old2 = _old1;
                }
                else
                {
                    _old2 = 0;
                }
                _old1 = c;
                break;
        }
        return false;
    }

    public void Reset()
    {
        _old1 = 0;
        _old2 = 0;
        _state = UnpackerState.Unknown;
    }

    [LoggerMessage(EventId = LogEvents.Unpacking.LenVerifyFailedId, EventName = LogEvents.Unpacking.LenVerifyFailedName,
        Level = LogLevel.Debug, Message = "Data verification failed")]
    private partial void LogLenVerifyFailed();

    [LoggerMessage(EventId = LogEvents.Unpacking.VeteranFrameValidId, EventName = LogEvents.Unpacking.VeteranFrameValidName,
        Level = LogLevel.Trace, Message = "Len {Len}, step reset")]
    private partial void LogFrameValid(int len);

    [LoggerMessage(EventId = LogEvents.Unpacking.CrcOkId, EventName = LogEvents.Unpacking.CrcOkName,
        Level = LogLevel.Trace, Message = "CRC32 ok")]
    private partial void LogCrcOk();

    [LoggerMessage(EventId = LogEvents.Unpacking.CrcFailId, EventName = LogEvents.Unpacking.CrcFailName,
        Level = LogLevel.Warning, Message = "CRC32 fail")]
    private partial void LogCrcFail();
}
