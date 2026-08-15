using WheelTalk.Core.Decoding;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Ports the KingSong fixtures from the original Android KingsongAdapterTest.kt that were used to
/// cross-check KingsongDecoder 1:1 (see AGENTS.md, "Как проверять изменения в декодере") — pinned
/// here as permanent regression tests instead of the one-off scratch project used then.
/// <para>
/// The synthetic (non-hex-literal) Kotlin fixtures build their frame bytes at test time via
/// <c>MathsUtil.getBytes</c>/<c>reverseEvery2</c> helpers this port doesn't carry into test code —
/// each such frame here is the exact byte-for-byte result of running that same construction once
/// and hex-encoding it, so the frame content is unchanged, only pre-computed.
/// </para>
/// </summary>
public class KingsongDecoderTests
{
    [Fact]
    public void Corrupted_data_never_decodes()
    {
        var harness = DecoderHarness.ForKingSong();
        var bytes = new List<byte>();
        for (int i = 0; i <= 29; i++)
        {
            bytes.Add((byte)i);
            Assert.False(harness.Decoder.ProtocolDecoder.Decode(bytes.ToArray()));
        }
    }

    [Fact]
    public void Decodes_live_data()
    {
        var harness = DecoderHarness.ForKingSong();
        bool result = harness.Decoder.ProtocolDecoder.Decode(
            Convert.FromHexString("aa5570176f009649d2020b0a39300f0ea9100000"));

        Assert.True(result);
        var snapshot = harness.Snapshot();
        Assert.Equal(60.0, snapshot.VoltageV, 2);
        Assert.Equal(11, Math.Abs(snapshot.RoundedSpeed()));
        Assert.Equal(123, snapshot.TemperatureC);
        Assert.Equal(0, snapshot.Temperature2C);
        Assert.Equal(1234567.89, snapshot.TotalDistanceKm, 2);
        Assert.Equal(62, snapshot.Battery);
    }

    [Fact]
    public void Decodes_distance_time_fan_data()
    {
        var harness = DecoderHarness.ForKingSong();
        bool result = harness.Decoder.ProtocolDecoder.Decode(
            Convert.FromHexString("aa559649d202070630750b0a410c0f0eb9100000"));

        Assert.False(result);
        var snapshot = harness.Snapshot();
        Assert.Equal(1234567.89, snapshot.WheelDistanceKm, 2);
        Assert.Equal(300.0, snapshot.TopSpeedKmh, 2);
        Assert.Equal(65, snapshot.FanStatus);
    }

    [Fact]
    public void Decodes_name_and_model_data()
    {
        var harness = DecoderHarness.ForKingSong();
        bool result = harness.Decoder.ProtocolDecoder.Decode(
            Convert.FromHexString("aa550253757065722d576865656c3132bb100000"));

        Assert.False(result);
        var snapshot = harness.Snapshot();
        Assert.Equal("Super-Wheel12", snapshot.Name);
        Assert.Equal("Super", snapshot.Model);
    }

    [Fact]
    public void Decodes_serial_number()
    {
        var harness = DecoderHarness.ForKingSong();
        bool result = harness.Decoder.ProtocolDecoder.Decode(
            Convert.FromHexString("aa554b696e6731323334353637383930b3313233"));

        Assert.False(result);
        var snapshot = harness.Snapshot();
        Assert.Equal("King1234567890123", snapshot.Serial.TrimEnd('\0'));
    }

