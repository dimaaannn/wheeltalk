using System.Text;
using WheelTalk.Core.Battery;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Decoding;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Battery;

/// <summary>
/// Гейт шага 27.3: декодеры перестали отвечать сами и спрашивают каскад — и <b>ни одно число не
/// сдвинулось</b>. Ожидания здесь — снимок того, что каждый декодер отдавал до подмены; сверяются
/// они через новый путь, то есть проверяют именно его.
/// <para>
/// Проверка тем и ценна, что этим числом считается кривая заряда: сдвиг ряда сдвинул бы проценты
/// у живых колёс, а поездки с прежними процентами у людей уже накоплены.
/// </para>
/// </summary>
public class CellCountThroughDecodersTests
{
    /// <summary>
    /// Настройка вольтажа Begode во всех значениях, включая нераспознанное. <c>"3"</c> здесь даёт
    /// 32, хотя по напряжению их 28: расхождение унаследовано 1:1 и чинится отдельным шагом — этот
    /// тест его нарочно закрепляет, чтобы «починка заодно» не прошла молча.
    /// </summary>
    [Theory]
    [InlineData("0", 16)]
    [InlineData("1", 20)]
    [InlineData("2", 24)]
    [InlineData("3", 32)]
    [InlineData("4", 32)]
    [InlineData("5", 40)]
    [InlineData("6", 36)]
    [InlineData("", 24)]
    [InlineData("9", 24)]
    public void Begode_keeps_its_voltage_setting_table(string voltageSetting, int expected)
    {
        var harness = DecoderHarness.ForGotway(config => config.GotwayVoltage = voltageSetting);

        Assert.Equal(expected, ((GotwayDecoder)harness.Decoder.ProtocolDecoder).GetCellsForWheel().Cells);
    }

    /// <summary>
    /// Veteran узнаёт ряд по версии протокола, и все её ветки достижимы настоящими кадрами из
    /// фикстур: Abrams 002 → 24, Patton 004 → 30, Lynx 005 → 36, Sherman L 006 → 36, Oryx 008 → 42.
    /// </summary>
    [Theory]
    [InlineData(24, "dc5a5c20266d00004aaf00004aaf000000000d9e", "0b8800000af00af007d2000300050004")]
    [InlineData(30, "dc5a5c452abe00003edc00008562003500000b5c", "0dfe000002bc07d00fac000219fb0000006f0000",
        "80808080808004000014ffffffffff32ee029109", "df0fd303cb000000006f9a79c2")]
    [InlineData(36, "dc5a5c53391b000006d000000770000000260bcc", "0e08000000fa00c8138c00b4000b014c80c80000",
        "808080808080010008808080800fee0fee0fee0f", "ee0fef0fe80fef0fef0ff00ff00ff00fea0fef0f", "ef0fefdab22518")]
    [InlineData(36, "dc5a5c53397afffe0aa400000df10000000a0b3d", "0e0e0000037a035217730064000e00b480c80000",
        "808080808080058080808080800ff30ff50ff50f", "f50ff50fef0ff20ff30ff30ff30ff30fed0ff30f", "f40ff5378c5145")]
    [InlineData(42, "dc5a5c473e7b000030100002a309000f00000a86", "0473000007d007d01f4300a0e43a000080c80000",
        "808080808080080000803ce8c8c8c81e00000000", "0001320554a8648037808064e0ca5a")]
    public void Veteran_keeps_its_protocol_version_table(int expected, params string[] frames)
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(frames);

