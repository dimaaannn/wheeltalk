using WheelTalk.Core.Logging;

namespace WheelTalk.Tests.Logging;

/// <summary>
/// The dump is only worth writing if WheelLog's own tooling and our RawReplayTransport can read
/// it back, so the line is pinned character by character against the original format
/// ("HH:mm:ss.SSS,&lt;hex&gt;", Locale.US, lowercase hex without separators).
/// </summary>
public class RawFrameLogTests
{
    private static readonly DateTimeOffset Moment =
        new(2026, 7, 27, 22, 5, 3, 40, TimeSpan.FromHours(3));

    [Fact]
    public void Writes_time_and_lowercase_hex_without_separators()
    {
        byte[] frame = Convert.FromHexString("DC5A5C20266D00004AAF");

        Assert.Equal("22:05:03.040,dc5a5c20266d00004aaf", RawFrameLog.FormatLine(Moment, frame));
    }
}
