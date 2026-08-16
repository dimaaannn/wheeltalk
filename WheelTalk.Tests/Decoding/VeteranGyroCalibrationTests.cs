using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Services;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Калибровка гироскопа LeaperKim — опкод 21, план импорта команд §1.5 (очередь D).
/// <para>
/// <b>Чем эта команда отличается от остальных настроек.</b> Она не записывает значение, а
/// переключает состояние колеса <b>самим фактом посылки</b>: один и тот же кадр и начинает
/// калибровку, и заканчивает её, а различает их не команда, а состояние, которое колесо сообщает
/// обратно полем <c>gyro</c> (<c>GyroscopeSettingActivity.java:42-56,95-113</c>). Эффект
/// долгоживущий и сам не откатывается — цена ошибки здесь не цифра на экране, а то, как колесо
/// держит человека. Оттого производитель разрешает её только на стоящем колесе, и оттого же
/// половина этого файла — про запрет, а не про байты.
/// </para>
/// <para>
/// <b>На живом колесе не проверено</b>, и проверено не будет: владелец 16.08.2026 отказался
/// калибровать рабочее колесо ради проверки (план §0.1). Всё основание команды — совпадение с
/// байтовым эталоном производителя, который здесь и выписан.
/// </para>
/// </summary>
public class VeteranGyroCalibrationTests
{
    private static IVeteranSettingsCommands Commands(DecoderHarness harness) =>
        (IVeteranSettingsCommands)harness.Decoder.ProtocolDecoder;

    /// <summary>
    /// Тело кадра — <b>дословный литерал производителя</b>, переписанный в том же виде, в каком он
    /// стоит в java: <c>{76, 100, 65, 112, 21, 1, 2, Byte.MIN_VALUE ×9, 1}</c>
    /// (<c>GyroscopeSettingActivity.java:121-123</c>, разбор — <c>leaperkim-official-app.md:230-232</c>).
    /// Здесь он выписан числами, а не hex-строкой, чтобы строку java и строку теста можно было
    /// сличить глазами без пересчёта.
    /// </summary>
    private static readonly byte[] OfficialBody =
    [
        76, 100, 65, 112,            // "LdAp" — одиночный кадр нового поколения
        21,                          // опкод; он же длина готового кадра (17 тела + 4 CRC)
        1, 2,                        // b5/b6 — та же пара «запись настройки», что у прочих шестнадцати
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, // Byte.MIN_VALUE ×9
        1,                           // значение фиксировано: у команды нет ни диапазона, ни выбора
    ];

    /// <summary>CRC32 (IEEE, big-endian) досчитан отдельно стандартным алгоритмом, а не взят у
    /// билдера — то же правило, что у всех байтовых эталонов этого протокола.</summary>
    private const string OfficialCrc = "AB8C09E5";

    // ==================== Байты ====================

    /// <summary>
    /// Кадр совпадает с литералом производителя байт-в-байт: тело дословно, CRC независимо.
    /// Колесо стоит — Patton из фикстуры <c>Decodes_patton_crc</c>, и его нулевая скорость здесь
    /// утверждается явно, иначе тест проверял бы не то, что думает.
    /// </summary>
    [Fact]
    public void CalibrateGyro_MatchesOfficialLiteral()
    {
        var harness = StandingWheel();
        Assert.Equal(0, harness.Snapshot().SpeedRaw);

        byte[]? frame = Commands(harness).BuildCalibrateGyro();

        Assert.NotNull(frame);
        Assert.Equal((byte)frame.Length, frame[4]); // инвариант «длина = опкод»
        Assert.Equal(OfficialBody, frame[..OfficialBody.Length]);
        Assert.Equal(OfficialCrc, Convert.ToHexString(frame[OfficialBody.Length..]));
    }

    /// <summary>Пока колесо не сказало ни слова, скорость нулевая, и команда строится — как у
    /// производителя, где запрет висит на условии <c>carDataPackageInfo != null &amp;&amp;
    /// getSpeed() &gt; 0</c> (<c>GyroscopeSettingActivity.java:66-70</c>): нет данных — нет
    /// запрета.</summary>
    [Fact]
    public void CalibrateGyro_BeforeAnyTelemetry_IsBuilt() =>
        Assert.NotNull(Commands(DecoderHarness.ForVeteran()).BuildCalibrateGyro());

    // ==================== Запрет на ходу ====================

    /// <summary>
    /// <b>Главный замок этой команды.</b> На движущемся колесе кадр не строится вовсе — отказ, а не
    /// собранный и не отправленный буфер: то, что не родилось, нельзя отправить по ошибке ниже по
    /// течению. Фикстура — Sherman L, и она здесь выбрана не случайно (см. соседний тест).
    /// </summary>
    [Fact]
    public void CalibrateGyro_WhileMoving_ReturnsNull() =>
        Assert.Null(Commands(VeteranOutgoingFrames.NewProtocolWheel()).BuildCalibrateGyro());