    /// <summary>
    /// Frame 0xB5 — four 16-bit fields (speedLevel1/2/3, waneSpeed), plan 35 §10, port-deviations.md.
    /// Owner decision (plan 21 §7 q3): alarm tiers/tiltback are not ported — the wheel beeps on its
    /// own thresholds, set from the stock app. This frame is still recognized (newDataFound stays
    /// true, matching the original) but nothing in it is <see cref="WheelState"/> telemetry, so
    /// nothing is written there — the parsed fields are only visible via <see cref="KingsongDecoder.KsAlarmAndWaneSpeed"/>.
    /// </summary>
    [Fact]
    public void Decodes_speed_alarm_frame_as_16_bit_fields()
    {
        var harness = DecoderHarness.ForKingSong();
        var decoder = (KingsongDecoder)harness.Decoder.ProtocolDecoder;
        byte[] frame = Convert.FromHexString("aa55000005010a000f02140000000000b5145a5a");

        bool result = decoder.Decode(frame);

        Assert.True(result);
        // Было (KingsongAdapter.java, портировано побайтово): buff[4]/[6]/[8]/[10] & 0xFF — читает
        // только младший байт каждого поля.
        Assert.Equal((5, 10, 15, 20), (frame[4] & 0xFF, frame[6] & 0xFF, frame[8] & 0xFF, frame[10] & 0xFF));
        // Теперь (MainFragmentAty.java:8688-8747+): полные 16-битные поля — на смещениях 4 и 8
        // старший байт ненулевой, и старое чтение теряло его молча.
        Assert.Equal((261, 10, 527, 20), decoder.KsAlarmAndWaneSpeed);
    }

    /// <summary>
    /// Frame 0xA4 — calibration/settings acknowledgement (two 0/1 flags at [2]/[3]), NOT the same
    /// layout as 0xB5 despite sharing a handler before plan 35 §10. Recognized (newDataFound true,
    /// matching the original's own return for this type) but writes nothing to <see cref="WheelState"/>.
    /// </summary>
    [Fact]
    public void Decodes_calibration_ack_frame_separately_from_speed_alarm()
    {
        var harness = DecoderHarness.ForKingSong();
        var decoder = (KingsongDecoder)harness.Decoder.ProtocolDecoder;

        bool result = decoder.Decode(
            Convert.FromHexString("aa5501000405060708090a0b0c0d0e0fa4145a5a"));

        Assert.True(result);
        Assert.Equal((1, 0), decoder.KsCalibrationAck);
        // 0xA4 не трогает раскладку 0xB5 — их разбор больше не пересекается.
        Assert.Equal((0, 0, 0, 0), decoder.KsAlarmAndWaneSpeed);
    }

    [Fact]
    public void Decodes_real_data_1()
    {
        var harness = DecoderHarness.ForKingSong();
        var decoder = harness.Decoder.ProtocolDecoder;

        bool result1 = decoder.Decode(Convert.FromHexString("aa554b532d5331382d30323035000000bb1484fd")); // model name
        bool result2 = decoder.Decode(Convert.FromHexString("aa556919030200009f36d700140500e0a9145a5a")); // live data
        bool result3 = decoder.Decode(Convert.FromHexString("aa550000090017011502140100004006b9145a5a")); // dist/fan/time
        bool result4 = decoder.Decode(Convert.FromHexString("aa55000000000000000000000000400cf5145a5a")); // cpu load
        bool result5 = decoder.Decode(Convert.FromHexString("aa55850c010000000000000016000000f6145a5a")); // output

        Assert.False(result1);
        Assert.True(result2);
        Assert.False(result3);
        Assert.False(result4);
        Assert.False(result5);

        var snapshot = harness.Snapshot();
        // 1st data
        Assert.Equal("KS-S18-0205", snapshot.Name);
        Assert.Equal("KS-S18", snapshot.Model);
        Assert.Equal("2.05", snapshot.Version);
        // 2nd data
        Assert.Equal(5.15, snapshot.SpeedKmh, 2);
        Assert.Equal(13, snapshot.TemperatureC);
        Assert.Equal(65.05, snapshot.VoltageV, 2);
        Assert.Equal(2.15, snapshot.CurrentA, 2);
        Assert.Equal(13983, snapshot.TotalDistance);
        Assert.Equal(12, snapshot.Battery);
        Assert.Equal("0", snapshot.ModeStr);
        // 3rd data
        Assert.Equal(16, snapshot.Temperature2C);
        Assert.Equal(0, snapshot.FanStatus);
        Assert.Equal(0, snapshot.ChargingStatus);
        Assert.Equal(0.009, snapshot.WheelDistanceKm, 3);
        // 4th data
        Assert.Equal(64, snapshot.CpuLoad);
        Assert.Equal(12, snapshot.Output);
        // 5th data
        Assert.Equal(32.05, snapshot.SpeedLimit, 2);
    }

