using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WheelTalk.Tests.TestSupport;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Decoding;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Begode и Veteran делят профиль `FFE0`/`FFE1`, поэтому по дереву GATT они неразличимы, и
/// протокол называет первый пришедший кадр. Порт `GotwayVirtualAdapter` оригинала — здесь
/// проверяется то, ради чего он существует: что заголовок читается правильно, что до него мы
/// честно молчим, и что после него всё уходит настоящему декодеру.
/// </summary>
public class AutoDecoderTests
{
    /// <summary>Первый кадр Sherman L — заголовок DC 5A 5C.</summary>
    private static byte[] VeteranFrame() => Convert.FromHexString("dc5a5c53397afffe0aa400000df10000000a0b3d");

    /// <summary>Первый кадр MTen3 — заголовок 55 AA.</summary>
    private static byte[] GotwayFrame() => Convert.FromHexString("55aa00000000000000000000000000000000181e");

    /// <summary>Целый кадр MTen3 (из <see cref="GotwayDecoderTests"/>): по его завершении декодер и опрашивает колесо.</summary>
    private static readonly string[] GotwayPacket =
    [
        "55AA19C1000000000000008CF0000001FFF80018",
        "5A5A5A5A55AA000060D248001C20006400010007",
    ];

    private static AutoDecoder Build(out WheelState state)
    {
        var config = new AppWheelConfig();
        var time = new FakeTimeProvider();
        state = new WheelState(config, time);
        return new AutoDecoder(state, config, time, NullLoggerFactory.Instance);
    }

    [Fact]
    public void A_veteran_header_names_the_protocol()
    {
        var decoder = Build(out _);

        decoder.Decode(VeteranFrame());

        Assert.Equal(WheelProtocol.Veteran, decoder.Protocol);
    }

    [Fact]
    public void A_gotway_header_names_the_protocol()
    {
        var decoder = Build(out _);

        decoder.Decode(GotwayFrame());

        Assert.Equal(WheelProtocol.Gotway, decoder.Protocol);
    }

    [Fact]
    public void The_protocol_is_announced_once()
    {
        var decoder = Build(out _);
        var announced = new List<WheelProtocol>();
        decoder.Detected += announced.Add;

        decoder.Decode(VeteranFrame());
        decoder.Decode(VeteranFrame());
        decoder.Decode(VeteranFrame());

        Assert.Equal([WheelProtocol.Veteran], announced);
    }

    [Fact]
    public void Bytes_that_are_neither_leave_the_protocol_unknown()
    {
        var decoder = Build(out _);

        // Обрывок чужого кадра — не повод угадывать: ждём следующий.
        Assert.False(decoder.Decode([0x01, 0x02, 0x03, 0x04]));
        Assert.Null(decoder.Protocol);
        Assert.False(decoder.IsReady);
    }

    [Fact]
    public void A_frame_too_short_to_carry_a_header_is_not_guessed()
    {
        var decoder = Build(out _);

        Assert.False(decoder.Decode([0xDC]));
        Assert.False(decoder.Decode([0xDC, 0x5A]));
        Assert.Null(decoder.Protocol);
    }

    /// <summary>
    /// Кадр, назвавший протокол, не выбрасывается: оригинал передаёт его тому же декодеру, и
    /// первое показание приходит с него, а не со следующего.
    /// </summary>
    [Fact]
    public void The_frame_that_named_the_protocol_is_decoded_too()
    {
        var decoder = Build(out var state);

        foreach (string chunk in new[]
        {
            "dc5a5c53397afffe0aa400000df10000000a0b3d",
            "0e0e0000037a035217730064000e00b480c80000",
            "808080808080058080808080800ff30ff50ff50f",
            "f50ff50fef0ff20ff30ff30ff30ff30fed0ff30f",
            "f40ff5378c5145",
        })
        {
            decoder.Decode(Convert.FromHexString(chunk));
        }

        Assert.Equal("Sherman L", state.Model);
    }

    [Fact]
    public void Commands_asked_for_before_the_first_frame_refuse_instead_of_guessing()
    {
        var decoder = Build(out _);

        Assert.Throws<ProtocolNotDetectedException>(() => decoder.BuildWheelBeep());
        Assert.Throws<ProtocolNotDetectedException>(() => decoder.BuildSetLightState(true));
    }

    [Fact]
    public void Commands_after_detection_are_the_real_protocols_own()
    {
        var decoder = Build(out _);
        decoder.Decode(VeteranFrame());

        // "SetLightON" — вет��ранская команда; у Begode она другая, и это доказывает, что внутри
        // именно тот декодер.
        Assert.Equal("SetLightON", System.Text.Encoding.ASCII.GetString(decoder.BuildSetLightState(true)));
    }

    /// <summary>
    /// Gotway пишет в колесо по своей инициативе — опрос «V»/«N» и вторая половина калибровки.
    /// Подписка живёт на внешнем декодере, а поднимает событие внутренний: без переброски эти
    /// записи молча пропали бы.
    /// </summary>
    [Fact]
    public void Writes_the_inner_decoder_asks_for_reach_the_outside()
    {
        var decoder = Build(out _);
        var requested = new List<byte[]>();
        decoder.WriteRequested += requested.Add;

        // Опрос идёт по завершении кадра, поэтому кадр нужен целый, а не только его заголовок.
        foreach (string chunk in GotwayPacket) decoder.Decode(Convert.FromHexString(chunk));

        Assert.Equal(WheelProtocol.Gotway, decoder.Protocol);
        Assert.NotEmpty(requested);
    }
}
