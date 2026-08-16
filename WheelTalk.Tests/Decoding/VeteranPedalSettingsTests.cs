using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Services;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Педали LeaperKim — жёсткость (опкод 15) и поколение колеса, решающее вид «режима езды» (опкод 12).
/// План импорта команд §1.6, §5; архитектура настроек <c>docs/wheel-settings-architecture.md</c> §7.
/// <para>
/// <b>Различение, ради которого этот файл существует.</b> «Жёсткость педалей» и «режим езды» — не
/// две формы одной настройки, а две разные, взаимоисключающие настройки на разных опкодах
/// (<c>ControlActivity.java:392-395</c>: пришла жёсткость сентинелом <c>128</c> — показывается
/// «режим езды», и наоборот). У жёсткости шкала <b>всегда</b> плавная, и поколение колеса ей
/// безразлично; каталожный признак <c>continuousSoftHardSet</c> спрашивается только во второй ветке
/// (<c>SetRideModeActivity.java:69</c>). Проверяется здесь и то, и другое — вместе, чтобы замок не
/// сполз обратно на соседнюю команду.
/// </para>
/// </summary>
public class VeteranPedalSettingsTests
{
    private static IVeteranSettingsCommands Commands(DecoderHarness harness) =>
        (IVeteranSettingsCommands)harness.Decoder.ProtocolDecoder;