    /// <summary>
    /// Frame 0xF6 also carries the wheel trouble code at offset 14-15 (docs/kingsong-trouble-codes.md,
    /// plan 35 §10) — the port used to read only the speed limit at offset 2-3 and ignore it entirely.
    /// Zero is silence, not "keep whatever alert was showing".
    /// </summary>
    [Fact]
    public void Zero_trouble_code_clears_the_alert()
    {
        var harness = DecoderHarness.ForKingSong();
        // Было: смещение 14-15 не читалось вовсе, SetAlert для этого кадра не вызывался.
        bool result = harness.Decoder.ProtocolDecoder.Decode(
            Convert.FromHexString("aa55850c000000000000000000000000f6145a5a"));

        Assert.False(result);
        Assert.Equal(32.05, harness.Snapshot().SpeedLimit, 2);
        Assert.Equal("", harness.Snapshot().Alert);
    }

    /// <summary>Known code (213) resolves to the manufacturer's own English text (ks-troublecode.json,
    /// kingsong-trouble-codes.md §1) — dictionary text, not the bare number.</summary>
    [Fact]
    public void Known_trouble_code_sets_the_alert_text()
    {
        var harness = DecoderHarness.ForKingSong();
        bool result = harness.Decoder.ProtocolDecoder.Decode(
            Convert.FromHexString("aa55850c00000000000000000000d500f6145a5a"));

        Assert.False(result);
        Assert.Equal("bms setting err", harness.Snapshot().Alert);
    }

    /// <summary>Code outside the 66-entry dictionary — shown as a number, not silently dropped.</summary>
    [Fact]
    public void Unknown_trouble_code_shows_the_number()
    {
        var harness = DecoderHarness.ForKingSong();
        bool result = harness.Decoder.ProtocolDecoder.Decode(
            Convert.FromHexString("aa55850c000000000000000000000f27f6145a5a"));

        Assert.False(result);
        Assert.Equal("Ошибка колеса 9999", harness.Snapshot().Alert);
    }

    /// <summary>BMS frames are out of this slice's scope (see KingsongDecoder's class doc) — every
    /// original fixture for them asserts only that decode() falls through to false, no field
    /// values, so that's all these check too.</summary>
    private static void AssertAllFalse(DecoderHarness harness, params string[] hexFrames)
    {
        foreach (string hex in hexFrames)
        {
            Assert.False(harness.Decoder.ProtocolDecoder.Decode(Convert.FromHexString(hex)));
        }
    }

    [Fact]
    public void Decodes_f22_bms_data_1()
    {
        AssertAllFalse(DecoderHarness.ForKingSong(),
            "aa55000000000000000000000000007ff1d05a5a002af90ff80ff90ff90ff80ff80ff80ff80ff70ff70ff70ff70ff70ff70ff60ff60ff60ff60ff50ff50ff50ff70ff80ff80ff70ff70ff80ff70ff70ff70ff80ff70ff80ff60ff60ff60ff60ff50ff50ff50ff50ff10f08b80bcc0bcc0bc20bcc0bd60bcc0bb80bf5ff0e43b60300e7033000e80300f90000000a0200000000",
            "aa550000000000000000000000000049f1d15a5a2a080000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000026000002000000000000000015150000",
            "aa55000000000000000000000000007ff2d05a5a002afa0ff90ff90ff90ff80ff90ff80ff90ff80ff80ff80ff80ff70ff80ff70ff70ff60ff60ff60ff60ff60ff70ff70ff60ff60ff50ff60ff60ff60ff70ff60ff60ff50ff50ff50ff40ff40ff40ff40ff30ff40ff00f08b80bc20bc20bb80bc20bcc0bb80bae0bf8ff0d43b50300e7033000e80300f9000000240200000000",
            "aa550000000000000000000000000049f2d15a5a2a080000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000026000002000000000000000015150000",
            "aa5530000000000000000000000000b9e3a15a5a19081f110d390a1037102c000bcc0bb80bae0b9afff1440203de05520000000000040029007b7e340200000000000030",
            "aa5530000000000000000000000000b9e4a15a5a19081f110e130610361027000bc20bb80ba40b9afff5440203de05510000000000040029007b7c360200000000000030");
    }

