using WheelTalk.Core.Decoding;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Замки плана 36 Л3 — <b>опрос InMotion V2 задаётся временем, а не колесом</b>.
/// <para>
/// Болезнь была такая: тик 25 мс, а счётчик обнулялся на каждом принятом кадре
/// (<c>InMotionDecoderV2.cs:93-95,154</c>) — круг замыкался через колесо, и нагрузка росла тем
/// сильнее, чем быстрее оно отвечает: 16–40 сообщений в секунду против 1–2 у производителя и 4–5 у
/// DarknessBot (мастер-план §8). Ни один тест этого не замечал, потому что часы в них стоят.
/// </para>
/// <para>
/// Здесь они идут. Главный замок — первый: два прогона одинаковой длины, молчащее колесо и колесо,
/// отвечающее на каждый запрос, <b>обязаны</b> дать одно и то же число исходящих.
/// </para>
/// </summary>
public class InMotionPollTests
{
    /// <summary>Тип колеса: series 6, type 1 — Inmotion V11 (кадр из <see cref="InMotionDecoderV2Tests"/>).</summary>
    private const string CarTypeV11 = "AAAA110882010206010201009C";

    private const string SerialNumber = "AAAA11178202313438304341313232323037303032420000000000FD";

    private const string RealTime =
        "aaaa1431843020a5a50068025207870080009400882c5fc4b000d7001000f4ff2b037c1564190000d9d9492b00000000000000000000a5a5";

    private static readonly TimeSpan Run = TimeSpan.FromSeconds(10);

    /// <summary>
    /// <b>Замок мастер-плана.</b> Колесо не управляет темпом: сколько бы кадров ни пришло между
    /// тиками, исходящих будет ровно столько, сколько отмерил таймер. Это и есть детектор возврата
    /// к ответозависимости — вернуть <c>_updateStep = 0</c> и не уронить этот тест нельзя.
    /// </summary>
    [Fact]
    public void The_wheel_cannot_speed_up_the_poll()
    {
        int silent = RequestsOver(Run, answering: false).Count;
        int answering = RequestsOver(Run, answering: true).Count;

        Assert.Equal(silent, answering);
    }

    /// <summary>
    /// Темп сам по себе: заводские 250 мс — это 4 запроса в секунду, темп DarknessBot. Первый
    /// уходит через 100 мс после подключения, поэтому за 10 секунд их сорок.
    /// </summary>
    [Fact]
    public void The_rate_is_the_one_the_setting_asks_for()
    {
        Assert.InRange(RequestsOver(Run, answering: true).Count, 39, 41);
    }

    /// <summary>Настройка и вправду правит темпом: секундный шаг — вчетверо меньше запросов.</summary>
    [Fact]
    public void A_longer_period_makes_a_slower_poll()
    {
        Assert.InRange(RequestsOver(Run, answering: true, periodMs: 1000).Count, 9, 11);
    }

    /// <summary>Настройка за границами не пускает опрос ни в прежнюю болезнь, ни в спячку.</summary>
    [Theory]
    [InlineData(0, 39, 41)]
    [InlineData(25, 39, 41)]
    [InlineData(100000, 9, 11)]
    public void The_period_stays_inside_its_bounds(int periodMs, int least, int most)
    {
        Assert.InRange(RequestsOver(Run, answering: true, periodMs).Count, least, most);
    }

    /// <summary>
    /// Состав круга: телеметрия дважды, одометр однажды — круг DarknessBot. У порта они чередуются
    /// один к одному, и половина запросов уходила на пробег, который меняется раз в сотни метров.
    /// </summary>
    [Fact]
    public void A_circle_asks_telemetry_twice_and_the_odometer_once()
    {
        var steady = RequestsOver(Run, answering: true).Skip(5).ToList();

        int telemetry = steady.Count(request => request.SequenceEqual(InMotionV2Message.GetRealTimeData().WriteBuffer()));
        int odometer = steady.Count(request => request.SequenceEqual(InMotionV2Message.GetStatistics().WriteBuffer()));

        Assert.Equal(steady.Count, telemetry + odometer);
        Assert.InRange(telemetry, 2 * odometer - 1, 2 * odometer + 1);
    }

    /// <summary>
    /// Лестница доходит до круга у колеса, которое отвечает лишь на два первых запроса, — и
    /// доходит по порядку порта: тип, серийник, версии, настройки, «бесполезные данные», дальше
    /// телеметрия.
    /// </summary>
    [Fact]
    public void The_handshake_walks_the_ladder_and_reaches_the_cycle()
    {
        var asked = RequestsOver(Run, answering: true);

        Assert.Equal(
        [
            InMotionV2Message.GetCarType().WriteBuffer(),
            InMotionV2Message.GetSerialNumber().WriteBuffer(),
            InMotionV2Message.GetVersions().WriteBuffer(),
            InMotionV2Message.GetCurrentSettings().WriteBuffer(),
            InMotionV2Message.GetUselessData().WriteBuffer(),
            InMotionV2Message.GetRealTimeData().WriteBuffer(),
        ], asked.Take(6));
    }

    /// <summary>
    /// Молчащее колесо переспрашивается о том же, о чём и порт: без типа колеса раскладка
    /// телеметрии неизвестна, и просить её не о чем.
    /// </summary>
    [Fact]
    public void A_silent_wheel_is_asked_who_it_is_and_nothing_else()
    {
        var asked = RequestsOver(Run, answering: false);

        Assert.All(asked, request => Assert.Equal(InMotionV2Message.GetCarType().WriteBuffer(), request));
    }

    /// <summary>
    /// Опрос порта молчит: живое колесо приходит через надстройку, и тик 25 мс, будь он жив, дал бы
    /// за десять секунд четыре сотни запросов вместо сорока.
    /// </summary>
    [Fact]
    public void The_ported_keep_alive_timer_is_silenced()
    {
        Assert.True(RequestsOver(Run, answering: true).Count < 100);
    }

    /// <summary>
    /// Прогон по виртуальному времени. <paramref name="answering"/> — колесо, отвечающее кадром на
    /// <b>каждый</b> запрос, и отвечающее по делу: спросили тип — назвало тип, спросили серийник —
    /// назвало серийник, на прочее шлёт телеметрию. Ровно этот поток и разгонял прежний опрос:
    /// приём кадра обнулял счётчик, и следующий запрос уходил через 25 мс.
    /// </summary>
    private static List<byte[]> RequestsOver(TimeSpan span, bool answering, int periodMs = 250)
    {
        var harness = DecoderHarness.ForInMotionV2_1(config => config.InMotionPollPeriodMs = periodMs);
        var decoder = harness.Decoder.ProtocolDecoder;
        var asked = new List<byte[]>();

        decoder.WriteRequested += request =>
        {
            asked.Add(request);
            if (!answering) return;

            decoder.Decode(Convert.FromHexString(AnswerTo(request)));
        };

        harness.Time.Advance(span);
        return asked;
    }

    private static string AnswerTo(byte[] request)
    {
        if (request.SequenceEqual(InMotionV2Message.GetCarType().WriteBuffer())) return CarTypeV11;
        return request.SequenceEqual(InMotionV2Message.GetSerialNumber().WriteBuffer()) ? SerialNumber : RealTime;
    }
}
