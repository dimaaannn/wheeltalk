using Microsoft.Extensions.Logging;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Diagnostics;
using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Single place that maps a <see cref="WheelProtocol"/> to its <see cref="IWheelDecoder"/>.
/// Lives in Core (not in the console app's composition root) so every host — the Windows console
/// app, the tests' DecoderHarness, the planned Android app — selects the decoder the same way
/// instead of each re-implementing the same if/else over a config string.
/// </summary>
public static class WheelDecoderFactory
{
    /// <summary>
    /// Builds the decoder for <paramref name="protocol"/> against a caller-owned
    /// <paramref name="state"/> and logs the choice (one line per session, so the log makes it
    /// obvious which protocol a recorded run was decoded with).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">No decoder is ported for this protocol.</exception>
    public static IWheelDecoder Create(WheelProtocol protocol, WheelState state, IWheelConfig config,
        TimeProvider timeProvider, ILoggerFactory loggerFactory)
    {
        var protocolSelected = new EventId(LogEvents.Service.ProtocolSelectedId, LogEvents.Service.ProtocolSelectedName);

        switch (protocol)
        {
            case WheelProtocol.Gotway:
            {
                var logger = loggerFactory.CreateLogger<GotwayDecoder>();
                logger.LogInformation(protocolSelected, "Protocol.Selected {Protocol}", protocol);
                return new GotwayDecoder(state, config, timeProvider, logger);
            }
            case WheelProtocol.Veteran:
            {
                var logger = loggerFactory.CreateLogger<VeteranDecoder>();
                logger.LogInformation(protocolSelected, "Protocol.Selected {Protocol}", protocol);
                return new VeteranDecoder(state, config, timeProvider, logger);
            }
            case WheelProtocol.KingSong:
            {
                var logger = loggerFactory.CreateLogger<KingsongDecoder>();
                logger.LogInformation(protocolSelected, "Protocol.Selected {Protocol}", protocol);
                return new KingsongDecoder(state, config, logger);
            }
            case WheelProtocol.InMotion:
            {
                var logger = loggerFactory.CreateLogger<InMotionDecoder>();
                logger.LogInformation(protocolSelected, "Protocol.Selected {Protocol}", protocol);
                return new InMotionDecoder(state, config, timeProvider, logger);
            }
            case WheelProtocol.InMotionV2:
            {
                var logger = loggerFactory.CreateLogger<InMotionDecoderV2>();
                logger.LogInformation(protocolSelected, "Protocol.Selected {Protocol}", protocol);
                return new InMotionDecoderV2(state, config, timeProvider, logger);
            }
            case WheelProtocol.InMotionV2_1:
            {
                var logger = loggerFactory.CreateLogger<InMotionDecoderV2_1>();
                logger.LogInformation(protocolSelected, "Protocol.Selected {Protocol}", protocol);
                return new InMotionDecoderV2_1(state, config, timeProvider, loggerFactory);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(protocol), protocol,
                    $"No decoder is ported for protocol '{protocol}'. Supported: " +
                    $"{WheelProtocol.Veteran}, {WheelProtocol.Gotway}, {WheelProtocol.KingSong}, {WheelProtocol.InMotion}, " +
                    $"{WheelProtocol.InMotionV2}, {WheelProtocol.InMotionV2_1}.");
        }
    }
}
