using Microsoft.Extensions.Logging;
using WheelTalk.Core.Diagnostics;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of InMotionAdapter.CANMessage 1:1 (InMotionAdapter.java:693-1096, minus the WheelData-writing
/// parse methods — <see cref="InMotionDecoder"/> owns that, the same split <see cref="GotwayUnpacker"/>
/// keeps from <see cref="GotwayDecoder"/>). A CAN frame over InMotion's wire format: 4-byte little-endian
/// id, 8-byte data payload (or a longer "extended" payload when <see cref="Len"/> is <c>0xFE</c>, whose
/// actual length lives in the first 4 bytes of <see cref="Data"/>), plus len/channel/format/type fields.
/// <see cref="WriteBuffer"/> wraps it for the wire (<c>AA AA … checksum 55 55</c>, with <c>0xA5</c>
/// escaping any <c>0xAA</c>/<c>0x55</c>/<c>0xA5</c> byte in the body); <see cref="Verify"/> reverses that
/// for a buffer <see cref="InMotionUnpacker"/> already de-escaped and framed.
/// <para>
/// Not ported: <c>getFastData()</c> and <c>getBatteryLevelsdata()</c>/<c>getVersion()</c> — dead code in
/// the original (declared, never called from anywhere in <c>InMotionAdapter</c>); <c>setMode(int)</c> —
/// likewise unused. <c>standardMessage()</c> (the one that IS used, by the keep-alive poll) is identical
/// to the dead <c>getFastData()</c> byte-for-byte, which is itself the reason the original carries both.
/// </para>
/// </summary>
public sealed class InMotionCanMessage
{
    public enum IdValue
    {
        NoOp = 0,
        GetFastInfo = 0x0F550113,
        GetSlowInfo = 0x0F550114,
        RideMode = 0x0F550115,
        RemoteControl = 0x0F550116,
        Calibration = 0x0F550119,
        PinCode = 0x0F550307,
        Light = 0x0F55010D,
        HandleButton = 0x0F55012E,
        SpeakerVolume = 0x0F55060A,
        PlaySound = 0x0F550609,
        Alert = 0x0F780101,
    }

    private const int StandardFormat = 0;
    private const int ExtendedFormat = 1;
    private const int DataFrame = 0;
    private const int RemoteFrame = 1;

    public int Id { get; private init; } = (int)IdValue.NoOp;
    public byte[] Data { get; private init; } = new byte[8];
    public int Len { get; private init; }
    public int Ch { get; private init; }
    public int Format { get; private init; } = StandardFormat;
    public int Type { get; private init; } = DataFrame;
    public byte[]? ExData { get; private init; }

    public bool IsValid => ExData is not null;

    /// <summary>
    /// Port of <c>CANMessage.verify(byte[])</c> — checks header/footer and checksum on a buffer
    /// <see cref="InMotionUnpacker"/> has already de-escaped and framed (<c>AA AA … check 55 55</c>),
    /// then parses the checksum-stripped body. <c>null</c> on any mismatch, exactly like the original's
    /// silent drop.
    /// </summary>
    public static InMotionCanMessage? Verify(byte[] buffer, ILogger logger)
    {
        if (buffer.Length < 5 || buffer[0] != 0xAA || buffer[1] != 0xAA
            || buffer[^1] != 0x55 || buffer[^2] != 0x55)
        {
            return null;
        }

        int len = buffer.Length - 3;
        byte[] dataBuffer = buffer[2..len];
        byte check = ComputeCheck(dataBuffer);
        byte bufferCheck = buffer[len];

        if (check != bufferCheck)
        {
            logger.LogChecksumFail(check, bufferCheck);
            return null;
        }

        return FromDataBuffer(dataBuffer);
    }

    /// <summary>Port of the <c>CANMessage(byte[] bArr)</c> constructor (InMotionAdapter.java:757-774).</summary>
    private static InMotionCanMessage? FromDataBuffer(byte[] bArr)
    {
        if (bArr.Length < 16) return null;

        // Signed-byte arithmetic, matching Java's byte->int promotion — harmless for every real id
        // in IdValue (all four bytes stay below 0x80), kept for exact fidelity regardless.
        int id = (((sbyte)bArr[3] * 256 + (sbyte)bArr[2]) * 256 + (sbyte)bArr[1]) * 256 + (sbyte)bArr[0];
        byte[] data = bArr[4..12];
        int len = bArr[12];
        int ch = bArr[13];
        int format = bArr[14] == 0 ? StandardFormat : ExtendedFormat;
        int type = bArr[15] == 0 ? DataFrame : RemoteFrame;
        byte[]? exData = null;

        if (len == 0xFE)
        {
            int ldata = MathsUtil.IntFromBytesLE(data, 0);
            if (ldata == bArr.Length - 16 && ldata >= 0)
            {
                exData = bArr[16..(16 + ldata)];
            }
        }

        return new InMotionCanMessage { Id = id, Data = data, Len = len, Ch = ch, Format = format, Type = type, ExData = exData };
    }

    /// <summary>Port of <c>writeBuffer()</c> (InMotionAdapter.java:783-802).</summary>
    public byte[] WriteBuffer()
    {
        byte[] canBuffer = GetBytes();
        byte check = ComputeCheck(canBuffer);

        var buffer = new List<byte>(canBuffer.Length * 2 + 5) { 0xAA, 0xAA };
        buffer.AddRange(Escape(canBuffer));
        buffer.Add(check);
        buffer.Add(0x55);
        buffer.Add(0x55);
        return [.. buffer];
    }

    /// <summary>Port of <c>getBytes()</c> (InMotionAdapter.java:804-838).</summary>
    private byte[] GetBytes()
    {
        var buffer = new List<byte>(18)
        {
            (byte)Id, (byte)(Id >> 8), (byte)(Id >> 16), (byte)(Id >> 24),
        };
        buffer.AddRange(Data);
        buffer.Add((byte)Len);
        buffer.Add((byte)Ch);
        buffer.Add((byte)(Format == StandardFormat ? 0 : 1));
        buffer.Add((byte)(Type == DataFrame ? 0 : 1));

        if (Len == 0xFE && ExData is not null)
        {
            buffer.AddRange(ExData);
        }
        return [.. buffer];
    }

    /// <summary>Port of <c>escape(byte[])</c> (InMotionAdapter.java:875-884).</summary>
    private static byte[] Escape(byte[] buffer)
    {
        var result = new List<byte>(buffer.Length + 4);
        foreach (byte c in buffer)
        {
            if (c is 0xAA or 0x55 or 0xA5) result.Add(0xA5);
            result.Add(c);
        }
        return [.. result];
    }

    /// <summary>Port of <c>computeCheck(byte[])</c> (InMotionAdapter.java:844-851).</summary>
    private static byte ComputeCheck(byte[] buffer)
    {
        int check = 0;
        foreach (byte c in buffer) check = (check + c) & 0xFF;
        return (byte)check;
    }

    // --- Factory methods (InMotionAdapter.java:886-1096) ---

    public static InMotionCanMessage StandardMessage() => new()
    {
        Len = 8,
        Id = (int)IdValue.GetFastInfo,
        Ch = 5,
        Data = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
    };

    public static InMotionCanMessage GetSlowData() => new()
    {
        Len = 8,
        Id = (int)IdValue.GetSlowInfo,
        Ch = 5,
        Type = RemoteFrame,
        Data = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
    };

    public static InMotionCanMessage SetLight(bool on) => new()
    {
        Len = 8,
        Id = (int)IdValue.Light,
        Ch = 5,
        Data = [(byte)(on ? 1 : 0), 0, 0, 0, 0, 0, 0, 0],
    };

    public static InMotionCanMessage SetLed(bool on) => new()
    {
        Len = 8,
        Id = (int)IdValue.RemoteControl,
        Ch = 5,
        Data = [0xB2, 0, 0, 0, (byte)(on ? 0x0F : 0x10), 0, 0, 0],
    };

    public static InMotionCanMessage WheelBeep() => new()
    {
        Len = 8,
        Id = (int)IdValue.RemoteControl,
        Ch = 5,
        Data = [0xB2, 0, 0, 0, 0x11, 0, 0, 0],
    };

    public static InMotionCanMessage WheelCalibration() => new()
    {
        Len = 8,
        Id = (int)IdValue.Calibration,
        Ch = 5,
        Data = [0x32, 0x54, 0x76, 0x98, 0, 0, 0, 0],
    };

    public static InMotionCanMessage PowerOff() => new()
    {
        Len = 8,
        Id = (int)IdValue.RemoteControl,
        Ch = 5,
        Data = [0xB2, 0, 0, 0, 5, 0, 0, 0],
    };

    public static InMotionCanMessage SetHandleButton(bool on) => new()
    {
        Len = 8,
        Id = (int)IdValue.HandleButton,
        Ch = 5,
        Data = [(byte)(on ? 0 : 1), 0, 0, 0, 0, 0, 0, 0],
    };

    public static InMotionCanMessage SetMaxSpeed(int maxSpeed) => new()
    {
        Len = 8,
        Id = (int)IdValue.RideMode,
        Ch = 5,
        Data = LittleEndianData(1, (short)(maxSpeed * 1000), afterValueAt: 4),
    };

    public static InMotionCanMessage PlaySound(byte soundNumber) => new()
    {
        Len = 8,
        Id = (int)IdValue.PlaySound,
        Ch = 5,
        Data = [soundNumber, 0, 0, 0, 0, 0, 0, 0],
    };

    /// <summary>rideMode: false = Comfort, true = Classic.</summary>
    public static InMotionCanMessage SetRideMode(bool rideMode) => new()
    {
        Len = 8,
        Id = (int)IdValue.RideMode,
        Ch = 5,
        Data = [0x0A, 0, 0, 0, (byte)(rideMode ? 1 : 0), 0, 0, 0],
    };

    public static InMotionCanMessage SetPedalSensivity(int sensivity) => new()
    {
        Len = 8,
        Id = (int)IdValue.RideMode,
        Ch = 5,
        Data = LittleEndianData(6, (short)((sensivity + 28) << 5), afterValueAt: 4),
    };

    public static InMotionCanMessage SetSpeakerVolume(int speakerVolume) => new()
    {
        Len = 8,
        Id = (int)IdValue.SpeakerVolume,
        Ch = 5,
        Data = [(byte)(speakerVolume * 100 & 0xFF), (byte)(speakerVolume * 100 / 0x100 & 0xFF), 0, 0, 0, 0, 0, 0],
    };

    public static InMotionCanMessage SetTiltHorizon(int tiltHorizon) => new()
    {
        Len = 8,
        Id = (int)IdValue.RideMode,
        Ch = 5,
        Data = LittleEndianData32(tiltHorizon * 65536 / 10, at: 4),
    };

    /// <summary>Port of <c>getPassword(String)</c> (InMotionAdapter.java:1087-1096) — the password is
    /// always exactly 6 ASCII digits (<see cref="Ports.IWheelConfig.InMotionPassword"/> guarantees it).</summary>
    public static InMotionCanMessage GetPassword(string password) => new()
    {
        Len = 8,
        Id = (int)IdValue.PinCode,
        Ch = 5,
        Data = [.. System.Text.Encoding.ASCII.GetBytes(password)[..6], 0, 0],
    };

    /// <summary>4 zero bytes, then <paramref name="value"/> little-endian at <paramref name="afterValueAt"/>
    /// (2 bytes), matching the byte layout <c>setMaxSpeed</c>/<c>setPedalSensivity</c> build via
    /// <c>MathsUtil.getBytes(short)</c> + a manual byte swap in the original.</summary>
    private static byte[] LittleEndianData(byte firstByte, short value, int afterValueAt)
    {
        var data = new byte[8];
        data[0] = firstByte;
        data[afterValueAt] = (byte)value;
        data[afterValueAt + 1] = (byte)(value >> 8);
        return data;
    }

    /// <summary>4-byte little-endian <paramref name="value"/> at <paramref name="at"/>, matching
    /// <c>setTiltHorizon</c>'s byte layout.</summary>
    private static byte[] LittleEndianData32(int value, int at)
    {
        var data = new byte[8];
        data[at] = (byte)value;
        data[at + 1] = (byte)(value >> 8);
        data[at + 2] = (byte)(value >> 16);
        data[at + 3] = (byte)(value >> 24);
        return data;
    }
}

internal static partial class InMotionLog
{
    [LoggerMessage(EventId = LogEvents.Unpacking.InMotionChecksumFailId, EventName = LogEvents.Unpacking.InMotionChecksumFailName,
        Level = LogLevel.Debug, Message = "InMotion checksum mismatch, calc: {Calculated:X2}, packet: {Received:X2}")]
    public static partial void LogChecksumFail(this ILogger logger, byte calculated, byte received);
}
