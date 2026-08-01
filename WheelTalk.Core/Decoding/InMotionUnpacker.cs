using Microsoft.Extensions.Logging;
using WheelTalk.Core.Diagnostics;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of InMotionAdapter.InMotionUnpacker 1:1 (InMotionAdapter.java:1269-1329) — frame assembly
/// automaton for InMotion's CAN-over-BLE protocol. Header <c>AA AA</c>, footer <c>55 55</c>, with
/// <c>0xA5</c> as an escape byte: the sender doubles any occurrence of <c>0xAA</c>/<c>0x55</c>/<c>0xA5</c>
/// inside the payload by prefixing it with a bare <c>0xA5</c>, and this unpacker strips those escape
/// markers back out while collecting. Basic frames carry a fixed 8-byte CAN payload (<c>len == 8</c>);
/// extended frames (<c>len == 0xFE</c>) carry a longer payload whose length lives in the low byte of
/// the payload's own first 32-bit field — the unpacker peeks at that single byte (position 7) as
/// <c>lenEx</c>, which is enough since every extended payload this protocol sends is under 256 bytes.
/// </summary>
public sealed partial class InMotionUnpacker
{
    private enum UnpackerState
    {
        Unknown,
        Collecting,
        Done,
    }

    // Not independently DI-resolved (always `new`'d by InMotionDecoder) — shares the owning
    // decoder's typed logger category rather than needing its own ILogger<InMotionUnpacker>.
    private readonly ILogger<InMotionDecoder> _logger;
    private List<byte> _buffer = [];
    private byte _oldc;
    private int _lenP;
    private int _lenEx;
    private UnpackerState _state = UnpackerState.Unknown;

    public InMotionUnpacker(ILogger<InMotionDecoder> logger)
    {
        _logger = logger;
    }

    public byte[] GetBuffer() => [.. _buffer];

    public bool AddChar(byte c)
    {
        // A bare 0xA5 is an escape marker and is dropped — unless the *previous* byte was itself
        // 0xA5, in which case this one is the escaped data byte the marker announced, and it is
        // processed like any other byte. Ported exactly as the original computes it (including its
        // behavior on runs of literal 0xA5 data bytes) rather than re-derived from first principles.
        if (c != 0xA5 || _oldc == 0xA5)
        {
            if (_state == UnpackerState.Collecting)
            {
                _buffer.Add(c);
                int size = _buffer.Count;
                if (size == 7) _lenEx = c;
                else if (size == 15) _lenP = c;

                if (size > _lenEx + 21 && _lenP == 0xFE)
                {
                    Reset(); // longer than expected — Reset() already zeroes _oldc, matching the
                             // original's early return past its own trailing `oldc = c`.
                    return false;
                }

                // 18-byte header/CAN-fields + 1 checksum + 2-byte footer.
                if (c == 0x55 && _oldc == 0x55 && (size == _lenEx + 21 || _lenP != 0xFE))
                {
                    _state = UnpackerState.Done;
                    LogFrameValid();
                    _oldc = 0; // matches the original's early return past its own trailing `oldc = c`.
                    return true;
                }
            }
            else
            {
                if (c == 0xAA && _oldc == 0xAA)
                {
                    _buffer = [0xAA, 0xAA];
                    _state = UnpackerState.Collecting;
                }
            }
        }

        _oldc = c;
        return false;
    }

    private void Reset()
    {
        _buffer = [];
        _oldc = 0;
        _lenP = 0;
        _lenEx = 0;
        _state = UnpackerState.Unknown;
    }

    [LoggerMessage(EventId = LogEvents.Unpacking.InMotionFrameValidId, EventName = LogEvents.Unpacking.InMotionFrameValidName,
        Level = LogLevel.Trace, Message = "Valid InMotion frame received")]
    private partial void LogFrameValid();
}
