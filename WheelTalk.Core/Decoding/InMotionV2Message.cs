using Microsoft.Extensions.Logging;
using WheelTalk.Core.Diagnostics;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of InmotionAdapterV2.Message 1:1 (InmotionAdapterV2.java:490-2404, minus the WheelData-writing
/// parse methods — <see cref="InMotionDecoderV2"/> owns that, same split as V1's
/// <see cref="InMotionCanMessage"/>). Wire shape: <c>AA AA</c> header, one-byte flags, one-byte
/// length (= command + data length, i.e. <c>data.Length + 1</c>), one-byte command, data, then a
/// single XOR checksum byte — no footer marker, unlike V1. <c>0xA5</c> escapes <c>0xAA</c>/<c>0xA5</c>
/// bytes in the body (narrower escape set than V1's, which also escapes <c>0x55</c> for its footer).
/// </summary>
public sealed class InMotionV2Message
{
    public enum Flag
    {
        NoOp = 0,
        Initial = 0x11,
        Default = 0x14,
    }

    public enum Command
    {
        NoOp = 0,
        MainVersion = 0x01,
        MainInfo = 0x02,
        Diagnostic = 0x03,
        RealTimeInfo = 0x04,
        BatteryRealTimeInfo = 0x05,
        Something1 = 0x10,
        TotalStats = 0x11,
        Settings = 0x20,
        Control = 0x60,
    }

    public int Flags { get; private init; } = (int)Flag.NoOp;
    public int Len { get; private init; }
    public int Command_ { get; private init; }
    public byte[] Data { get; private init; } = [];

    /// <summary>Port of the <c>Message(byte[] bArr)</c> constructor (InmotionAdapterV2.java:538-546).
    /// <paramref name="bArr"/> is the de-escaped frame including its <c>AA AA</c> header, checksum
    /// already stripped by <see cref="Verify"/>.</summary>
    private static InMotionV2Message? FromFrame(byte[] bArr)
    {
        if (bArr.Length < 5) return null;

        int flags = bArr[2];
        int len = bArr[3];
        int command = bArr[4] & 0x7F;
        byte[] data = len > 1 ? bArr[5..(len + 4)] : [];

        return new InMotionV2Message { Flags = flags, Len = len, Command_ = command, Data = data };
    }

    /// <summary>Port of <c>Message.verify(byte[])</c> (InmotionAdapterV2.java:2380-2392) — the
    /// checksum is the buffer's last byte, XOR of everything before it.</summary>
    public static InMotionV2Message? Verify(byte[] buffer, ILogger logger)
    {
        if (buffer.Length < 1) return null;

        byte[] dataBuffer = buffer[..^1];
        byte check = CalcCheck(dataBuffer);
        byte bufferCheck = buffer[^1];

        if (check != bufferCheck)
        {
            logger.LogV2ChecksumFail(check, bufferCheck);
            return null;
        }

        return FromFrame(dataBuffer);
    }

    /// <summary>Port of <c>writeBuffer()</c> (InmotionAdapterV2.java:2340-2355) — no <c>55 55</c>
    /// footer, unlike V1.</summary>
    public byte[] WriteBuffer()
    {
        byte[] body = GetBytes();
        byte check = CalcCheck(body);

        var buffer = new List<byte>(body.Length * 2 + 3) { 0xAA, 0xAA };
        buffer.AddRange(Escape(body));
        buffer.Add(check);
        return [.. buffer];
    }

    /// <summary>Port of <c>getBytes()</c> (InmotionAdapterV2.java:2357-2369).</summary>
    private byte[] GetBytes()
    {
        var buffer = new List<byte>(3 + Data.Length) { (byte)Flags, (byte)(Data.Length + 1), (byte)Command_ };
        buffer.AddRange(Data);
        return [.. buffer];
    }

    /// <summary>Port of <c>calcCheck(byte[])</c> (InmotionAdapterV2.java:2371-2378) — XOR, not the
    /// additive checksum V1 uses.</summary>
    private static byte CalcCheck(byte[] buffer)
    {
        int check = 0;
        foreach (byte c in buffer) check = (check ^ c) & 0xFF;
        return (byte)check;
    }

    /// <summary>Port of <c>escape(byte[])</c> (InmotionAdapterV2.java:2394-2403).</summary>
    private static byte[] Escape(byte[] buffer)
    {
        var result = new List<byte>(buffer.Length + 4);
        foreach (byte c in buffer)
        {
            if (c is 0xAA or 0xA5) result.Add(0xA5);
            result.Add(c);
        }
        return [.. result];
    }

    // --- Factory methods (InmotionAdapterV2.java:1929-2338) ---

    public static InMotionV2Message GetCarType() => new() { Flags = (int)Flag.Initial, Command_ = (int)Command.MainInfo, Data = [0x01] };
    public static InMotionV2Message GetSerialNumber() => new() { Flags = (int)Flag.Initial, Command_ = (int)Command.MainInfo, Data = [0x02] };
    public static InMotionV2Message GetVersions() => new() { Flags = (int)Flag.Initial, Command_ = (int)Command.MainInfo, Data = [0x06] };
    public static InMotionV2Message WheelOffFirstStage() => new() { Flags = (int)Flag.Initial, Command_ = (int)Command.Diagnostic, Data = [0x81, 0x00] };
    public static InMotionV2Message WheelOffSecondStage() => new() { Flags = (int)Flag.Initial, Command_ = (int)Command.Diagnostic, Data = [0x82] };
    public static InMotionV2Message GetCurrentSettings() => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Settings, Data = [0x20] };
    public static InMotionV2Message GetUselessData() => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Something1, Data = [0x00, 0x01] };
    public static InMotionV2Message GetStatistics() => new() { Flags = (int)Flag.Default, Command_ = (int)Command.TotalStats, Data = [] };
    public static InMotionV2Message GetRealTimeData() => new() { Flags = (int)Flag.Default, Command_ = (int)Command.RealTimeInfo, Data = [] };

    /// <summary>Port of playSound(int) (InmotionAdapterV2.java:2025-2036) — command byte 0x41 on
    /// V11 pre-1.4 firmware (<paramref name="isLegacyV11"/>), 0x51 otherwise.</summary>
    public static InMotionV2Message PlaySound(int number, bool isLegacyV11) => new()
    {
        Flags = (int)Flag.Default,
        Command_ = (int)Command.Control,
        Data = [(byte)(isLegacyV11 ? 0x41 : 0x51), (byte)(number & 0xFF), 0x01],
    };

    public static InMotionV2Message PlayBeep(int number) => new()
    {
        Flags = (int)Flag.Default,
        Command_ = (int)Command.Control,
        Data = [0x51, (byte)(number & 0xFF), 0x64],
    };

    public static InMotionV2Message WheelCalibration() => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x42, 0x01, 0x00, 0x01] };
    public static InMotionV2Message WheelCalibrationTurn() => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x52, 0x01, 0x00, 0x01] };
    public static InMotionV2Message WheelCalibrationBalance() => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x52, 0x01, 0x01, 0x00] };

    /// <summary>Port of setLight(boolean) (InmotionAdapterV2.java:2056-2068) — command byte 0x40 on
    /// V11 pre-1.4 firmware, 0x50 otherwise.</summary>
    public static InMotionV2Message SetLight(bool on, bool isLegacyV11) => new()
    {
        Flags = (int)Flag.Default,
        Command_ = (int)Command.Control,
        Data = [(byte)(isLegacyV11 ? 0x40 : 0x50), (byte)(on ? 1 : 0)],
    };

    public static InMotionV2Message SetLightV12(bool lowBeamOn, bool highBeamOn) => new()
    {
        Flags = (int)Flag.Default,
        Command_ = (int)Command.Control,
        Data = [0x50, (byte)(lowBeamOn ? 1 : 0), (byte)(highBeamOn ? 1 : 0)],
    };

    public static InMotionV2Message SetHandleButton(bool on) => new()
    {
        Flags = (int)Flag.Default,
        Command_ = (int)Command.Control,
        Data = [0x2e, (byte)(on ? 0 : 1)],
    };

    /// <summary>Port of setClassicMode (InmotionAdapterV2.java:2281-2289) — this is the "ride mode" hook.</summary>
    public static InMotionV2Message SetClassicMode(bool on) => new()
    {
        Flags = (int)Flag.Default,
        Command_ = (int)Command.Control,
        Data = [0x23, (byte)(on ? 1 : 0)],
    };

    public static InMotionV2Message SetVolume(int volume) => new()
    {
        Flags = (int)Flag.Default,
        Command_ = (int)Command.Control,
        Data = [0x26, (byte)(volume & 0xFF)],
    };

    public static InMotionV2Message SetPedalTilt(int angle)
    {
        short value = (short)(angle * 10);
        return new InMotionV2Message
        {
            Flags = (int)Flag.Default,
            Command_ = (int)Command.Control,
            Data = [0x22, (byte)value, (byte)(value >> 8)],
        };
    }

    public static InMotionV2Message SetPedalSensivity(int sensivity) => new()
    {
        Flags = (int)Flag.Default,
        Command_ = (int)Command.Control,
        Data = [0x25, (byte)(sensivity & 0xFF), (byte)(sensivity & 0xFF)],
    };

    public static InMotionV2Message SetStandbyDelay(int delayMinutes)
    {
        short value = (short)(delayMinutes * 60);
        return new InMotionV2Message
        {
            Flags = (int)Flag.Default,
            Command_ = (int)Command.Control,
            Data = [0x28, (byte)value, (byte)(value >> 8)],
        };
    }

    public static InMotionV2Message SetSplitAccelBreak(int acceleration, int breakSens, bool isV12Family) => new()
    {
        Flags = (int)Flag.Default,
        Command_ = (int)Command.Control,
        Data = [(byte)(isV12Family ? 0x40 : 0x3f), (byte)(acceleration & 0xFF), (byte)(breakSens & 0xFF)],
    };

    public static InMotionV2Message SetSoundWave(bool on) => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x39, (byte)(on ? 1 : 0)] };
    public static InMotionV2Message SetAutoLight(bool on) => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x2f, (byte)(on ? 1 : 0)] };
    public static InMotionV2Message SetLightBrightnessV12(int lowBeam, int highBeam) => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x2b, (byte)(lowBeam & 0xFF), (byte)(highBeam & 0xFF)] };
    public static InMotionV2Message SetLightBrightness(int brightness) => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x2b, (byte)(brightness & 0xFF)] };
    public static InMotionV2Message SetDrl(bool on) => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x2d, (byte)(on ? 1 : 0)] };
    public static InMotionV2Message SetFan(bool on) => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x43, (byte)(on ? 1 : 0)] };
    public static InMotionV2Message SetQuietMode(bool on) => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x38, (byte)(on ? 1 : 0)] };
    public static InMotionV2Message SetFancierMode(bool on) => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x24, (byte)(on ? 1 : 0)] };
    public static InMotionV2Message SetBermAngleMode(bool on) => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x45, (byte)(on ? 1 : 0)] };
    public static InMotionV2Message SetGoHome(bool on) => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x37, (byte)(on ? 1 : 0)] };
    public static InMotionV2Message SetTransportMode(bool on) => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x32, (byte)(on ? 1 : 0)] };
    public static InMotionV2Message SetLock(bool on) => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x31, (byte)(on ? 1 : 0)] };

    /// <summary>Port of setMute(boolean) (InmotionAdapterV2.java:2321-2329) — note the inverted sense
    /// versus every other on/off command here: <c>on</c> (muted) sends <c>0</c>.</summary>
    public static InMotionV2Message SetMute(bool on) => new() { Flags = (int)Flag.Default, Command_ = (int)Command.Control, Data = [0x2c, (byte)(on ? 0 : 1)] };

    /// <summary>Port of setSplitMode (InmotionAdapterV2.java:2092-2104) — command byte 0x42 on the
    /// V12 HS/HT/PRO family (0x3e conflicts with setAlarmSpeedV12 there), 0x3e otherwise.</summary>
    public static InMotionV2Message SetSplitMode(bool on, bool isV12Family) => new()
    {
        Flags = (int)Flag.Default,
        Command_ = (int)Command.Control,
        Data = [(byte)(isV12Family ? 0x42 : 0x3e), (byte)(on ? 1 : 0)],
    };

    public static InMotionV2Message SetAlarmSpeedV12(int speedLow, int speedHigh)
    {
        short low = (short)(speedLow * 100);
        short high = (short)(speedHigh * 100);
        return new InMotionV2Message
        {
            Flags = (int)Flag.Default,
            Command_ = (int)Command.Control,
            Data = [0x3e, (byte)low, (byte)(low >> 8), (byte)high, (byte)(high >> 8)],
        };
    }

    public static InMotionV2Message SetMaxSpeed(int maxSpeed)
    {
        short value = (short)(maxSpeed * 100);
        return new InMotionV2Message
        {
            Flags = (int)Flag.Default,
            Command_ = (int)Command.Control,
            Data = [0x21, (byte)value, (byte)(value >> 8)],
        };
    }

    public static InMotionV2Message SetMaxSpeedV14(int maxSpeed, int alarmSpeed)
    {
        short max = (short)(maxSpeed * 100);
        short alarm = (short)(alarmSpeed * 100);
        return new InMotionV2Message
        {
            Flags = (int)Flag.Default,
            Command_ = (int)Command.Control,
            Data = [0x21, (byte)max, (byte)(max >> 8), (byte)alarm, (byte)(alarm >> 8)],
        };
    }
}

internal static partial class InMotionV2Log
{
    [LoggerMessage(EventId = LogEvents.Unpacking.InMotionV2ChecksumFailId, EventName = LogEvents.Unpacking.InMotionV2ChecksumFailName,
        Level = LogLevel.Debug, Message = "InMotion V2 checksum mismatch, calc: {Calculated:X2}, packet: {Received:X2}")]
    public static partial void LogV2ChecksumFail(this ILogger logger, byte calculated, byte received);
}
