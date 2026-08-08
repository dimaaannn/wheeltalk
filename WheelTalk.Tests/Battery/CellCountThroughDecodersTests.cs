using System.Text;
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

        Assert.Equal(expected, ((GotwayDecoder)harness.Decoder.ProtocolDecoder).GetCellsForWheel());
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

        Assert.Equal(expected, ((VeteranDecoder)harness.Decoder.ProtocolDecoder).GetCellsForWheel());
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

        Assert.Equal(expected, ((KingsongDecoder)harness.Decoder.ProtocolDecoder).GetCellsForWheel());
    }

    /// <summary>У InMotion первого поколения ряд один на все модели.</summary>
    [Fact]
    public void InMotion_v1_keeps_its_single_answer()
    {
        var harness = DecoderHarness.ForInMotion();

        Assert.Equal(20, ((InMotionDecoder)harness.Decoder.ProtocolDecoder).GetCellsForWheel());
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

            Assert.Equal(CellsBeforeTheCascade(model), decoder.GetCellsForWheel());
        }
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