        Assert.Equal(expected, ((VeteranDecoder)harness.Decoder.ProtocolDecoder).GetCellsForWheel().Cells);
    }

    /// <summary>
    /// KingSong узнаёт ряд по имени модели из кадра 0xBB. Неизвестное имя даёт 16 — то же, что и
    /// до подмены: молчание протокола ниже по каскаду не спускается, потому что протокол не молчит.
    /// </summary>
    [Theory]
    [InlineData("KS-S18-0205", 20)]
    [InlineData("KS-S19-0001", 24)]
    [InlineData("KS-S22-0001", 30)]
    [InlineData("KS-F18P-0001", 36)]
    [InlineData("KS-F22P-0001", 42)]
    [InlineData("Super-Wheel12", 16)]
    public void KingSong_keeps_its_model_table(string wheelName, int expected)
    {
        var harness = DecoderHarness.ForKingSong();
        harness.Decoder.ProtocolDecoder.Decode(NameFrame(wheelName));

        Assert.Equal(expected, ((KingsongDecoder)harness.Decoder.ProtocolDecoder).GetCellsForWheel().Cells);
    }

    /// <summary>У InMotion первого поколения ряд один на все модели.</summary>
    [Fact]
    public void InMotion_v1_keeps_its_single_answer()
    {
        var harness = DecoderHarness.ForInMotion();

        Assert.Equal(20, ((InMotionDecoder)harness.Decoder.ProtocolDecoder).GetCellsForWheel().Cells);
    }

    /// <summary>
    /// InMotion V2 — единственный, у кого ряд приходит из рукопожатия. Проверяется вся таблица
    /// разом: пропущенная модель означала бы 20 ячеек вместо 30 или 32 у V13/V14, то есть кривую
    /// заряда мимо на треть.
    /// </summary>
    [Fact]
    public void InMotion_v2_keeps_its_model_table()
    {
        foreach (InMotionV2Model model in Enum.GetValues<InMotionV2Model>())
        {
            var harness = DecoderHarness.ForInMotionV2();
            var decoder = (InMotionDecoderV2)harness.Decoder.ProtocolDecoder;
            decoder.SetModel(model);

            Assert.Equal(CellsBeforeTheCascade(model), decoder.GetCellsForWheel().Cells);
        }
    }

    /// <summary>
    /// Вход <c>Configured</c> замкнут (§27.4): число, заданное человеком, бьёт и знание протокола, и
    /// умный BMS — у всех декодеров разом, а не у того, где о нём вспомнили. Ноль означает «не
    /// задано», поэтому настройка, которой не касались, ничего не сдвигает — это и проверяют
    /// таблицы выше.
    /// </summary>
    [Fact]
    public void A_series_set_by_hand_beats_every_lower_step()
    {
        var gotway = DecoderHarness.ForGotway(config =>
        {
            config.GotwayVoltage = "1";   // протокол сказал бы 20
            config.CellsInSeries = 32;
        });
        var kingsong = DecoderHarness.ForKingSong(config => config.CellsInSeries = 32);
        var inMotion = DecoderHarness.ForInMotion(config => config.CellsInSeries = 32);

        var byHand = new CellCount(32, CellCountSource.UserSetting);

        Assert.Equal(byHand, ((GotwayDecoder)gotway.Decoder.ProtocolDecoder).GetCellsForWheel());
        Assert.Equal(byHand, ((KingsongDecoder)kingsong.Decoder.ProtocolDecoder).GetCellsForWheel());
        Assert.Equal(byHand, ((InMotionDecoder)inMotion.Decoder.ProtocolDecoder).GetCellsForWheel());
    }

    /// <summary>
    /// Гейт шага 27.5: две нижние ступени наконец накормлены — и <b>ни одно число не сдвинулось</b>.
    /// Отвечает по-прежнему ступень протокола, до напряжения очередь не доходит ни у кого из пяти.
    /// <para>
    /// Кадры тут настоящие, потому напряжение в состоянии живое, — без него проверка была бы пустой,
    /// и оттого оно утверждается отдельно, а не подразумевается.
    /// </para>
    /// </summary>
    [Fact]
    public void Live_telemetry_moves_no_answer()
    {
        AssertProtocolStillAnswers(FedGotway(), 16);
        AssertProtocolStillAnswers(FedVeteran(), 24);
        AssertProtocolStillAnswers(FedKingSong(), 16);
        AssertProtocolStillAnswers(FedInMotion(), 20);
        AssertProtocolStillAnswers(FedInMotionV2(), 20);
    }

    /// <summary>Напряжение пакета подают все пятеро, и подают текущее — то же, что в состоянии.</summary>
    [Fact]
    public void Every_decoder_feeds_the_pack_voltage()
    {
        foreach (DecoderHarness harness in
                 (DecoderHarness[])[FedGotway(), FedVeteran(), FedKingSong(), FedInMotion(), FedInMotionV2()])
        {
            Assert.Equal(harness.Snapshot().VoltageV, CellInputsOf(harness).PackVolts);
        }
    }

    /// <summary>
    /// Процент подаёт <b>один InMotion V2</b>: у него он приходит из кадра. У остальных четырёх
    /// заряд посчитан нашей же кривой из напряжения, и подать его значило бы делить напряжение на
    /// выведенное из него — ступень подтверждала бы любую догадку, включая неверную.
    /// <para>
    /// Проверяется тестом, а не глазами, ровно затем, чтобы «недоделанное» не доделали: пустое поле
    /// выглядит упущением, пока кто-нибудь не увидит, что оно пусто нарочно (§27.5).
    /// </para>
    /// </summary>
    [Fact]
    public void Only_inmotion_v2_feeds_the_wheels_own_percent()
    {
        Assert.Null(CellInputsOf(FedGotway()).WheelPercent);
        Assert.Null(CellInputsOf(FedVeteran()).WheelPercent);
        Assert.Null(CellInputsOf(FedKingSong()).WheelPercent);
        Assert.Null(CellInputsOf(FedInMotion()).WheelPercent);

        Assert.Equal(88, CellInputsOf(FedInMotionV2()).WheelPercent);
    }

    /// <summary>
    /// Ради чего всё и делалось: промолчи ступень протокола — отвечает пара «напряжение с
    /// процентом», и отвечает верно (79,10 В при 88 % — это 20S). У Ветерана та же подмена доходит
    /// лишь до догадки: процента в входах нет.
    /// <para>
    /// Молчание протокола подставлено руками, потому что живого молчания у нас нет ни у кого:
    /// неопознанная модель тоже даёт число (у InMotion V2 — 20). Это и есть тот случай, ради
    /// которого ступени лежат готовыми, — колесо, чей протокол ряда не знает.
    /// </para>
    /// </summary>
    [Fact]
    public void A_silent_protocol_hands_the_answer_down()
    {
        CellCountInputs inMotionV2 = CellInputsOf(FedInMotionV2()) with { ProtocolCells = null };
        CellCountInputs veteran = CellInputsOf(FedVeteran()) with { ProtocolCells = null };

        Assert.Equal(new CellCount(20, CellCountSource.VoltageWithPercent), CellCountResolver.Resolve(inMotionV2));
        Assert.Equal(CellCountSource.VoltageGuess, CellCountResolver.Resolve(veteran).Source);
    }

    private static void AssertProtocolStillAnswers(DecoderHarness harness, int expected)
    {
        TelemetrySnapshot snapshot = harness.Snapshot();

        Assert.True(snapshot.VoltageV > 0, "кадры не дали напряжения — проверять было бы нечего");
        Assert.Equal(new CellCount(expected, CellCountSource.Protocol), snapshot.PackCells);
    }

    /// <summary>
    /// Входы каскада наружу не видны и видны быть не должны: их незачем знать никому, кроме
    /// резолвера. Иначе как отсюда их не проверить — ровно потому, что ответ от них сегодня не
    /// зависит (тест выше), а неподанное поле молча выглядит как поданное. Оттого ядро и открыто
    /// тестам через <c>InternalsVisibleTo</c> (решение владельца 09.08.2026).
    /// <para>
    /// Пять ветвей поимённо, а не отражение: шестой декодер уронит этот перебор сразу и внятно, а
    /// заодно спросит своего автора, подаёт ли он проценту то же, что и остальные.
    /// </para>
    /// </summary>
    private static CellCountInputs CellInputsOf(DecoderHarness harness) =>
        harness.Decoder.ProtocolDecoder switch
        {
            GotwayDecoder decoder => decoder.CellInputs(),
            VeteranDecoder decoder => decoder.CellInputs(),
            KingsongDecoder decoder => decoder.CellInputs(),
            InMotionDecoderV2 decoder => decoder.CellInputs(),
            InMotionDecoder decoder => decoder.CellInputs(),
            var other => throw new ArgumentException(
                $"Декодер {other.GetType().Name} не перечислен здесь — подаёт ли он входы каскада?",
                nameof(harness)),
        };

    /// <summary>Кадры живой телеметрии — те же, на которых стоят фикстуры каждого декодера.</summary>
    private static DecoderHarness FedGotway()
    {
        var harness = DecoderHarness.ForGotway();
        harness.FeedHex(
            "55AA19C1000000000000008CF0000001FFF80018",
            "5A5A5A5A55AA000060D248001C20006400010007",
            "000804185A5A5A5A");
        return harness;
    }

    private static DecoderHarness FedVeteran()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex("dc5a5c20266d00004aaf00004aaf000000000d9e", "0b8800000af00af007d2000300050004");
        return harness;
    }

    private static DecoderHarness FedKingSong()
    {
        var harness = DecoderHarness.ForKingSong();
        harness.FeedHex("aa5570176f009649d2020b0a39300f0ea9100000");
        return harness;
    }

    private static DecoderHarness FedInMotion()
    {
        var harness = DecoderHarness.ForInMotion();
        harness.FeedHex(
            "AAAA1301A5550F60000000B4720020FE000100FF",
            "3F00003A18DEFF5D01000029F0FFFF29F0FFFFEC",
            "FFFFFF15200000000000001A1A00000000000000",
            "0000001CE3130000000000000026061A03D20721",
            "0000006F0100006F010000F7010000420C00002B",
            "110000070000000000000000000000265555");
        return harness;
    }

    private static DecoderHarness FedInMotionV2()
    {
        var harness = DecoderHarness.ForInMotionV2();
        harness.FeedHex(
            "AAAA110882010206010201009C",
            "AAAA111D820622080004030F000602214000010110000602230D00010107000001F3",
            "AAAA143184E61EEB0561094A11AE04A004DF01402958CBB000CE004A010000D4FF7C15641900000000492B00000000000000000000C6");
        return harness;
    }

    /// <summary>Снимок таблицы <c>InMotionV2Model.CellsForWheel</c> на день подмены.</summary>
    private static int CellsBeforeTheCascade(InMotionV2Model model) => model switch
    {
        InMotionV2Model.V12HS or InMotionV2Model.V12HT or InMotionV2Model.V12PRO => 24,
        InMotionV2Model.V13 or InMotionV2Model.V13PRO => 30,
        InMotionV2Model.V14g or InMotionV2Model.V14s => 32,
        _ => 20,
    };

    /// <summary>Кадр 0xBB: имя лежит в байтах 2…15, остаток — как в фикстурах KingSong.</summary>
    private static byte[] NameFrame(string wheelName)
    {
        byte[] frame = new byte[20];
        frame[0] = 0xAA;
        frame[1] = 0x55;
        Encoding.ASCII.GetBytes(wheelName).CopyTo(frame, 2);
        frame[16] = 0xBB;
        return frame;
    }
}
