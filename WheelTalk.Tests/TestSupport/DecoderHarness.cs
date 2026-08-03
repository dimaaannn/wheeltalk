using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Services;

namespace WheelTalk.Tests.TestSupport;

/// <summary>
/// Wires a protocol decoder to a fresh WheelState/Decoder pair the same way Program.Build()
/// does, minus BLE — feed raw frame bytes via <see cref="Decoder"/>, then read back a
/// <see cref="TelemetrySnapshot"/> of the accumulated state (mirrors how the original
/// *AdapterTest.kt fixtures assert on WheelData after a sequence of decode() calls, not on
/// the return value of any single call).
/// </summary>
public sealed class DecoderHarness
{
    private readonly WheelState _state;

    public AppWheelConfig Config { get; }
    public Decoder Decoder { get; }

    /// <summary>Часы декодера — их двигают те тесты, что проверяют его собственные таймеры
    /// (рукопожатие KingSong, опрос InMotion); остальным довольно того, что они стоят.</summary>
    public FakeTimeProvider Time { get; }

    private DecoderHarness(AppWheelConfig config, WheelState state, Decoder decoder, FakeTimeProvider time)
    {
        Config = config;
        _state = state;
        Decoder = decoder;
        Time = time;
    }

    public static DecoderHarness ForGotway(Action<AppWheelConfig>? configure = null)
    {
        var config = new AppWheelConfig
        {
            // "1", а не поставочный "0", и это не рассогласование: харнесс повторяет setUp()
            // тестов оригинала (`GotwayAdapterTest.kt:38` ставит "1" явно), потому что от него
            // зависят ожидания портированных фикстур — знаковые −11.8, 9.5, 8.1. Умолчание
            // приложения живёт в appsettings.json и повторяет `AppConfig`, а это разные вещи:
            // одно про «как оно поставляется», другое про «на чём проверяется декодер».
            GotwayNegative = "1",
            AutoVoltage = true,
            // Matches the *unstubbed* mockk-relaxed default in GotwayAdapterTest.kt (a relaxed
            // mock returns "" for an unspecified String property), which falls into
            // GotwayDecoder.GetScaledVoltage's `_ => 1.0` branch — same as explicit "0".
            GotwayVoltage = "0",
        };
        configure?.Invoke(config);

        // Delayed follow-up commands (e.g. "b" after a light-mode command) aren't under test
        // here, so it's fine that this FakeTimeProvider is never advanced — those Task.Delay
        // calls are fire-and-forget in production and just stay pending.
        var timeProvider = new FakeTimeProvider();
        return Build(WheelProtocol.Gotway, config, timeProvider);
    }

    public static DecoderHarness ForVeteran(Action<AppWheelConfig>? configure = null)
    {
        var config = new AppWheelConfig
        {
            // "1", а не поставочный "0", и это не рассогласование: харнесс повторяет setUp()
            // тестов оригинала (`GotwayAdapterTest.kt:38` ставит "1" явно), потому что от него
            // зависят ожидания портированных фикстур — знаковые −11.8, 9.5, 8.1. Умолчание
            // приложения живёт в appsettings.json и повторяет `AppConfig`, а это разные вещи:
            // одно про «как оно поставляется», другое про «на чём проверяется декодер».
            GotwayNegative = "1",
        };
        configure?.Invoke(config);

        var timeProvider = new FakeTimeProvider();
        return Build(WheelProtocol.Veteran, config, timeProvider);
    }

    public static DecoderHarness ForKingSong(Action<AppWheelConfig>? configure = null)
    {
        // Matches KingsongAdapterTest.kt's setUp(): a relaxed mockk AppConfig, whose unstubbed
        // Boolean getters (UseBetterPercents, CustomPercents) return false — the defaults here.
        var config = new AppWheelConfig();
        configure?.Invoke(config);

        var timeProvider = new FakeTimeProvider();
        return Build(WheelProtocol.KingSong, config, timeProvider);
    }

    public static DecoderHarness ForInMotion(Action<AppWheelConfig>? configure = null)
    {
        // Matches InmotionAdapterTest.kt's setUp(): a relaxed mockk AppConfig, whose unstubbed
        // Boolean getters (UseBetterPercents) return false — the default here. The keep-alive timer
        // this decoder starts on construction never advances (this FakeTimeProvider is never
        // advanced by these tests), so it never actually ticks — fine, since none of the byte-in/
        // value-out fixtures depend on the poll, only on feeding frames directly.
        var config = new AppWheelConfig();
        configure?.Invoke(config);

        var timeProvider = new FakeTimeProvider();
        return Build(WheelProtocol.InMotion, config, timeProvider);
    }

    public static DecoderHarness ForInMotionV2(Action<AppWheelConfig>? configure = null)
    {
        // Matches InmotionAdapterV2Test.kt's setUp(): a relaxed mockk AppConfig, whose unstubbed
        // Boolean getters (UseBetterPercents) return false — the default here. Same as
        // ForInMotion, the keep-alive timer this decoder starts never actually ticks in tests
        // (this FakeTimeProvider is never advanced) — fine, none of the byte-in/value-out fixtures
        // depend on the poll, only on feeding frames directly.
        var config = new AppWheelConfig();
        configure?.Invoke(config);

        var timeProvider = new FakeTimeProvider();
        return Build(WheelProtocol.InMotionV2, config, timeProvider);
    }

    public static DecoderHarness ForInMotionV2_1(Action<AppWheelConfig>? configure = null)
    {
        var config = new AppWheelConfig();
        configure?.Invoke(config);

        var timeProvider = new FakeTimeProvider();
        return Build(WheelProtocol.InMotionV2_1, config, timeProvider);
    }

    /// <summary>Same decoder selection the composition root uses (<see cref="WheelDecoderFactory"/>).</summary>
    private static DecoderHarness Build(WheelProtocol protocol, AppWheelConfig config, FakeTimeProvider timeProvider)
    {
        var state = new WheelState(config, timeProvider);
        var protocolDecoder = WheelDecoderFactory.Create(protocol, state, config, timeProvider, NullLoggerFactory.Instance);
        var decoder = new Decoder(state, protocolDecoder, new NullEventSink(), NullLogger<Decoder>.Instance);
        return new DecoderHarness(config, state, decoder, timeProvider);
    }

    /// <summary>Feeds each hex string as one frame, in order.</summary>
    public void FeedHex(params string[] frames)
    {
        foreach (string frame in frames)
        {
            Decoder.Feed(Convert.FromHexString(frame));
        }
    }

    public TelemetrySnapshot Snapshot() => _state.ToSnapshot();
}
