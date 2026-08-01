namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of MathsUtil.java 1:1 — only the byte helpers needed by the Veteran and Gotway/Begode decoders.
/// </summary>
public static class MathsUtil
{
    public static int ShortFromBytesBE(byte[] bytes, int starting)
    {
        if (bytes.Length >= starting + 2)
        {
            return ((bytes[starting] & 0xFF) << 8) | (bytes[starting + 1] & 0xFF);
        }
        return 0;
    }

    public static int SignedShortFromBytesBE(byte[] bytes, int starting)
    {
        if (bytes.Length >= starting + 2)
        {
            return ((sbyte)bytes[starting] << 8) | (bytes[starting + 1] & 0xFF);
        }
        return 0;
    }

    public static int IntFromBytesRevBE(byte[] bytes, int starting)
    {
        if (bytes.Length >= starting + 4)
        {
            return ((bytes[starting + 2] & 0xFF) << 24) | ((bytes[starting + 3] & 0xFF) << 16)
                 | ((bytes[starting] & 0xFF) << 8) | (bytes[starting + 1] & 0xFF);
        }
        return 0;
    }

    public static long IntFromBytesBE(byte[] bytes, int starting)
    {
        if (bytes.Length >= starting + 4)
        {
            return (((uint)(bytes[starting] & 0xFF) << 24) | ((uint)(bytes[starting + 1] & 0xFF) << 16)
                  | ((uint)(bytes[starting + 2] & 0xFF) << 8) | (uint)(bytes[starting + 3] & 0xFF));
        }
        return 0;
    }

    /// <summary>Port of MathsUtil.getInt4 — signed 32-bit BE read (Java ByteBuffer.getInt() default order).</summary>
    public static int GetInt4(byte[] bytes, int starting)
    {
        if (bytes.Length >= starting + 4)
        {
            return (bytes[starting] << 24) | (bytes[starting + 1] << 16) | (bytes[starting + 2] << 8) | bytes[starting + 3];
        }
        return 0;
    }

    /// <summary>
    /// Port of MathsUtil.getInt2R — reverseEvery2 then ByteBuffer.getShort(): signed 16-bit,
    /// low byte at <paramref name="starting"/>, high byte at <paramref name="starting"/>+1 (KingSong's
    /// little-endian field order).
    /// </summary>
    public static int GetInt2R(byte[] bytes, int starting)
    {
        if (bytes.Length >= starting + 2)
        {
            return ((sbyte)bytes[starting + 1] << 8) | (bytes[starting] & 0xFF);
        }
        return 0;
    }

    /// <summary>
    /// Port of MathsUtil.getInt4R — reverseEvery2 swaps each byte pair ((0,1) and (2,3)) before a
    /// big-endian 32-bit read, landing on {b1,b0,b3,b2} (KingSong's total-distance field order).
    /// </summary>
    public static int GetInt4R(byte[] bytes, int starting)
    {
        if (bytes.Length >= starting + 4)
        {
            return ((bytes[starting + 1] & 0xFF) << 24) | ((bytes[starting] & 0xFF) << 16)
                 | ((bytes[starting + 3] & 0xFF) << 8) | (bytes[starting + 2] & 0xFF);
        }
        return 0;
    }

    public static int Clamp(int val, int min, int max) => Math.Max(min, Math.Min(max, val));
    public static double Clamp(double val, double min, double max) => Math.Max(min, Math.Min(max, val));

    /// <summary>
    /// Port of MathsUtil.intFromBytesLE — signed 32-bit little-endian read (InMotion's CAN field
    /// order). MathsUtil.signedIntFromBytesLE in the original computes the exact same bit pattern,
    /// just widened to a Java <c>long</c> at the return — a distinction that doesn't exist in C#,
    /// so both original call sites use this one method.
    /// </summary>
    public static int IntFromBytesLE(byte[] bytes, int starting)
    {
        if (bytes.Length >= starting + 4)
        {
            return ((bytes[starting + 3] & 0xFF) << 24) | ((bytes[starting + 2] & 0xFF) << 16)
                 | ((bytes[starting + 1] & 0xFF) << 8) | (bytes[starting] & 0xFF);
        }
        return 0;
    }

    /// <summary>Port of MathsUtil.shortFromBytesLE — unsigned 16-bit little-endian read (InMotion V2's field order).</summary>
    public static int ShortFromBytesLE(byte[] bytes, int starting)
    {
        if (bytes.Length >= starting + 2)
        {
            return ((bytes[starting + 1] & 0xFF) << 8) | (bytes[starting] & 0xFF);
        }
        return 0;
    }

    /// <summary>Port of MathsUtil.signedShortFromBytesLE — 16-bit little-endian read, sign-extending the high byte.</summary>
    public static int SignedShortFromBytesLE(byte[] bytes, int starting)
    {
        if (bytes.Length >= starting + 2)
        {
            return ((sbyte)bytes[starting + 1] << 8) | (bytes[starting] & 0xFF);
        }
        return 0;
    }

    /// <summary>Port of MathsUtil.intFromBytesRevLE — swaps each byte pair ((0,1) and (2,3)) before a
    /// little-endian 32-bit read, landing on {b1,b0,b3,b2} (InMotion V2 V13's mileage field order —
    /// distinct from <see cref="IntFromBytesRevBE"/>, which is a different byte order entirely).</summary>
    public static int IntFromBytesRevLE(byte[] bytes, int starting)
    {
        if (bytes.Length >= starting + 4)
        {
            return ((bytes[starting + 1] & 0xFF) << 24) | ((bytes[starting] & 0xFF) << 16)
                 | ((bytes[starting + 3] & 0xFF) << 8) | (bytes[starting + 2] & 0xFF);
        }
        return 0;
    }

    /// <summary>Port of MathsUtil.longFromBytesLE — unsigned 64-bit little-endian read.</summary>
    public static long LongFromBytesLE(byte[] bytes, int starting)
    {
        if (bytes.Length >= starting + 8)
        {
            long result = 0;
            for (int i = 7; i >= 0; i--)
            {
                result = (result << 8) | (uint)(bytes[starting + i] & 0xFF);
            }
            return result;
        }
        return 0;
    }
}
