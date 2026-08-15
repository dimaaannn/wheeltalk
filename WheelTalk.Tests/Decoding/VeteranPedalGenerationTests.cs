using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Services;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Поколения педалей — <c>docs/veteran-commands-import-plan.md</c> §1.6, §5.3, этап 6.
/// <para>
/// Настройка педалей у LeaperKim существует в двух несовместимых видах: три именованных положения
/// (порт WheelLog, <c>SETh</c>/<c>SETm</c>/<c>SETs</c>) и плавная шкала 0..100 на опкоде 15. Какой
/// из двух понимает колесо, до разбора входящего кадра настроек мы узнаём только по версии
/// протокола — и для пяти известных моделей не узнаём вовсе. Отсюда третий, честный ответ таблицы:
/// <see cref="PedalGeneration.Unknown"/>, на котором не строится <b>ни одна</b> из двух команд.
/// </para>
/// </summary>
public class VeteranPedalGenerationTests
{
    private static IVeteranSettingsCommands Continuous() =>
        (IVeteranSettingsCommands)VeteranOutgoingFrames.NewProtocolWheel().Decoder.ProtocolDecoder;

    /// <summary>
    /// Колесо неизвестного поколения: та же фикстура Sherman L, что и всюду, но с версией протокола
    /// 8 (Oryx) — байты 28-29 переписаны на 8000 (<c>0x1F40</c>, формула <c>VeteranDecoder.cs:117</c>),
    /// CRC32 кадра пересчитан. Настоящей записи с Oryx у нас нет, а поколение решается ровно этими
    /// двумя байтами — большего для проверки не нужно.
    /// </summary>
    private static DecoderHarness UnknownGenerationWheel()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(
            "dc5a5c53397afffe0aa400000df10000000a0b3d",
            "0e0e0000037a03521f400064000e00b480c80000",
            "808080808080058080808080800ff30ff50ff50f",
            "f50ff50fef0ff20ff30ff30ff30ff30fed0ff30f",
            "f40ff5d0e198fe");
        return harness;
    }

    // ==================== Байты команды ====================

    /// <summary>
    /// `pedalHardness`, опкод 15/<c>0x0F</c>, b6=2, 0..100 — литерал
    /// <c>PedalSoftnessSettingActivity.java:37</c>: <c>{76, 100, 65, 112, 15, 1, 2, MIN, MIN, MIN,
    /// progressToCmdValue(i)}</c>, где преобразование ползунка тождественно (<c>:16-18</c>), а его
    /// предел — 100 (<c>:11-13</c>, <c>BaseSetProgressActivity.java:46</c>). Три заполнителя, а не
    /// иное число, — сходится с инвариантом «длина = опкод»: 4+1+1+1+3+1+4 = 15.
    /// CRC32 досчитан независимо (<c>zlib.crc32</c>, big-endian), не взят у билдера.
    /// </summary>
    [Theory]
    [InlineData(0, "4C6441700F0102808080007A5BDDC6")]
    [InlineData(50, "4C6441700F010280808032B28C8C46")]
    [InlineData(100, "4C6441700F01028080806430847887")]
    public void SetPedalHardness_MatchesOfficialFrame(int percent, string expectedHex)
    {
        byte[]? frame = Continuous().BuildSetPedalHardness(percent);

        Assert.NotNull(frame);
        Assert.Equal((byte)frame.Length, frame[4]);
        Assert.Equal(Convert.FromHexString(expectedHex), frame);
    }

    /// <summary>Вне диапазона производителя команда не строится вовсе — то же правило, что у
    /// остальных настроек (<see cref="VeteranSettingsCommandBytesTests"/>).</summary>
    [Fact]
    public void SetPedalHardness_OutOfRange_ReturnsNull()
    {
        var wheel = Continuous();

        Assert.Null(wheel.BuildSetPedalHardness(-1));
        Assert.Null(wheel.BuildSetPedalHardness(101));
    }

    // ==================== Таблица поколений ====================

    /// <summary>
    /// Все тринадцать версий протокола, которые мы вообще умеем называть по имени
    /// (<c>VeteranDecoder.GetModel</c>, <c>VeteranDecoder.cs:357-372</c>). Первые семь совпадают с
    /// каталогом производителя дословно по имени модели (<c>Util.CAR_DATA_JSON</c>,
    /// <c>leaperkim-official-app.md</c> §5.1), и оттуда же взят их <c>continuousSoftHardSet</c>.
    /// Последних пяти в каталоге нет вовсе — потому им <see cref="PedalGeneration.Unknown"/>, а не
    /// догадка по соседней модели.
    /// </summary>
    [Theory]
    [InlineData(0, PedalGeneration.ThreePosition)]   // Sherman
    [InlineData(1, PedalGeneration.ThreePosition)]   // Sherman (тот же, ≤1)
    [InlineData(2, PedalGeneration.ThreePosition)]   // Abrams
    [InlineData(3, PedalGeneration.ThreePosition)]   // Sherman S
    [InlineData(4, PedalGeneration.ThreePosition)]   // Patton
    [InlineData(5, PedalGeneration.Continuous)]      // Lynx
    [InlineData(6, PedalGeneration.Continuous)]      // Sherman L
    [InlineData(7, PedalGeneration.Continuous)]      // Patton S
    [InlineData(8, PedalGeneration.Unknown)]         // Oryx — в каталоге производителя нет
    [InlineData(9, PedalGeneration.Unknown)]         // Lynx S — нет
    [InlineData(42, PedalGeneration.Unknown)]        // Nosfet Apex — нет
    [InlineData(43, PedalGeneration.Unknown)]        // Nosfet Aero — нет
    [InlineData(44, PedalGeneration.Unknown)]        // Nosfet Aeon — нет
    public void KnownProtocolVersions_MapToTheirGeneration(int protocolVersion, PedalGeneration expected) =>
        Assert.Equal(expected, PedalGenerations.FromProtocolVersion(protocolVersion));

    /// <summary>Версия, которой мы не знаем вовсе (колесо новее нашей таблицы), — тот же явный
    /// <see cref="PedalGeneration.Unknown"/>, а не «ближайшее похожее»: молчание источника и
    /// молчание колеса для этого решения одно и то же.</summary>
    [Theory]
    [InlineData(10)]
    [InlineData(41)]
    [InlineData(45)]
    [InlineData(100)]
    public void UnknownProtocolVersion_IsUnknownGeneration(int protocolVersion) =>
        Assert.Equal(PedalGeneration.Unknown, PedalGenerations.FromProtocolVersion(protocolVersion));

    /// <summary>Поколение колеса берётся из его собственной телеметрии, а не назначается снаружи:
    /// фикстура Sherman L даёт плавную шкалу, та же фикстура с версией 8 — неизвестность.</summary>
    [Fact]
    public void WheelReportsItsGeneration_FromDecodedTelemetry()
    {
        var oryx = UnknownGenerationWheel();
        Assert.Equal("008.0.00", oryx.Snapshot().Version); // фикстура действительно перешита

        Assert.Equal(PedalGeneration.Continuous, Continuous().Pedals);
        Assert.Equal(PedalGeneration.Unknown, ((IVeteranSettingsCommands)oryx.Decoder.ProtocolDecoder).Pedals);
    }

    // ==================== Fail-closed: диспетчер ====================

    private static (WheelService Service, FakeTransport Transport) ServiceFor(DecoderHarness harness)
    {
        var transport = new FakeTransport();
        return (new WheelService(transport, harness.Decoder, NullLogger<WheelService>.Instance), transport);
    }

    /// <summary>
    /// Главное утверждение этапа: колесу неизвестного поколения не уходит <b>ни одна</b> из двух
    /// педальных команд — ни плавная шкала (её отсекает билдер), ни три положения (их строит порт
    /// WheelLog, потому замок стоит в <c>WheelService</c>). Обе молча пропускаются, как всякая
    /// непостроенная команда.
    /// </summary>
    [Fact]
    public async Task UnknownGeneration_SendsNeitherPedalCommand()
    {
        var (service, transport) = ServiceFor(UnknownGenerationWheel());

        await service.SendCommand(new WheelCommand.SetPedalHardness(50));
        for (int mode = 0; mode <= 2; mode++) await service.SendCommand(new WheelCommand.SetPedalsMode(mode));

        Assert.Empty(transport.Written);
    }

    /// <summary>Обратная сторона замка: на колесе, чьё поколение известно, обе дороги открыты —
    /// плавная шкала уходит кадром опкода 15, три положения остаются текстом порта. Иначе
    /// «fail-closed» ничего не стоил бы: молчать на всё умеет и оборванный провод.</summary>
    [Fact]
    public async Task ContinuousGeneration_SendsBothForms()
    {
        var (service, transport) = ServiceFor(VeteranOutgoingFrames.NewProtocolWheel());

        await service.SendCommand(new WheelCommand.SetPedalHardness(50));
        await service.SendCommand(new WheelCommand.SetPedalsMode(1));

        Assert.Equal(
            ["4C6441700F010280808032B28C8C46", "SETm"],
            transport.Written.Select(w => w[0] == 'L' ? Convert.ToHexString(w) : System.Text.Encoding.ASCII.GetString(w)));
    }

    /// <summary>Старому колесу плавная шкала не уходит: опкода 15 оно не понимает, и запись ушла бы
    /// в незнакомый регистр прошивки. Три положения при этом остаются доступны — это его штатная
    /// форма настройки.</summary>
    [Fact]
    public async Task ThreePositionGeneration_SendsOnlyTheThreePositionCommand()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex( // Patton, версия «004.0.12» — фикстура VeteranDecoderTests.Decodes_patton_crc
            "dc5a5c452abe00003edc00008562003500000b5c",
            "0dfe000002bc07d00fac000219fb0000006f0000",
            "80808080808004000014ffffffffff32ee029109",
            "df0fd303cb000000006f9a79c2");
        var (service, transport) = ServiceFor(harness);

        await service.SendCommand(new WheelCommand.SetPedalHardness(50));
        await service.SendCommand(new WheelCommand.SetPedalsMode(1));

        Assert.Equal("SETm", System.Text.Encoding.ASCII.GetString(Assert.Single(transport.Written)));
    }
}