    /// <summary>Patton, версия «004.0.12» — фикстура <c>VeteranDecoderTests.Decodes_patton_crc</c>,
    /// старое поколение (версия железа <c>0040</c>, каталог: <c>continuousSoftHardSet: false</c>).</summary>
    private static DecoderHarness ThreePositionWheel()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(
            "dc5a5c452abe00003edc00008562003500000b5c",
            "0dfe000002bc07d00fac000219fb0000006f0000",
            "80808080808004000014ffffffffff32ee029109",
            "df0fd303cb000000006f9a79c2");
        return harness;
    }

    /// <summary>
    /// Колесо, которого нет в каталоге производителя: та же фикстура Sherman L, что и всюду, но с
    /// версией протокола 8 (Oryx) — байты 28-29 переписаны на 8000 (<c>0x1F40</c>, формула
    /// <c>VeteranDecoder.cs:117</c>), CRC32 кадра пересчитан. Настоящей записи с Oryx у нас нет, а
    /// поколение решается ровно этими двумя байтами.
    /// </summary>
    private static DecoderHarness UncataloguedWheel()
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

    // ==================== Байты жёсткости педалей ====================

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
        byte[]? frame = Commands(DecoderHarness.ForVeteran()).BuildSetPedalHardness(percent);

        Assert.NotNull(frame);
        Assert.Equal((byte)frame.Length, frame[4]);
        Assert.Equal(Convert.FromHexString(expectedHex), frame);
    }

    /// <summary>Вне диапазона производителя команда не строится вовсе — то же правило, что у
    /// остальных настроек (<see cref="VeteranSettingsCommandBytesTests"/>).</summary>
    [Fact]
    public void SetPedalHardness_OutOfRange_ReturnsNull()
    {
        var wheel = Commands(DecoderHarness.ForVeteran());

        Assert.Null(wheel.BuildSetPedalHardness(-1));
        Assert.Null(wheel.BuildSetPedalHardness(101));
    }

    /// <summary>
    /// Замок против соблазна, на котором эта работа уже один раз оступилась: жёсткость педалей
    /// <b>не спрашивает поколение</b>. Кадр один и тот же у старого Patton, у нового Sherman L и у
    /// колеса, которого нет в каталоге, — потому что у опкода 15 шкала всегда плавная, а есть ли
    /// настройка у колеса, скажет сентинел <c>128</c> в его телеметрии, а не таблица моделей
    /// (<c>ControlActivity.java:392-395</c>).
    /// </summary>
    [Fact]
    public void SetPedalHardness_IsTheSameFrameOnEveryGeneration()
    {
        byte[] expected = Convert.FromHexString("4C6441700F010280808032B28C8C46");

        foreach (var harness in new[] { ThreePositionWheel(), VeteranOutgoingFrames.NewProtocolWheel(), UncataloguedWheel() })
        {
            Assert.Equal(expected, Commands(harness).BuildSetPedalHardness(50));
        }
    }

    // ==================== Таблица поколений: вид «режима езды» ====================

    /// <summary>
    /// Все версии протокола, которые мы умеем называть по имени (<c>VeteranDecoder.cs:357-372</c>),
    /// плюс ноль — «телеметрии ещё не было». Границу даёт каталог производителя по версии железа с
    /// порогом <c>0050</c> (<c>Util.CAR_DATA_JSON</c>, разбор — архитектура настроек §7): три
    /// положения только у Sherman/Abrams/Sherman S/Patton. Остальным — плавная шкала, и пяти
    /// моделям вне каталога тоже: <b>по умолчанию</b>, решением владельца 16.08.2026, а не потому,
    /// что признак нашёлся.
    /// </summary>
    [Theory]
    [InlineData(0, RideModeScale.Continuous)]      // версии ещё нет — умолчание
    [InlineData(1, RideModeScale.ThreePosition)]   // Sherman, железо 0010/0011
    [InlineData(2, RideModeScale.ThreePosition)]   // Abrams, 0020
    [InlineData(3, RideModeScale.ThreePosition)]   // Sherman S, 0030
    [InlineData(4, RideModeScale.ThreePosition)]   // Patton, 0040
    [InlineData(5, RideModeScale.Continuous)]      // Lynx, 0050 — порог каталога
    [InlineData(6, RideModeScale.Continuous)]      // Sherman L, 0060
    [InlineData(7, RideModeScale.Continuous)]      // Patton S, 0070
    [InlineData(8, RideModeScale.Continuous)]      // Oryx — в каталоге нет, умолчание
    [InlineData(9, RideModeScale.Continuous)]      // Lynx S — нет
    [InlineData(42, RideModeScale.Continuous)]     // Nosfet Apex — нет
    [InlineData(43, RideModeScale.Continuous)]     // Nosfet Aero — нет
    [InlineData(44, RideModeScale.Continuous)]     // Nosfet Aeon — нет
    public void KnownProtocolVersions_MapToTheirRideModeScale(int protocolVersion, RideModeScale expected) =>
        Assert.Equal(expected, RideModeScales.FromProtocolVersion(protocolVersion));

    /// <summary>
    /// Замок на умолчание: версия, которой мы не знаем вовсе (колесо новее нашей таблицы), даёт
    /// плавную шкалу, а не молчание и не «ближайшее похожее». Так поступает и производитель —
    /// <c>carInfoByHardVersion == null || isContinuousSoftHardSet()</c>
    /// (<c>SetRideModeActivity.java:69</c>): нет карточки модели — ползунок.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(41)]
    [InlineData(45)]
    [InlineData(100)]
    public void UnknownProtocolVersion_FallsBackToContinuous(int protocolVersion) =>
        Assert.Equal(RideModeScale.Continuous, RideModeScales.FromProtocolVersion(protocolVersion));

    /// <summary>Поколение колесо называет само, своей телеметрией, а не настройками приложения:
    /// Patton — три положения, Sherman L — плавная шкала, Oryx (вне каталога) — плавная по
    /// умолчанию.</summary>
    [Fact]
    public void RideModeScale_ComesFromDecodedTelemetry()
    {
        var uncatalogued = UncataloguedWheel();
        Assert.Equal("008.0.00", uncatalogued.Snapshot().Version); // фикстура действительно перешита

        Assert.Equal(RideModeScale.ThreePosition, Commands(ThreePositionWheel()).RideModeScale);
        Assert.Equal(RideModeScale.Continuous, Commands(VeteranOutgoingFrames.NewProtocolWheel()).RideModeScale);
        Assert.Equal(RideModeScale.Continuous, Commands(uncatalogued).RideModeScale);
    }

    // ==================== Диспетчер ====================

    private static (WheelService Service, FakeTransport Transport) ServiceFor(DecoderHarness harness)
    {
        var transport = new FakeTransport();
        return (new WheelService(transport, harness.Decoder, NullLogger<WheelService>.Instance), transport);
    }

    /// <summary>
    /// Обе педальные команды доходят до провода на любом поколении, включая колесо вне каталога:
    /// прежнее правило «не знаем — не шлём» отменено владельцем 16.08.2026 («команда не того
    /// формата не должна причинить ущерба колесу»). Отдельно важно, что <c>SetPedalsMode</c>
    /// осталась нетронутой: на Sherman L владельца она работает сегодня, и запрет по поколению
    /// отнял бы работающее.
    /// </summary>
    [Theory]
    [InlineData("uncatalogued")]
    [InlineData("three-position")]
    public async Task BothPedalCommandsReachTheWire_WhateverTheGeneration(string wheel)
    {
        var (service, transport) = ServiceFor(wheel == "uncatalogued" ? UncataloguedWheel() : ThreePositionWheel());

        await service.SendCommand(new WheelCommand.SetPedalHardness(50));
        await service.SendCommand(new WheelCommand.SetPedalsMode(1));

        Assert.Equal(
            ["4C6441700F010280808032B28C8C46", "SETm"],
            transport.Written.Select(w => w[0] == 'L' ? Convert.ToHexString(w) : Encoding.ASCII.GetString(w)));
    }

    /// <summary>Негодное значение до колеса не доезжает и через диспетчер — общая ветка «команда
    /// пропущена», та же, что у остальных настроек.</summary>
    [Fact]
    public async Task OutOfRangeHardness_NeverReachesTheWire()
    {
        var (service, transport) = ServiceFor(VeteranOutgoingFrames.NewProtocolWheel());

        await service.SendCommand(new WheelCommand.SetPedalHardness(101));

        Assert.Empty(transport.Written);
    }

    /// <summary>Чужому протоколу жёсткость педалей не отдаётся вовсе: примерка
    /// <c>as IVeteranSettingsCommands</c> не срастается.</summary>
    [Fact]
    public async Task A_non_Veteran_decoder_gets_no_pedal_hardness()
    {
        var (service, transport) = ServiceFor(DecoderHarness.ForGotway());

        await service.SendCommand(new WheelCommand.SetPedalHardness(50));

        Assert.Empty(transport.Written);
    }
}
