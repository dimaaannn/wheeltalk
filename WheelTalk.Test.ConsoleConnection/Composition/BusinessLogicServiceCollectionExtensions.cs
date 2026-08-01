using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WheelTalk.Ble;
using WheelTalk.Configuration;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Ports;
using WheelTalk.Core.Services;
using WheelTalk.Debug;

namespace WheelTalk.Composition;

/// <summary>
/// Registers the wheel-decoding business logic (state, protocol decoder, event sink, service,
/// presenter, test-harness scenarios) on top of whatever shared infrastructure
/// (WheelTalkOptions/IWheelConfig/TimeProvider/ITransport/...) <c>Program.Main</c> already
/// registered. Kept in its own file so the composition root reads as "shared infrastructure"
/// (Main) + "domain wiring" (here) instead of one large undifferentiated block.
/// </summary>
public static class BusinessLogicServiceCollectionExtensions
{
    public static IServiceCollection AddWheelBusinessLogic(this IServiceCollection services)
    {
        services.AddSingleton<WheelState>();
        services.AddSingleton<IEventSink, LoggingEventSink>();

        // "Veteran" (Sherman L) or "Begode" (MTen3, etc.) — same appsettings.json, switch by hand
        // between test sessions since only one wheel is targeted at a time in this test port.
        services.AddSingleton<IWheelDecoder>(sp => WheelDecoderFactory.Create(
            sp.GetRequiredService<WheelTalkOptions>().Protocol,
            sp.GetRequiredService<WheelState>(),
            sp.GetRequiredService<IWheelConfig>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILoggerFactory>()));

        services.AddSingleton<Decoder>();
        services.AddSingleton<WheelService>();
        services.AddSingleton<ConsolePresenter>();

        // TestHarness's own constructor shape is unchanged — the wheel address is just pulled off
        // the typed options here instead of being read from IConfiguration by hand.
        services.AddSingleton(sp => new TestHarness(
            sp.GetRequiredService<WindowsBleClient>(),
            sp.GetRequiredService<WheelService>(),
            sp.GetRequiredService<Decoder>(),
            sp.GetRequiredService<ConsolePresenter>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<WheelTalkOptions>().WheelAddress));

        return services;
    }
}