    [Fact]
    public void Decodes_f18_bms_data_1()
    {
        AssertAllFalse(DecoderHarness.ForKingSong(),
            "aa550100000000000000000000000077f1d05a5a00249e0ea30ea10ea10ea20ea20ea20ea20ea20ea10ea10e9e0e9c0e9f0ea20e9f0e9f0ea00ea10ea40ea20ea50ea50ea40ea50ea50ea50ea20ea10ea10ea40ea40ea00ea40ea30ea30e08540b5e0b540b540b680b00005e0b0000f5ffae34e10100e8030700e803ffa100a600b2025a03646c00000000",
            "aa550100000000000000000000000047f1d15a5a2408000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000600000200000000000000001212000000000000",
            "aa550100000000000000000000000077f2d05a5a0024a10ea20ea10ea20ea20ea20ea20ea10ea10ea10ea10ea10ea00ea10ea00ea00ea00ea00ea10ea20ea20ea10ea00ea10ea20ea00ea10ea20ea10ea00e9f0ea00ea10e9f0e9f0e9f0e08540b5e0b5e0b5e0b720b00005e0b0000f4ffaa34e00100e8030700e803ffa900a900cc02f902656e00000000",
            "aa550100000000000000000000000047f2d15a5a2408000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000600000200000000000000001212000000000000");
    }

    [Fact]
    public void Decodes_s16_pro_bms_data_1()
    {
        AssertAllFalse(DecoderHarness.ForKingSong(),
            "aa55f01dfcff4602e7031a00e8030000f1005a5a",
            "aa550000000000000000000000002400f5145a5a",
            "aa55d00700000000d20100004e000000f6145a5a",
            "aa55220b220b220b220b000000005e0bf1015a5a",
            "aa55e90ee80ee80ee80ee70ef80ef80ef1025a5a",
            "aa55f80ef80ef40ef80ef90efa0ef90ef1035a5a",
            "aa55f60ef80ef90ef90efa0ef70e0000f1045a5a",
            "aa550000000000000000000000000000f1055a5a",
            "aa5500000000000000004a0b00000000f1065a5a",
            "aa55ed1dfbff0d029003a101e8030000f2005a5a",
            "aa552c0b220b220b2c0b000000005e0bf2015a5a",
            "aa55e70ee50ee60ee60ee30ef70ef70ef2025a5a",
            "aa55f70ef70ef50ef70ef80ef80ef80ef2035a5a",
            "aa55f50ef70ef70ef80ef80ef40e0000f2045a5a",
            "aa550000000000000000000000000000f2055a5a",
            "aa5500000000000000004a0b00000000f2065a5a");
    }

    [Fact]
    public void Decodes_s16_pro_bms_data_2()
    {
        AssertAllFalse(DecoderHarness.ForKingSong(),
            "aa550300210c0e0e028701200a000000e4bea62f",
            "aa5519081f13050d950f070ef9000001e4a15481",
            "aa55000be00bcc0bd60bd6fffb1e0f02e4a16644",
            "aa550224033a00000000000401a00f03e4a12b2d",
            "aa5500cb7f4000000000000000030f04e4a19400",
            "aa550300210c0e0e028701200a000000e4bea62f",
            "aa5519081f13050d950f070ef9000001e4a15481",
            "aa55000be00bcc0bd60bd6fffb1e0f02e4a16644",
            "aa550224033a00000000000401a00f03e4a12b2d",
            "aa5500cb7f4000000000000000030f04e4a19400");
    }

    /// <summary>
    /// Port of MainActivity.kt:387 — на CONNECTED оригинал просит имя, не дожидаясь кадра. Колесо
    /// молчит, пока его не спросят, поэтому первое слово обязано быть нашим.
    /// </summary>
    [Fact]
    public void Asks_for_the_name_before_the_wheel_has_said_anything()
    {
        var harness = DecoderHarness.ForKingSong();
        var asked = new List<byte[]>();
        harness.Decoder.ProtocolDecoder.WriteRequested += asked.Add;

        harness.Time.Advance(TimeSpan.FromMilliseconds(200));

        Assert.Equal([Empty(0x9B)], asked);
    }

