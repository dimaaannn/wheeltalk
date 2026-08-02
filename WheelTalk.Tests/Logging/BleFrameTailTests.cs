using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WheelTalk.Core.Detection;
using WheelTalk.Core.Logging;
using WheelTalk.Core.Services;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Logging;

/// <summary>
/// <see cref="BleFrameTail"/> end to end: a fake transport feeds it frames the way a real BLE
/// notification would, a real <see cref="WheelSession"/> supplies the MAC each frame is stamped
/// with, and <see cref="BleFrameTail.FormatSection"/> is checked both for its own shape (headers,
/// grouping) and for round-tripping through the same parser a real <c>RAW_*.csv</c> dump would.
/// </summary>
public class BleFrameTailTests
{
    private const string MacA = "D6:8D:7C:BB:7B:44";
    private const string MacB = "88:25:84:F0:5B:69";

    private static (BleFrameTail Tail, FakeTransport Transport, WheelSession Session) Build()
    {
        var transport = new FakeTransport();
        var time = new FakeTimeProvider();
        var session = new WheelSession(
            transport,
            new AppWheelConfig(),
            new NullEventSink(),
            time,
            new ConnectionOptions(),
            new WheelDetector(NullLogger<WheelDetector>.Instance),
            NullLoggerFactory.Instance);

        var tail = new BleFrameTail(transport, session, time);
        return (tail, transport, session);
    }

    [Fact]
    public async Task An_empty_ring_formats_to_an_empty_section()
    {
        var (tail, _, _) = Build();

        Assert.Equal("", tail.FormatSection());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Connecting_a_different_wheel_empties_the_ring()
    {
        var (tail, transport, session) = Build();

        await session.ConnectAsync(MacA);
        transport.Deliver("aa5501", "aa5502");

        await session.ConnectAsync(MacB);
        transport.Deliver("55aa03");

        string[] lines = tail.FormatSection().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal($"----- кадры BLE (кольцо {BleFrameTail.Capacity}) -----", lines[0]);
        Assert.Equal($"· {MacB}", lines[1]);
        Assert.Contains("55aa03", lines[2]);
        Assert.Equal(3, lines.Length);
    }

    /// <summary>Обрыв и переподключение к тому же колесу — тот же разговор, и хвост до обрыва
    /// обычно и есть самое интересное в нём.</summary>
    [Fact]
    public async Task Reconnecting_the_same_wheel_keeps_what_was_already_collected()
    {
        var (tail, transport, session) = Build();

        await session.ConnectAsync(MacA);
        transport.Deliver("aa5501");

        await session.ConnectAsync(MacA);
        transport.Deliver("aa5502");

        string[] frameLines = tail.FormatSection()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains(','))
            .ToArray();

        Assert.Equal(2, frameLines.Length);
        Assert.Contains("aa5501", frameLines[0]);
        Assert.Contains("aa5502", frameLines[1]);
    }

    [Fact]
    public async Task Frame_lines_round_trip_through_RawFrameLog_TryParseLine()
    {
        var (tail, transport, session) = Build();
        await session.ConnectAsync(MacA);
        transport.Deliver("aabbccdd", "1122334455");

        string[] frameLines = tail.FormatSection()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains(','))
            .ToArray();

        Assert.Equal(2, frameLines.Length);
        Assert.True(RawFrameLog.TryParseLine(frameLines[0], out _, out byte[] first));
        Assert.Equal("AABBCCDD", Convert.ToHexString(first));
        Assert.True(RawFrameLog.TryParseLine(frameLines[1], out _, out byte[] second));
        Assert.Equal("1122334455", Convert.ToHexString(second));
    }

    /// <summary>The header line itself has no comma, so a naive line-by-line replay silently
    /// skips it — exactly the point of putting the MAC on its own line instead of widening the
    /// frame-line format.</summary>
    [Fact]
    public async Task The_mac_header_line_is_rejected_by_the_frame_line_parser()
    {
        var (tail, transport, session) = Build();
        await session.ConnectAsync(MacA);
        transport.Deliver("aa5501");

        string headerLine = tail.FormatSection()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith('·'));

        Assert.False(RawFrameLog.TryParseLine(headerLine, out _, out _));
    }

    [Fact]
    public async Task Overflowing_the_ring_keeps_only_the_last_capacity_frames()
    {
        var (tail, transport, session) = Build();
        await session.ConnectAsync(MacA);

        for (int i = 0; i < BleFrameTail.Capacity + 20; i++)
        {
            transport.Deliver(Convert.ToHexString(BitConverter.GetBytes(i)));
        }

        int frameLineCount = tail.FormatSection()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains(','));

        Assert.Equal(BleFrameTail.Capacity, frameLineCount);
    }
}
