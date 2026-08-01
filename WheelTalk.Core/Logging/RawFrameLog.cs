using System.Globalization;

namespace WheelTalk.Core.Logging;

/// <summary>
/// The raw BLE dump format of the original WheelLog (<c>BluetoothService.readData</c>): one line
/// per notification, written before decoding. Lowercase hex without separators, because that is
/// what <c>StringUtil.toHexStringRaw</c> produces — a dump recorded on the phone has to replay
/// into the decoder unchanged, so both halves of that promise live here together.
/// </summary>
public static class RawFrameLog
{
    private const string TimeFormat = "HH:mm:ss.fff";

    public static string FormatLine(DateTimeOffset time, ReadOnlySpan<byte> frame) =>
        string.Concat(
            time.ToString(TimeFormat, CultureInfo.InvariantCulture),
            ",",
            Convert.ToHexStringLower(frame));

    /// <summary>
    /// Reads a line back. The dump carries no date, so the timestamp is a time of day — enough for
    /// the only thing a reader wants it for, the gap since the previous frame. Malformed lines are
    /// rejected rather than thrown on: a dump can end mid-line if the phone died with the file open.
    /// </summary>
    public static bool TryParseLine(string line, out TimeSpan time, out byte[] frame)
    {
        time = default;
        frame = [];

        int comma = line.IndexOf(',');
        if (comma < 0) return false;

        if (!TimeSpan.TryParseExact(line[..comma], @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out time))
        {
            return false;
        }

        try
        {
            frame = Convert.FromHexString(line[(comma + 1)..].Trim());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
