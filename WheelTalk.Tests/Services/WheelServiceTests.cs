using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Ports;
using WheelTalk.Core.Services;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Services;

/// <summary>
/// 1.4: 08.08.2026 разбор упёрся в то, что протокольные записи (Begode/InMotion's own polling,
/// KingSong's <c>RequestMissingIdentity</c>) log at Debug — invisible in a rider's Info-level
/// report — so there was no way to tell "never asked" from "asked, wheel never heard". These pin
/// down the handshake-window promotion to Info in <see cref="WheelService"/>: on before the first
/// snapshot, off after, and bounded so a handshake that never completes cannot flood the log.
/// </summary>
public class WheelServiceTests
{
    private static (FakeProtocolDecoder Protocol, Decoder Decoder, CapturingLogger<WheelService> Log) Build(FakeTransport transport)
    {
        var config = new AppWheelConfig();
        var time = new FakeTimeProvider();
        var state = new WheelState(config, time);
        var protocol = new FakeProtocolDecoder();
        var decoder = new Decoder(state, protocol, new NullEventSink(), NullLogger<Decoder>.Instance);
        var log = new CapturingLogger<WheelService>();
        _ = new WheelService(transport, decoder, log);
        return (protocol, decoder, log);
    }

    [Fact]
    public void A_protocol_initiated_write_before_the_first_snapshot_logs_Info_and_Debug_after()
    {
        var transport = new FakeTransport();
        var (protocol, decoder, log) = Build(transport);

        protocol.RaiseWriteRequested([0x01]);
        Assert.Equal(LogLevel.Information, Assert.Single(log.Entries).Level);

        // The handshake concludes — a real frame decodes into a snapshot.
        protocol.DecodesTo = true;
        decoder.Feed([0xAA]);

        protocol.RaiseWriteRequested([0x02]);
        Assert.Equal(LogLevel.Debug, log.Entries[^1].Level);
    }

    [Fact]
    public void A_write_abandoned_for_a_dead_link_before_the_first_snapshot_logs_Info_and_Debug_after()
    {
        var transport = new FakeTransport { FailWritesWith = new WriteLinkLostException() };
        var (protocol, decoder, log) = Build(transport);

        protocol.RaiseWriteRequested([0x01]);
        var abandoned = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, abandoned.Level);
        Assert.Contains("link gone", abandoned.Message);

        protocol.DecodesTo = true;
        decoder.Feed([0xAA]);

        protocol.RaiseWriteRequested([0x02]);
        Assert.Equal(LogLevel.Debug, log.Entries[^1].Level);
    }

    [Fact]
    public void A_write_that_does_not_fit_the_link_before_the_first_snapshot_logs_Info_and_Debug_after()
    {
        var transport = new FakeTransport { FailWritesWith = new WriteTooLongException(22, 20) };
        var (protocol, decoder, log) = Build(transport);

        protocol.RaiseWriteRequested([0x01]);
        var tooLong = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, tooLong.Level);
        Assert.Contains("does not fit", tooLong.Message);

        protocol.DecodesTo = true;
        decoder.Feed([0xAA]);

        protocol.RaiseWriteRequested([0x02]);
        Assert.Equal(LogLevel.Debug, log.Entries[^1].Level);
    }

    /// <summary>
    /// A handshake that never completes must not turn into an unbounded Info stream — InMotion's
    /// keep-alive alone re-fires every 25 ms (see InMotionDecoder's timer).
    /// </summary>
    [Fact]
    public void The_Info_promotion_stops_once_the_handshake_window_budget_is_spent()
    {
        var transport = new FakeTransport();
        var (protocol, _, log) = Build(transport);

        for (int i = 0; i < 30; i++)
        {
            protocol.RaiseWriteRequested([(byte)i]);
        }

        Assert.Equal(30, log.Entries.Count);
        int infoCount = log.Entries.Count(e => e.Level == LogLevel.Information);
        Assert.True(infoCount is > 0 and < 30, $"expected the budget to cap Info lines, got {infoCount} of 30");
        Assert.All(log.Entries.Skip(infoCount), e => Assert.Equal(LogLevel.Debug, e.Level));
    }

    private sealed class FakeProtocolDecoder : IWheelDecoder
    {
        public bool DecodesTo { get; set; }
        public event Action<byte[]>? WriteRequested;

        // Сторож связи кормится этим событием (bugfix-1 §1.1), а здесь под проверкой журнал
        // записи — поднимать его некому и незачем.
#pragma warning disable CS0067
        public event Action<byte[]>? FrameRecognized;
#pragma warning restore CS0067

        public bool Decode(byte[] data) => DecodesTo;
        public bool IsReady => true;

        public void RaiseWriteRequested(byte[] bytes) => WriteRequested?.Invoke(bytes);

        public byte[] BuildWheelBeep() => [];
        public byte[] BuildSetLightState(bool enabled) => [];
        public byte[] BuildSwitchFlashlight() => [];
        public byte[]? BuildUpdatePedalsMode(int mode) => null;
        public byte[]? BuildResetTrip() => null;
        public byte[]? BuildCalibrate() => null;
    }
}
