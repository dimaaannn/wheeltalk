using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Playback;
using WheelTalk.Core.Settings.Device;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Settings;

/// <summary>
/// Жёсткость педалей у разных поколений колёс (план 34 §6, этап 4). Одно место экрана, две разные
/// строки: у новых колёс — плавная шкала 0..100 со страницы 8, у старых — режим езды из байта 31
/// кадра телеметрии. <b>Развилку решает сентинел, а не таблица моделей</b> (§1.3).
/// <para>
/// Описания живут в android-проекте, тестам не референсном, поэтому правило показа читается по
/// исходнику (приём <c>WheelDevicePageRulesTests</c>), а его посылки — настоящими кадрами: запись
/// Sherman L 28.07.2026 и портированные фикстуры Abrams и Patton.
/// </para>
/// </summary>
public class PedalHardnessByGenerationTests
{
    private const string PageFile = "WheelTalk.Droid/Settings/Catalogue/WheelDevicePage.cs";

    /// <summary>
    /// (а) Шаг 4.1. Новое колесо: плавная жёсткость приходит значением (94), и режима езды у него
    /// нет — байт 31 сентинел. Обе половины правила говорят «строке режима здесь не место», и
    /// сказало это само колесо.
    /// </summary>
    [Fact]
    public async Task A_new_wheel_reports_the_smooth_scale_and_no_ride_mode_at_all()
    {
        var snapshot = await ShermanLRide();

        Assert.Equal(6, snapshot.ProtocolVersion);
        Assert.NotNull(snapshot.WheelSettings);

        var pedalHardness = snapshot.WheelSettings[WheelSettingKeys.PedalHardness];
        Assert.True(pedalHardness.Supported);
        Assert.Equal(94, pedalHardness.Value);

        Assert.Equal((byte?)VeteranSettingsPage.NoSuchSetting, snapshot.RideModeRaw);
    }

    /// <summary>
    /// (б) Шаг 4.2. Старое колесо: байт 31 несёт положение, а страницы настроек нет вовсе — она
    /// разбирается только от пятого поколения (<c>VeteranDecoder.DecodeSmartBms</c>). Отсюда
    /// главное для правила показа: <b>строка режима не вправе требовать снимка</b> — потребуй она
    /// его, и у единственных колёс, которым она нужна, её бы никогда не было.
    /// </summary>
    [Theory]
    [InlineData(2, (byte)3, "dc5a5c20266d00004aaf00004aaf000000000d9e", "0b8800000af00af007d2000300050004")]
    [InlineData(4, (byte)2, "dc5a5c452abe00003edc00008562003500000b5c",
        "0dfe000002bc07d00fac000219fb0000006f0000" +
        "80808080808004000014ffffffffff32ee029109" +
        "df0fd303cb000000006f9a79c2")]
    public void An_old_wheel_has_no_settings_page_but_reports_a_ride_mode_position(
        int protocolVersion, byte rideMode, string head, string tail)
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(head, tail);

        var snapshot = harness.Snapshot();

        Assert.Equal(protocolVersion, snapshot.ProtocolVersion);
        Assert.Null(snapshot.WheelSettings);
        Assert.Equal((byte?)rideMode, snapshot.RideModeRaw);
        Assert.NotEqual((byte?)VeteranSettingsPage.NoSuchSetting, snapshot.RideModeRaw);
    }

    /// <summary>
    /// (в) Правило показа в самом описании: строка режима есть, только когда колесо сказало «нет»
    /// дважды — байтом 31 не сентинел и плавной жёсткости не сообщило. Сентинел один на оба места
    /// (<see cref="VeteranSettingsPage.NoSuchSetting"/>), числа <c>0x80</c> в описании нет.
    /// </summary>
    [Fact]
    public void The_row_appears_by_the_sentinel_and_by_nothing_else()
    {
        string page = RepoFiles.Read(PageFile);
        string rideMode = RepoFiles.MethodBody(page, "private static WheelSettingValue RideMode(");

        Assert.Contains("raw == VeteranSettingsPage.NoSuchSetting", rideMode);
        Assert.Contains("[WheelSettingKeys.PedalHardness].Supported == true", rideMode);
        Assert.DoesNotContain("0x80", rideMode);
        Assert.DoesNotContain("128", rideMode);

        // Ни модели, ни поколения в развилке: сентинел точнее любой таблицы (§1.3).
        Assert.DoesNotContain("ProtocolVersion", rideMode);
        Assert.DoesNotContain("Model", rideMode);

        // Строк две, и обе на своих местах: плавная шкала 0..100 и три положения 1..3.
        string build = RepoFiles.MethodBody(page, "public static IReadOnlyList<SettingDescriptor> Build(");
        Assert.Contains("WheelSettingKeys.PedalHardness, ReportedSection, \"SettingWheelDevicePedalHardness\"", build);
        Assert.Contains("WheelSettingKeys.RideMode, ReportedSection, \"SettingWheelDeviceRideMode\"", build);
        Assert.Contains("SettingKind.Number, min: 1, max: 3", build);
    }

    /// <summary>
    /// (г) Шаг 4.3. Поколение доезжает до снимка числом из кадра, а не догадкой о модели: имя
    /// модели — это же число, переведённое таблицей, и сверяется здесь с ним. До первого кадра
    /// поколение неизвестно, и это ноль, а не единица.
    /// </summary>
    [Fact]
    public async Task The_generation_reaches_the_snapshot_as_a_fact_from_the_frame()
    {
        Assert.Equal(0, DecoderHarness.ForVeteran().Snapshot().ProtocolVersion);

        var shermanL = await ShermanLRide();
        Assert.Equal(6, shermanL.ProtocolVersion);
        Assert.Equal("Sherman L", shermanL.Model);

        // Число — старшая часть кода версии того же кадра, а не отдельное поле, которое могло бы
        // разойтись с ним: «006.0.10» и 6 — одно и то же, сказанное дважды.
        Assert.Equal(shermanL.ProtocolVersion, int.Parse(shermanL.Version.Split('.')[0]));
    }

    /// <summary>Запись поездки Sherman L целиком, проигранная тем же транспортом, что и в бою.</summary>
    private static async Task<TelemetrySnapshot> ShermanLRide()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "shermanl_raw_ride_20260728.csv");

        var harness = DecoderHarness.ForVeteran();
        var transport = new ReplayTransport(
            () => new StreamReader(path), TimeProvider.System, NullLogger<ReplayTransport>.Instance);
        transport.DataReceived += frame => harness.Decoder.Feed(frame);
        await transport.PlayAsync(realtime: false);

        return harness.Snapshot();
    }
}