    /// <summary>
    /// Port of BluetoothService.kt:282-286 — запрос стоит вне <c>decode()</c>, поэтому проверки
    /// длины и заголовка внутри адаптера его не гасят. Кадр здесь — живой: KS-16S 03.08.2026 до
    /// первого запроса отвечал на подписку только этими девятью байтами (<c>AT+ULKTE</c>).
    /// </summary>
    [Fact]
    public void Asks_for_the_name_on_a_notification_that_is_not_a_wheel_frame()
    {
        var harness = DecoderHarness.ForKingSong();
        var asked = new List<byte[]>();
        harness.Decoder.ProtocolDecoder.WriteRequested += asked.Add;

        Assert.False(harness.Decoder.ProtocolDecoder.Decode(Convert.FromHexString("41542b554c4b544500")));

        Assert.Equal([Empty(0x9B)], asked);
    }

    /// <summary>Имя известно — спрашивается серийник, и ровно он (BluetoothService.kt:284-285).</summary>
    [Fact]
    public void Asks_for_the_serial_once_the_name_is_known()
    {
        var harness = DecoderHarness.ForKingSong();
        harness.Decoder.ProtocolDecoder.Decode(Convert.FromHexString("aa550253757065722d576865656c3132bb100000"));

        var asked = new List<byte[]>();
        harness.Decoder.ProtocolDecoder.WriteRequested += asked.Add;
        // Кадр с именем сам был поводом спросить серийник, и та попытка ушла до подписки. Пол в
        // 40 мс — наше ограничение (план 36 Л1), поэтому вторая попытка ждёт своей очереди.
        harness.Time.Advance(TimeSpan.FromMilliseconds(50));
        harness.Decoder.ProtocolDecoder.Decode(Convert.FromHexString("aa5570176f009649d2020b0a39300f0ea9100000"));

        Assert.Equal([Empty(0x63)], asked);
    }

    /// <summary>
    /// Замок плана 36 Л1: колесо, которое молчит в ответ на запрос опознания, получает его <b>не
    /// чаще пола</b> (40 мс) — а не с частотой своих же уведомлений. До правки сотня уведомлений
    /// давала сотню запросов (мастер-план §5а).
    /// </summary>
    [Fact]
    public void Identity_requests_keep_the_floor_between_them()
    {
        var harness = DecoderHarness.ForKingSong();
        var asked = new List<byte[]>();
        harness.Decoder.ProtocolDecoder.WriteRequested += asked.Add;

        // Сто уведомлений подряд в одно и то же мгновение — так выглядит очередь кадров колеса.
        for (int i = 0; i < 100; i++)
        {
            harness.Decoder.ProtocolDecoder.Decode(Convert.FromHexString("41542b554c4b544500"));
        }

        Assert.Equal([Empty(0x9B)], asked);

        // Прошёл пол — уходит ровно одна следующая попытка.
        harness.Time.Advance(TimeSpan.FromMilliseconds(41));
        for (int i = 0; i < 100; i++)
        {
            harness.Decoder.ProtocolDecoder.Decode(Convert.FromHexString("41542b554c4b544500"));
        }

        Assert.Equal([Empty(0x9B), Empty(0x9B)], asked);
    }

    /// <summary>
    /// Замок плана 36 Л1: потолок. Колесо, которое не назовёт имя никогда, получает не больше
    /// пятидесяти запросов за всю поездку — образец наш же Begode (<c>GotwayDecoder.cs:498</c>).
    /// </summary>
    [Fact]
    public void Identity_requests_stop_at_the_attempt_ceiling()
    {
        var harness = DecoderHarness.ForKingSong();
        var asked = new List<byte[]>();
        harness.Decoder.ProtocolDecoder.WriteRequested += asked.Add;

        // Двести поводов спросить, каждый заведомо позже пола — ограничивает только потолок.
        for (int i = 0; i < 200; i++)
        {
            harness.Time.Advance(TimeSpan.FromMilliseconds(50));
            harness.Decoder.ProtocolDecoder.Decode(Convert.FromHexString("41542b554c4b544500"));
        }

        Assert.Equal(50, asked.Count);
        Assert.All(asked, request => Assert.Equal(Empty(0x9B), request));
    }