    /// <summary>
    /// Обратная сторона того же замка, без которой первый ничего не стоит: стоит колесо — кадр
    /// есть. Запрет обязан различать движение и покой, а не молчать всегда.
    /// </summary>
    [Fact]
    public void CalibrateGyro_WhileStanding_IsBuilt() =>
        Assert.NotNull(Commands(StandingWheel()).BuildCalibrateGyro());

    /// <summary>
    /// <b>Почему условие <c>!= 0</c>, а не оригинальное <c>&gt; 0</c>.</b> Знак скорости у нас
    /// настраиваемый (<c>veteranNegative</c>, <c>VeteranDecoder.cs:136-145</c>), и фикстура
    /// Sherman L катится <b>назад</b>: −0.2 км/ч. Сравнение «больше нуля» пустило бы калибровку под
    /// едущим человеком — здесь это записано исполняемым, чтобы условие не «упростили» обратно.
    /// </summary>
    [Fact]
    public void MovingFixture_RollsBackwards_SoGreaterThanZeroWouldNotHaveCaught()
    {
        var harness = VeteranOutgoingFrames.NewProtocolWheel();

        Assert.True(harness.Snapshot().SpeedRaw < 0);
        Assert.Null(Commands(harness).BuildCalibrateGyro());
    }

    // ==================== Порт не тронут (план §2.4) ====================

    /// <summary>
    /// Соседний <c>BuildCalibrate()</c> — часть 1:1-порта <c>VeteranAdapter.java</c>, у которого
    /// калибровки нет вовсе, и он честно отдаёт <c>null</c> на стоящем колесе тоже. Новая команда
    /// его не подменяет и не «чинит»: это разные команды и разные варианты <c>WheelCommand</c>.
    /// Тест стоит затем, чтобы попытка переиспользовать порт под новую калибровку упала здесь.
    /// </summary>
    [Fact]
    public void PortedCalibrate_StaysNull_WhileGyroCalibrationBuilds()
    {
        var harness = StandingWheel();
        var decoder = (VeteranDecoder)harness.Decoder.ProtocolDecoder;

        Assert.Null(decoder.BuildCalibrate());
        Assert.NotNull(((IVeteranSettingsCommands)decoder).BuildCalibrateGyro());
    }

    // ==================== Через диспетчер ====================

    /// <summary>
    /// Запрет держится и на пути до провода: на ходу <c>WheelService</c> не пишет ничего (общая
    /// ветка «команда пропущена»), на стоянке уходит ровно эталонный кадр. Диспетчер здесь ничего
    /// не решает сам — решение живёт в декодере, у которого есть скорость.
    /// </summary>
    [Fact]
    public async Task CalibrateGyro_ReachesTheWire_OnlyWhileStanding()
    {
        var (movingService, movingTransport) = ServiceFor(VeteranOutgoingFrames.NewProtocolWheel());
        await movingService.SendCommand(new WheelCommand.CalibrateGyro());
        Assert.Empty(movingTransport.Written);

        var (standingService, standingTransport) = ServiceFor(StandingWheel());
        await standingService.SendCommand(new WheelCommand.CalibrateGyro());

        Assert.Equal(
            [Convert.ToHexString(OfficialBody) + OfficialCrc],
            standingTransport.Written.Select(Convert.ToHexString));
    }

    /// <summary>Чужому протоколу калибровка не отдаётся вовсе: примерка
    /// <c>as IVeteranSettingsCommands</c> не срастается, и на провод не уходит ничего.</summary>
    [Fact]
    public async Task A_non_Veteran_decoder_gets_no_gyro_calibration()
    {
        var (service, transport) = ServiceFor(DecoderHarness.ForGotway());

        await service.SendCommand(new WheelCommand.CalibrateGyro());

        Assert.Empty(transport.Written);
    }

    /// <summary>Patton, версия «004.0.12» — та же фикстура, что у <c>Decodes_patton_crc</c>:
    /// единственная ветеранская запись в тестах, где колесо действительно стоит.</summary>
    private static DecoderHarness StandingWheel()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(
            "dc5a5c452abe00003edc00008562003500000b5c",
            "0dfe000002bc07d00fac000219fb0000006f0000",
            "80808080808004000014ffffffffff32ee029109",
            "df0fd303cb000000006f9a79c2");
        return harness;
    }

    private static (WheelService Service, FakeTransport Transport) ServiceFor(DecoderHarness harness)
    {
        var transport = new FakeTransport();
        return (new WheelService(transport, harness.Decoder, NullLogger<WheelService>.Instance), transport);
    }
}
