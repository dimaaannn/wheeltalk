using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Ports the InMotion V1 fixtures from the original Android InmotionAdapterTest.kt (see AGENTS.md,
/// "Как проверять изменения в декодере") — pinned here as permanent regression tests. Each fixture
/// feeds a full frame sequence (fast-info handshake bytes, then a slow-info frame carrying model/
/// serial/version, then a fast-info live-data frame) exactly as recorded by the original test.
/// </summary>
public class InMotionDecoderTests
{
    [Fact]
    public void Decode_with_v5f_full_data()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;

        bool result1 = decoder.Decode(Convert.FromHexString("AAAA1401A5550F7C000000B4720020FE0001001B"));
        bool result2 = decoder.Decode(Convert.FromHexString("0076BA5C28711200000000000000000100000000"));
        bool result3 = decoder.Decode(Convert.FromHexString("000000FA010301FA0103010402020100000000C2"));
        bool result4 = decoder.Decode(Convert.FromHexString("040001C2040001900302010000000000000000A8"));
        bool result5 = decoder.Decode(Convert.FromHexString("6100000010000000000000000000000000000000"));
        bool result6 = decoder.Decode(Convert.FromHexString("0000000100000000000000000000000000000000"));
        bool result7 = decoder.Decode(Convert.FromHexString("0000000200000500000000000000000000000004"));
        bool result8 = decoder.Decode(Convert.FromHexString("020301E35555"));

        bool result11 = decoder.Decode(Convert.FromHexString("AAAA1301A5550F60000000B4720020FE000100FF"));
        bool result12 = decoder.Decode(Convert.FromHexString("3F00003A18DEFF5D01000029F0FFFF29F0FFFFEC"));
        bool result13 = decoder.Decode(Convert.FromHexString("FFFFFF15200000000000001A1A00000000000000"));
        bool result14 = decoder.Decode(Convert.FromHexString("0000001CE3130000000000000026061A03D20721"));
        bool result15 = decoder.Decode(Convert.FromHexString("0000006F0100006F010000F7010000420C00002B"));
        bool result16 = decoder.Decode(Convert.FromHexString("110000070000000000000000000000265555"));

        Assert.False(result1);
        Assert.False(result2);
        Assert.False(result3);
        Assert.False(result4);
        Assert.False(result5);
        Assert.False(result6);
        Assert.False(result7);
        Assert.False(result8);

        var afterHandshake = harness.Snapshot();
        Assert.Equal("1271285CBA76001B", afterHandshake.Serial);
        Assert.Equal("Inmotion V5F", afterHandshake.Model);
        Assert.Equal("1.3.506", afterHandshake.Version);

        Assert.False(result11);
        Assert.False(result12);
        Assert.False(result13);
        Assert.False(result14);
        Assert.False(result15);
        Assert.True(result16);

        var snapshot = harness.Snapshot();
        Assert.Equal(3.82, snapshot.SpeedKmh, 2);
        Assert.Equal(26, snapshot.TemperatureC);
        Assert.Equal(0, snapshot.ImuTemp);
        Assert.Equal(82.13, snapshot.VoltageV, 2);
        Assert.Equal(-0.2, snapshot.CurrentA, 2);
        Assert.Equal(0.0, snapshot.WheelDistanceKm, 3);
        Assert.Equal(1303324, snapshot.TotalDistance);
        Assert.Equal(97, snapshot.Battery);
        Assert.Equal(0.2499847412109375, snapshot.Angle, 10);
        Assert.Equal(5.588888888888889, snapshot.Roll, 10);
    }

    [Fact]
    public void Decode_with_v8f_full_data()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;

        FeedV8FHandshake(decoder);

        bool result11 = decoder.Decode(Convert.FromHexString("AAAA1301A5550F9500000000000000FE0201008F"));
        bool result12 = decoder.Decode(Convert.FromHexString("020000000000000000000054FAFFFF54FAFFFFFB"));
        bool result13 = decoder.Decode(Convert.FromHexString("FFFFFFBE200000000000001B1B24240000000000"));
        bool result14 = decoder.Decode(Convert.FromHexString("000000AF5400000100000000302B140605E00722"));
        bool result15 = decoder.Decode(Convert.FromHexString("00000023000000C50000005D020000D900000006"));
        bool result16 = decoder.Decode(Convert.FromHexString("000000000000000000000000000000004000081B"));
        bool result17 = decoder.Decode(Convert.FromHexString("0000F221000033060000000000000B0000006216"));
        bool result18 = decoder.Decode(Convert.FromHexString("0000F42A0000030000000E000000110106000000"));
        bool result19 = decoder.Decode(Convert.FromHexString("000000000000C500765555"));

        var afterHandshake = harness.Snapshot();
        Assert.Equal("14604A5EBD9B000E", afterHandshake.Serial);
        Assert.Equal("Inmotion V8F", afterHandshake.Model);
        Assert.Equal("2.2.21", afterHandshake.Version);

        Assert.False(result11);
        Assert.False(result12);
        Assert.False(result13);
        Assert.False(result14);
        Assert.False(result15);
        Assert.False(result16);
        Assert.False(result17);
        Assert.False(result18);
        Assert.True(result19);

        var snapshot = harness.Snapshot();
        Assert.Equal(1.37, snapshot.SpeedKmh, 2);
        Assert.Equal(27, snapshot.TemperatureC);
        Assert.Equal(36, snapshot.ImuTemp);
        Assert.Equal(83.82, snapshot.VoltageV, 2);
        Assert.Equal(-0.05, snapshot.CurrentA, 2);
        Assert.Equal(0.001, snapshot.WheelDistanceKm, 3);
        Assert.Equal(21679, snapshot.TotalDistance);
        Assert.Equal(100, snapshot.Battery);
        Assert.Equal(0.0099945068359375, snapshot.Angle, 10);
        Assert.Equal(0.0, snapshot.Roll, 10);
        Assert.Equal("Drive", snapshot.ModeStr);
    }

    [Fact]
    public void Decode_with_v8f_full_data_2()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;

        FeedV8FHandshake(decoder);

        bool result11 = decoder.Decode(Convert.FromHexString("AAAA1301A5550F9500000000000000FE0201007A"));
        bool result12 = decoder.Decode(Convert.FromHexString("14000000000000000000003CFDFFFF3CFDFFFFF6"));
        bool result13 = decoder.Decode(Convert.FromHexString("FFFFFFA7200000400100001C1C2424F8FFFFFFE7"));
        bool result14 = decoder.Decode(Convert.FromHexString("FFFFFFB75400000900000000042C140605E00722"));
        bool result15 = decoder.Decode(Convert.FromHexString("000000E301000023010000AC0500000302000056"));
        bool result16 = decoder.Decode(Convert.FromHexString("0000004C0000000000000000000000004000081C"));
        bool result17 = decoder.Decode(Convert.FromHexString("0000F221000033060000BF020000070100006F16"));
        bool result18 = decoder.Decode(Convert.FromHexString("0000032B0000100000001D000000380256004C00"));
        bool result19 = decoder.Decode(Convert.FromHexString("F8FFE7FFE7FF2301465555"));

        Assert.False(result11);
        Assert.False(result12);
        Assert.False(result13);
        Assert.False(result14);
        Assert.False(result15);
        Assert.False(result16);
        Assert.False(result17);
        Assert.False(result18);
        Assert.True(result19);

        var snapshot = harness.Snapshot();
        Assert.Equal("14604A5EBD9B000E", snapshot.Serial);
        Assert.Equal("Inmotion V8F", snapshot.Model);
        Assert.Equal("2.2.21", snapshot.Version);
        Assert.Equal(0.66, snapshot.SpeedKmh, 2);
        Assert.Equal(28, snapshot.TemperatureC);
        Assert.Equal(36, snapshot.ImuTemp);
        Assert.Equal(83.59, snapshot.VoltageV, 2);
        Assert.Equal(-0.1, snapshot.CurrentA, 2);
        Assert.Equal(0.009, snapshot.WheelDistanceKm, 3);
        Assert.Equal(21687, snapshot.TotalDistance);
        Assert.Equal(100, snapshot.Battery);
        Assert.Equal(0.079986572265625, snapshot.Angle, 10);
        Assert.Equal(0.0, snapshot.Roll, 10);
        Assert.Equal("Drive", snapshot.ModeStr);
    }

    [Fact]
    public void Decode_with_v8s_full_data()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;

        bool result1 = decoder.Decode(Convert.FromHexString("aaaa1401a5550f8500000000000000fe02010006"));
        bool result2 = decoder.Decode(Convert.FromHexString("0146bd5ea5aa7115000000000000000000000000"));
        bool result3 = decoder.Decode(Convert.FromHexString("0000000015000266000000000700036600000000"));
        bool result4 = decoder.Decode(Convert.FromHexString("260301010000000000000a000000000000000800"));
        bool result5 = decoder.Decode(Convert.FromHexString("b888000043100000001000000000000000000000"));
        bool result6 = decoder.Decode(Convert.FromHexString("0000000001000000000000000000000000000000"));
        bool result7 = decoder.Decode(Convert.FromHexString("000000000700000800000000b005004f00000065"));
        bool result8 = decoder.Decode(Convert.FromHexString("00000000801027000001000a01a05555"));

        bool result11 = decoder.Decode(Convert.FromHexString("aaaa1301a5550f9500000000000000fe02010015"));
        bool result12 = decoder.Decode(Convert.FromHexString("eeffff0000000000000000000000000000000007"));
        bool result13 = decoder.Decode(Convert.FromHexString("00000006200000000000001e1e92920000000004"));
        bool result14 = decoder.Decode(Convert.FromHexString("000000af04000000000000000d370c1203d00723"));
        bool result15 = decoder.Decode(Convert.FromHexString("0000000000000000000000bcfeffff1400000000"));
        bool result16 = decoder.Decode(Convert.FromHexString("0000001100000000000000000000000040000892"));
        bool result17 = decoder.Decode(Convert.FromHexString("00007f0500006600000083b205004f0000006502"));
        bool result18 = decoder.Decode(Convert.FromHexString("0000ca45000000000000d51f0000600000001100"));
        bool result19 = decoder.Decode(Convert.FromHexString("0000040004000000bf5555"));

        Assert.False(result1);
        Assert.False(result2);
        Assert.False(result3);
        Assert.False(result4);
        Assert.False(result5);
        Assert.False(result6);
        Assert.False(result7);
        Assert.False(result8);

        var afterHandshake = harness.Snapshot();
        Assert.Equal("1571AA5EBD460106", afterHandshake.Serial);
        Assert.Equal("Inmotion V8S", afterHandshake.Model);
        Assert.Equal("102.2.21", afterHandshake.Version);

        Assert.False(result11);
        Assert.False(result12);
        Assert.False(result13);
        Assert.False(result14);
        Assert.False(result15);
        Assert.False(result16);
        Assert.False(result17);
        Assert.False(result18);
        Assert.True(result19);

        var snapshot = harness.Snapshot();
        Assert.Equal(0.0, snapshot.SpeedKmh, 2);
        Assert.Equal(30, snapshot.TemperatureC);
        Assert.Equal(-110, snapshot.ImuTemp);
        Assert.Equal(81.98, snapshot.VoltageV, 2);
        Assert.Equal(0.07, snapshot.CurrentA, 2);
        Assert.Equal(0.0, snapshot.WheelDistanceKm, 3);
        Assert.Equal(1199, snapshot.TotalDistance);
        Assert.Equal(96, snapshot.Battery);
        Assert.Equal(-0.0699920654296875, snapshot.Angle, 10);
        Assert.Equal(0.0, snapshot.Roll, 10);
        Assert.Equal("Drive", snapshot.ModeStr);
    }

    /// <summary>The wheel escapes its own checksum byte when the value happens to collide with
    /// AA/55/A5 — the unpacker's escape handling must strip that transparently, same as any other
    /// byte in the frame (see InMotionUnpacker's class doc).</summary>
    [Fact]
    public void Decode_data_with_escaped_checksum()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;

        string[] frames =
        [
            "aaaa1401a5550f8500000000000000fe02010001",
            "00da7c5e1a611400000000000000000000000000",
            "0000001500020200000000070003020000000026",
            "0301010000000000000a000000000000000200d0",
            "840000ea0f000000100000000000000000000000",
            "0000000100000000000000000000000000000000",
            "00000006000008000000005b0a006f6e01003a00",
            "0000006c3421000001010a00a5555555",
        ];

        bool result = false;
        foreach (string frame in frames) result = decoder.Decode(Convert.FromHexString(frame));

        Assert.False(result);
        var snapshot = harness.Snapshot();
        Assert.Equal("Inmotion V8F", snapshot.Model);
        Assert.Equal("2.2.21", snapshot.Version);
    }

    [Fact]
    public void Command_builders_match_original_bytes()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;

        Assert.Equal(Convert.FromHexString("aaaa1901a5550f3254769800000000080500001f5555"), decoder.BuildCalibrate());
        Assert.Null(decoder.BuildResetTrip());
        Assert.Null(decoder.BuildUpdatePedalsMode(1));

        Assert.Equal(Convert.FromHexString("aaaa0d01a5550f010000000000000008050000805555"), decoder.BuildSetLightState(true));
        Assert.Equal(Convert.FromHexString("aaaa0d01a5550f0000000000000000080500007f5555"), decoder.BuildSetLightState(false));

        // switchFlashlight() toggles from whatever the config says LightEnabled currently is
        // (false, the harness default) — so the first toggle turns it on.
        Assert.Equal(Convert.FromHexString("aaaa0d01a5550f010000000000000008050000805555"), decoder.BuildSwitchFlashlight());

        // Model still Unknown: wheelBeep() falls back to playSound(4) — old wheels like V8/V5F
        // don't have the dedicated beep command (InMotionAdapter.java:414-417). No original test
        // exercises playSound(4) directly (only playSound(2), via "play sound command"); this is
        // that same byte layout with data[0]=4 instead of 2, checksum shifted by the same +2.
        Assert.Equal(Convert.FromHexString("aaaa0906a5550f040000000000000008050000845555"), decoder.BuildWheelBeep());
    }

    /// <summary>
    /// Ни одна команда InMotion V1 не влезает в запись при MTU по умолчанию: 16 байт тела,
    /// обязательный escape <c>A5</c> перед <c>0x55</c> в id, заголовок, контрольная и хвост дают 22
    /// байта против 20 доступных. Стек Android такую запись не отвергает — он молча шлёт первые 20
    /// байт, и колесо, получив кадр без контрольной и без <c>55 55</c>, не отвечает ничем, кроме
    /// «привета» BLE-модуля (дамп vivo I2407 от 07.08.2026: V8F, ноль телеметрии за весь сеанс).
    /// Отсюда запрос MTU в <c>AndroidBleClient</c> — тест держит причину, по которой его нельзя
    /// убрать «как лишний шаг бутстрапа», как это уже решалось однажды (план 21 §0.3).
    /// </summary>
    [Fact]
    public void Every_command_needs_more_than_the_default_att_payload()
    {
        const int DefaultAttPayload = 20;

        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;

        byte[][] commands =
        [
            // Опрос: эти три идут сами, тактами keep-alive, и без них разговора не начнётся вовсе.
            Core.Decoding.InMotionCanMessage.GetPassword("000000").WriteBuffer(),
            Core.Decoding.InMotionCanMessage.GetSlowData().WriteBuffer(),
            Core.Decoding.InMotionCanMessage.StandardMessage().WriteBuffer(),
            // По кнопке.
            decoder.BuildWheelBeep(),
            decoder.BuildSetLightState(true),
            decoder.BuildCalibrate()!,
        ];

        Assert.All(commands, cmd =>
            Assert.True(cmd.Length > DefaultAttPayload,
                $"команда {Convert.ToHexString(cmd)} — {cmd.Length} Б, а протокол считался влезающим в {DefaultAttPayload}"));
    }

    /// <summary>V8F belongs to getWheelModesWheel()'s newer-wheel set, so once the model is known,
    /// wheelBeep() uses the dedicated beep command instead of playSound(4).</summary>
    [Fact]
    public void Wheel_beep_uses_dedicated_command_once_model_is_known()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = harness.Decoder.ProtocolDecoder;
        FeedV8FHandshake(decoder);

        Assert.Equal(Convert.FromHexString("aaaa1601a5550fb200000011000000080500004b5555"), decoder.BuildWheelBeep());
    }

    /// <summary>
    /// «Колесо не пустило» — единственный кадр, который V8F слал за весь сеанс 07.08.2026: привет
    /// BLE-модуля (`id 0x0F060101`). Связь при этом исправна, и отличить «пароль не тот» от
    /// «колесо молчит» можно только так — по тому, что кадры идут, а колесо не представилось.
    /// </summary>
    private const string ModuleHelloFrame =
        "AAAA0101060F1200000000000000FE0201000708C0370144215EFA7018A555000000000000CB5555";

    [Fact]
    public void The_wheel_that_stays_silent_with_frames_flowing_is_standing_on_the_password()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = (Core.Decoding.IPasswordProtected)harness.Decoder.ProtocolDecoder;

        harness.Time.Advance(TimeSpan.FromSeconds(2));
        harness.Decoder.ProtocolDecoder.Decode(Convert.FromHexString(ModuleHelloFrame));
        harness.Time.Advance(TimeSpan.FromSeconds(4));

        Assert.True(decoder.AwaitingPassword);
    }

    /// <summary>
    /// Ответ на сам кадр пароля (<c>PinCode 0x0F550307</c>) — не «пустило». Оригинал по нему всего
    /// лишь перестаёт слать пароль (<c>passwordSent = MAX_VALUE</c>), и колесо с <b>заданным</b>
    /// PIN отвечает им же. Кадр взят из эталонного дампа `RAW_inmotion_V8S.csv`.
    /// <para>
    /// Тест заведён по находке мастера 08.08.2026: срок ожидания взводился на шестой отправке, а
    /// шестой после этого кадра не случалось никогда — вопрос о пароле не поднимался ровно на тех
    /// колёсах, ради которых писался.
    /// </para>
    /// </summary>
    [Fact]
    public void An_answer_to_the_password_frame_is_not_an_answer_from_the_wheel()
    {
        const string PinCodeAck = "AAAA0703A5550F010000000000000004020000755555";

        var harness = DecoderHarness.ForInMotion();
        var protocol = harness.Decoder.ProtocolDecoder;
        var decoder = (Core.Decoding.IPasswordProtected)protocol;

        // Колесо отвечает на первый же кадр пароля — до шестой отправки дело не дойдёт.
        harness.Time.Advance(TimeSpan.FromMilliseconds(250));
        protocol.Decode(Convert.FromHexString(PinCodeAck));
        harness.Time.Advance(TimeSpan.FromSeconds(5));

        Assert.True(decoder.AwaitingPassword);
    }

    /// <summary>Винить пароль там, где колеса нет в эфире, — врать человеку: отказывать некому.
    /// Молчащий линк — забота сторожа данных.</summary>
    [Fact]
    public void A_wheel_that_sends_nothing_is_not_blamed_on_the_password()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = (Core.Decoding.IPasswordProtected)harness.Decoder.ProtocolDecoder;

        harness.Time.Advance(TimeSpan.FromSeconds(30));

        Assert.False(decoder.AwaitingPassword);
    }

    [Fact]
    public void A_wheel_that_introduces_itself_is_not_standing_on_the_password()
    {
        var harness = DecoderHarness.ForInMotion();
        var decoder = (Core.Decoding.IPasswordProtected)harness.Decoder.ProtocolDecoder;

        FeedV8FHandshake(harness.Decoder.ProtocolDecoder);
        harness.Time.Advance(TimeSpan.FromSeconds(30));

        Assert.False(decoder.AwaitingPassword);
    }

    /// <summary>
    /// Заданный человеком пароль уходит колесу и **подставляется без переподключения**: правка
    /// настройки зовёт <c>RestartAuthentication</c>, и кадр с новым паролем идёт заново. Колесо
    /// по-прежнему молчит — значит и этот пароль не подошёл, и причина снова видна.
    /// </summary>
    [Fact]
    public void A_password_set_by_hand_is_sent_without_reconnecting()
    {
        var harness = DecoderHarness.ForInMotion();
        var protocol = harness.Decoder.ProtocolDecoder;
        var decoder = (Core.Decoding.IPasswordProtected)protocol;

        harness.Time.Advance(TimeSpan.FromSeconds(2));
        protocol.Decode(Convert.FromHexString(ModuleHelloFrame));
        harness.Time.Advance(TimeSpan.FromSeconds(4));
        Assert.True(decoder.AwaitingPassword);

        var sent = new List<byte[]>();
        protocol.WriteRequested += sent.Add;
        harness.Config.InMotionPassword = "123456";
        decoder.RestartAuthentication();

        Assert.False(decoder.AwaitingPassword);

        harness.Time.Advance(TimeSpan.FromSeconds(2));
        byte[] expected = Core.Decoding.InMotionCanMessage.GetPassword("123456").WriteBuffer();
        Assert.Contains(sent, cmd => cmd.SequenceEqual(expected));

        protocol.Decode(Convert.FromHexString(ModuleHelloFrame));
        harness.Time.Advance(TimeSpan.FromSeconds(4));
        Assert.True(decoder.AwaitingPassword);
    }

    /// <summary>
    /// Причина не гаснет сама и не мигает: пока пароль не сменили, состояние держится ровно, сколько
    /// бы кадров ни пришло. Плашка связи читает его каждый кадр — дребезг был бы виден глазом.
    /// </summary>
    [Fact]
    public void The_reason_holds_steady_until_the_password_changes()
    {
        var harness = DecoderHarness.ForInMotion();
        var protocol = harness.Decoder.ProtocolDecoder;
        var decoder = (Core.Decoding.IPasswordProtected)protocol;

        harness.Time.Advance(TimeSpan.FromSeconds(2));
        protocol.Decode(Convert.FromHexString(ModuleHelloFrame));
        harness.Time.Advance(TimeSpan.FromSeconds(4));

        for (int i = 0; i < 10; i++)
        {
            protocol.Decode(Convert.FromHexString(ModuleHelloFrame));
            harness.Time.Advance(TimeSpan.FromSeconds(2));
            Assert.True(decoder.AwaitingPassword);
        }
    }

    private static void FeedV8FHandshake(Core.Decoding.IWheelDecoder decoder)
    {
        string[] frames =
        [
            "AAAA1401A5550F8500000000000000FE0201000E",
            "009BBD5E4A601400000000000000000000000000",
            "0000001500020200000000070003020000000026",
            "0301010000000000000A000000000073000000C8",
            "AF00002510000000100000000000000000000000",
            "0000000100000000000000000000000000000000",
            "0000000600000800000000000000000000000000",
            "000000801027000001010A00DC5555",
        ];
        foreach (string frame in frames) decoder.Decode(Convert.FromHexString(frame));
    }
}