    /// <summary>
    /// Замок плана 36 Л1: ответ колеса возвращает счёт к нулю — у ступени серийника свой полный
    /// бюджет попыток, как у Begode на ответ «GW» (<c>GotwayDecoder.cs:170</c>).
    /// </summary>
    [Fact]
    public void An_answer_gives_the_next_stage_its_own_budget()
    {
        var harness = DecoderHarness.ForKingSong();
        var decoder = harness.Decoder.ProtocolDecoder;
        var asked = new List<byte[]>();
        decoder.WriteRequested += asked.Add;

        // Имя выбирает свой потолок целиком.
        for (int i = 0; i < 60; i++)
        {
            harness.Time.Advance(TimeSpan.FromMilliseconds(50));
            decoder.Decode(Convert.FromHexString("41542b554c4b544500"));
        }

        Assert.Equal(50, asked.Count);

        // Колесо назвалось — дальше спрашивается серийник, и счёт начат заново.
        harness.Time.Advance(TimeSpan.FromMilliseconds(50));
        decoder.Decode(Convert.FromHexString("aa550253757065722d576865656c3132bb100000"));
        for (int i = 0; i < 60; i++)
        {
            harness.Time.Advance(TimeSpan.FromMilliseconds(50));
            decoder.Decode(Convert.FromHexString("41542b554c4b544500"));
        }

        Assert.Equal(50, asked.Count(request => request[16] == 0x63));
    }

    /// <summary>
    /// Опознание названо целиком — запросов нет вовсе, сколько бы кадров ни пришло. Это поведение
    /// оригинала, и потолок его не изменил.
    /// </summary>
    [Fact]
    public void A_fully_identified_wheel_is_never_asked_again()
    {
        var harness = DecoderHarness.ForKingSong();
        var decoder = harness.Decoder.ProtocolDecoder;
        decoder.Decode(Convert.FromHexString("aa550253757065722d576865656c3132bb100000"));
        decoder.Decode(Convert.FromHexString("aa554b696e6731323334353637383930b3313233"));

        var asked = new List<byte[]>();
        decoder.WriteRequested += asked.Add;
        for (int i = 0; i < 20; i++)
        {
            harness.Time.Advance(TimeSpan.FromMilliseconds(50));
            decoder.Decode(Convert.FromHexString("aa5570176f009649d2020b0a39300f0ea9100000"));
        }

        Assert.Empty(asked);
    }

    [Fact]
    public void Command_builders_match_original_bytes()
    {
        var harness = DecoderHarness.ForKingSong();
        var decoder = harness.Decoder.ProtocolDecoder;

        Assert.Equal(Empty(0x88), decoder.BuildWheelBeep());
        Assert.Equal(Empty(0x89), decoder.BuildCalibrate());
        Assert.Null(decoder.BuildResetTrip());

        // Было (WheelLog/KingsongAdapter.java): pedals[17] = 0x15 — расхождение с производителем,
        // который на этом месте держит обычный заполнитель 0x14 (см. port-deviations.md). Empty(0x87)
        // уже несёт [17] = 0x14, так что сравнение с ним само по себе доказывает правку смещения 17.
        byte[] pedals = Empty(0x87);
        pedals[2] = 1;
        pedals[3] = 0xE0;
        Assert.Equal(pedals, decoder.BuildUpdatePedalsMode(1));

        byte[] lightOn = Empty(0x73);
        lightOn[2] = 0x13; // lightMode(1) + 0x12
        lightOn[3] = 0x01;
        Assert.Equal(lightOn, decoder.BuildSetLightState(true));

        byte[] lightOff = Empty(0x73);
        lightOff[2] = 0x12; // lightMode(0) + 0x12
        lightOff[3] = 0x01;
        Assert.Equal(lightOff, decoder.BuildSetLightState(false));
    }

    /// <summary>Port of KingsongAdapter.getEmptyRequest() — каркас любой команды и любого запроса.</summary>
    private static byte[] Empty(byte type) =>
    [
        0xAA, 0x55, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, type, 0x14, 0x5A, 0x5A,
    ];
}
