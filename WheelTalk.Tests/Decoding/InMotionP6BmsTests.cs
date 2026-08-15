using WheelTalk.Core.Decoding;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// BMS InMotion P6 — запросы в круге опроса и разбор ответов (мастер-план часть II §9, раскладка —
/// docs/originals-reference-data.md §8).
/// <para>
/// <b>Чего эти замки не проверяют.</b> Ни одного живого кадра BMS у нас нет: наши два дампа P6 сняты
/// декодером, который эту подкоманду не спрашивал. Кадры здесь собраны по раскладке, то есть
/// проверяется <b>наш разбор известной раскладки</b>, а не сама раскладка — её подтвердит дамп с
/// живого колеса. Оттого особый вес имеют замки на молчание: короткий, битый и неправдоподобный
/// ответ обязаны не дать ничего, потому что именно так поведёт себя колесо, у которого раскладка
/// окажется иной.
/// </para>
/// </summary>
public class InMotionP6BmsTests
{
    /// <summary>P6: series 13, type 1. Кадр снят с колеса владельца 02.08.2026.</summary>
    private const string CarTypeP6 = "aaaa11088201020d0101010094";

    /// <summary>Тип колеса из таблицы оригинала: series 6, type 1 — Inmotion V11.</summary>
    private const string CarTypeV11 = "AAAA110882010206010201009C";

    private const string SerialNumber = "AAAA11178202313438304341313232323037303032420000000000FD";

    /// <summary>P6 в покое: пак 230,04 В, заряд паков 98,94 % и 96,90 % — те самые два поля, что
    /// весь дамп держат разницу около двух процентов.</summary>
    private const string P6RealTimeStanding =
        "aaaa145784dc59feff00000000000000001bfc1602000000003900f3fd6400faff5600a600adfca626da25983a983a401f401f401fe02ee02e50c300000000d4e500e0b0dbced1b02800040000000049000000000000010000000085";

    /// <summary>Запрос сводки BMS — конверт 0x14, подкоманда 5, пустое тело. Числа из разбора
    /// оригинала (<c>kg0.e([], 20, 5)</c>), а не из нашей же сборки: иначе замок сверял бы код сам с
    /// собой.</summary>
    private const string SummaryRequest = "AAAA14010510";

    /// <summary>Адресный запрос к плате 36 с селектором 1 — <c>kg0.e([36,1], 22, 2)</c>.</summary>
    private const string DirectRequest36 = "AAAA160302240132";

    private static readonly TimeSpan Run = TimeSpan.FromSeconds(30);

    // --- Круг опроса ---

    /// <summary>
    /// Главное свойство расширенного круга: <b>телеметрия не подвинулась</b>. Две трети запросов её,
    /// как и было, а место под BMS взято у одометра — он ходит шагом в 10 метров, и полутора опросов
    /// в секунду ему не нужно никогда.
    /// </summary>
    [Fact]
    public void The_bms_slot_is_paid_for_by_the_odometer_and_not_by_telemetry()
    {
        var steady = P6Requests(Run).Skip(5).ToList();

        int telemetry = steady.Count(IsTelemetry);
        int odometer = steady.Count(IsOdometer);
        int bms = steady.Count(r => IsSummary(r) || IsDirect(r));

        Assert.Equal(steady.Count, telemetry + odometer + bms);
        // Две трети — телеметрия, оставшаяся треть делится поровну между пробегом и BMS.
        Assert.InRange(telemetry, 2 * (odometer + bms) - 2, 2 * (odometer + bms) + 2);
        Assert.InRange(bms, odometer - 1, odometer + 1);
    }

    /// <summary>
    /// <b>Замок Л3 для P6.</b> Запросы BMS вошли в тот же таймерный круг и ничего не сделали
    /// ответозависимым: молчащее колесо и колесо, отвечающее на каждый запрос, дают одно и то же
    /// число исходящих — и оно то же, что у модели без всякого BMS.
    /// </summary>
    [Fact]
    public void Bms_requests_do_not_let_the_wheel_speed_up_the_poll()
    {
        int silent = P6Requests(Run, answering: false).Count;
        int answering = P6Requests(Run).Count;
        int v11 = Requests(Run, CarTypeV11).Count;

        Assert.Equal(silent, answering);
        Assert.Equal(v11, answering);
    }

    /// <summary>
    /// <b>Замок carType.</b> Сводка BMS у оригинала сохраняется только при carType 131, адресные
    /// запросы мы шлём только туда же: у любой другой модели InMotion круг остаётся прежним, байт в
    /// байт, и ни одного лишнего кадра колесо не увидит.
    /// </summary>
    [Fact]
    public void A_wheel_that_is_not_a_p6_is_never_asked_about_its_bms()
    {
        var asked = Requests(Run, CarTypeV11);

        Assert.DoesNotContain(asked, IsSummary);
        Assert.DoesNotContain(asked, IsDirect);
    }

    /// <summary>Сводка уходит той самой посылкой, что и у оригинала.</summary>
    [Fact]
    public void The_summary_request_is_the_one_the_original_sends()
    {
        Assert.Contains(P6Requests(Run), IsSummary);
    }

    /// <summary>
    /// Разведка: какие из шести плат живы у P6, не знает никто — ни наши записи, ни разбор. Оттого
    /// каждая спрашивается по разу, и <b>только по разу</b>: колесо, которое молчит на этот конверт,
    /// не должно платить за нашу неосведомлённость всю поездку.
    /// </summary>
    [Fact]
    public void All_six_boards_are_probed_once_and_then_left_alone()
    {
        var direct = P6Requests(TimeSpan.FromMinutes(2)).Where(IsDirect).ToList();

        Assert.Equal(6, direct.Count);
        Assert.Equal([36, 37, 38, 39, 50, 52], direct.Select(r => (int)r[5]));
        Assert.All(direct, r => Assert.Equal(1, r[6]));
    }

    /// <summary>
    /// Плата, которая отозвалась, дальше спрашивается по обоим селекторам: реальное время несёт
    /// температуры и границы банок, список банок — сами банки. Список — единственный путь к
    /// «56 ячейкам», сводка их не несёт вовсе.
    /// </summary>
    [Fact]
    public void A_board_that_answered_is_then_asked_for_its_cells()
    {
        var direct = P6Requests(Run, answerBms: true).Where(IsDirect).ToList();

        Assert.Contains(direct, r => r[5] == 36 && r[6] == 2);
    }

    // --- Разбор сводки ---

    [Fact]
    public void The_summary_fills_both_packs()
    {
        var harness = P6();
        bool decoded = harness.Decoder.ProtocolDecoder.Decode(SummaryFrame());

        Assert.True(decoded);
        var snapshot = harness.Snapshot();
        Assert.Equal(230.04, snapshot.Bms1.Voltage, 2);
        Assert.Equal(2.50, snapshot.Bms1.Current, 2);
        Assert.Equal(229.00, snapshot.Bms2.Voltage, 2);
        Assert.Equal(2.40, snapshot.Bms2.Current, 2);
    }

    /// <summary>Заряд по пакам приходит не из BMS, а из кадра телеметрии — и это единственное во
    /// всём разделе, что подтверждено нашими дампами (§8.4).</summary>
    [Fact]
    public void The_per_pack_charge_comes_from_the_telemetry_frame()
    {
        var harness = P6();
        harness.Decoder.ProtocolDecoder.Decode(Convert.FromHexString(P6RealTimeStanding));

        // Разбаланс паков виден и в целых процентах: общий заряд колеса при этом 98 %.
        var snapshot = harness.Snapshot();
        Assert.Equal(99, snapshot.Bms1.RemPerc);
        Assert.Equal(97, snapshot.Bms2.RemPerc);
        Assert.Equal(98, snapshot.Battery);
    }

    /// <summary>Необнаруженный пак — пустое место, а не нули: раздел на экране показывается по
    /// напряжению, и ноль в нём читался бы как «батарея села».</summary>
    [Fact]
    public void An_undetected_pack_stays_empty()
    {
        var harness = P6();
        byte[] data = SummaryData();
        data[14] = 0x00; // флаги второго пака: обнаружения нет
        data[15] = 0x00;

        harness.Decoder.ProtocolDecoder.Decode(Frame(0x14, 0x05, data));

        var snapshot = harness.Snapshot();
        Assert.Equal(230.04, snapshot.Bms1.Voltage, 2);
        Assert.Equal(0.0, snapshot.Bms2.Voltage);
    }

    /// <summary>Короткая сводка — тишина без падения: слот пака восемь байт, слотов два, меньше
    /// шестнадцати байт читать нечего.</summary>
    [Fact]
    public void A_short_summary_says_nothing()
    {
        var harness = P6();
        bool decoded = harness.Decoder.ProtocolDecoder.Decode(Frame(0x14, 0x05, [.. SummaryData()[..8]]));

        Assert.False(decoded);
        Assert.Equal(0.0, harness.Snapshot().Bms1.Voltage);
    }

    /// <summary>Сводка у модели из таблицы оригинала не разбирается — как и у самого оригинала, где
    /// её сохраняет только carType 131.</summary>
    [Fact]
    public void The_summary_is_dropped_for_every_model_but_the_p6()
    {
        var harness = DecoderHarness.ForInMotionV2_1();
        harness.Decoder.ProtocolDecoder.Decode(Convert.FromHexString(CarTypeV11));
        harness.Decoder.ProtocolDecoder.Decode(SummaryFrame());

        Assert.Equal(0.0, harness.Snapshot().Bms1.Voltage);
    }

    // --- Разбор адресных ответов ---

    [Fact]
    public void The_realtime_answer_brings_temperatures_and_cell_bounds()
    {
        var harness = P6();
        bool decoded = harness.Decoder.ProtocolDecoder.Decode(RealtimeFrame(36));

        Assert.True(decoded);
        var pack = harness.Snapshot().Bms1;
        Assert.Equal(230.04, pack.Voltage, 2);
        Assert.Equal(2.50, pack.Current, 2);
        Assert.Equal(4.110, pack.MaxCell, 3);
        Assert.Equal(4.050, pack.MinCell, 3);
        Assert.Equal(0.060, pack.CellDiff, 3);
        Assert.Equal(42, pack.FullCycles);
        Assert.Equal(32.0, pack.Temp1);
        Assert.Equal(34.0, pack.Temp2);
        // Третий байт хвоста — вне окна правдоподобия: место датчика остаётся пустым, а не занятым
        // выдуманным числом. Пустоту дальше отбрасывает и запись поездки.
        Assert.Equal(0.0, pack.Temp3);
    }

    [Fact]
    public void The_cell_answer_brings_the_cells_themselves()
    {
        var harness = P6();
        bool decoded = harness.Decoder.ProtocolDecoder.Decode(CellsFrame(36, 4110, 4100, 4090, 4080));

        Assert.True(decoded);
        var pack = harness.Snapshot().Bms1;
        Assert.Equal(4, pack.CellCount);
        Assert.Equal(4.110, pack.Cells[0], 3);
        Assert.Equal(4.080, pack.Cells[3], 3);
        Assert.Equal(0.0, pack.Cells[4]);
        Assert.Equal(4.110, pack.MaxCell, 3);
        Assert.Equal(4.080, pack.MinCell, 3);
        Assert.Equal(4.095, pack.AvgCell, 3);
        Assert.Equal(1, pack.MaxCellNum);
        Assert.Equal(4, pack.MinCellNum);
    }

    /// <summary>
    /// Всё-или-ничего: одна банка вне окна 2…5 В отбрасывает весь список. Явной нумерации банок в
    /// ответе нет, проверить смещение больше нечем — а половина списка со сдвигом выглядит как
    /// разбаланс, которого нет.
    /// </summary>
    [Fact]
    public void One_impossible_cell_drops_the_whole_list()
    {
        var harness = P6();
        bool decoded = harness.Decoder.ProtocolDecoder.Decode(CellsFrame(36, 4110, 4100, 6000, 4080));

        Assert.False(decoded);
        var pack = harness.Snapshot().Bms1;
        Assert.Equal(0, pack.CellCount);
        Assert.Equal(0.0, pack.Cells[0]);
    }

    /// <summary>Напряжение пака вне окна 50…300 В — ответ не наш или раскладка иная: тишина.</summary>
    [Fact]
    public void An_impossible_pack_voltage_drops_the_answer()
    {
        var harness = P6();
        byte[] data = RealtimeData();
        Put16(data, 6, 4200); // 42 В — ниже окна правдоподобия
        bool decoded = harness.Decoder.ProtocolDecoder.Decode(Frame(0x16, 36, [2, 1, .. data]));

        Assert.False(decoded);
        Assert.Equal(0.0, harness.Snapshot().Bms1.Voltage);
    }

    /// <summary>Короткий адресный ответ — тишина: раскладка реального времени начинается с 28 байт,
    /// и читать её из двадцати значит читать чужое.</summary>
    [Fact]
    public void A_short_direct_answer_says_nothing()
    {
        var harness = P6();
        bool decoded = harness.Decoder.ProtocolDecoder.Decode(Frame(0x16, 36, [2, 1, .. RealtimeData()[..20]]));

        Assert.False(decoded);
        Assert.Equal(0.0, harness.Snapshot().Bms1.Voltage);
    }

    /// <summary>Пак определяется позиционно по возрастанию адреса платы — так же, как в оригинале, и
    /// это его догадка, а не поле протокола: явного номера пака в ответе нет.</summary>
    [Fact]
    public void Boards_map_onto_packs_by_ascending_address()
    {
        var harness = P6();
        harness.Decoder.ProtocolDecoder.Decode(CellsFrame(36, 4110, 4100));
        harness.Decoder.ProtocolDecoder.Decode(CellsFrame(37, 3900, 3890));

        var snapshot = harness.Snapshot();
        Assert.Equal(4.110, snapshot.Bms1.MaxCell, 3);
        Assert.Equal(3.900, snapshot.Bms2.MaxCell, 3);
    }

    /// <summary>Адресный ответ не от P6 не разбирается вовсе: этих запросов мы никому, кроме P6, не
    /// шлём, и разбирать чужой конверт по догадке нечего.</summary>
    [Fact]
    public void A_direct_answer_to_a_non_p6_is_dropped()
    {
        var harness = DecoderHarness.ForInMotionV2_1();
        harness.Decoder.ProtocolDecoder.Decode(Convert.FromHexString(CarTypeV11));
        bool decoded = harness.Decoder.ProtocolDecoder.Decode(CellsFrame(36, 4110, 4100));

        Assert.False(decoded);
        Assert.Equal(0.0, harness.Snapshot().Bms1.MaxCell);
    }

    /// <summary>Ответ не нам (адрес получателя не 2) и селектор, которого никто не разобрал, —
    /// оба молча мимо.</summary>
    [Theory]
    [InlineData(3, 2)]
    [InlineData(2, 4)]
    public void An_answer_addressed_elsewhere_is_dropped(byte target, byte selector)
    {
        var harness = P6();
        byte[] cells = [.. Bytes16(4110, 4100)];
        bool decoded = harness.Decoder.ProtocolDecoder.Decode(Frame(0x16, 36, [target, selector, .. cells]));

        Assert.False(decoded);
        Assert.Equal(0.0, harness.Snapshot().Bms1.MaxCell);
    }

    // --- Сборка кадров и прогоны ---

    private static bool IsTelemetry(byte[] request) =>
        request.SequenceEqual(InMotionV2Message.GetRealTimeData().WriteBuffer());

    private static bool IsOdometer(byte[] request) =>
        request.SequenceEqual(InMotionV2Message.GetStatistics().WriteBuffer());

    private static bool IsSummary(byte[] request) =>
        request.SequenceEqual(Convert.FromHexString(SummaryRequest));

    private static bool IsDirect(byte[] request) => request.Length == 8 && request[2] == 0x16;

    private static DecoderHarness P6()
    {
        var harness = DecoderHarness.ForInMotionV2_1();
        harness.Decoder.ProtocolDecoder.Decode(Convert.FromHexString(CarTypeP6));
        return harness;
    }

    private static List<byte[]> P6Requests(TimeSpan span, bool answering = true, bool answerBms = false) =>
        Requests(span, CarTypeP6, answering, answerBms);

    /// <summary>
    /// Прогон по виртуальному времени. Колесо отвечает по делу: спросили тип — назвало тип,
    /// серийник — серийник, BMS — сводку или адресный ответ (если <paramref name="answerBms"/>), на
    /// прочее шлёт телеметрию.
    /// </summary>
    private static List<byte[]> Requests(TimeSpan span, string carType, bool answering = true, bool answerBms = false)
    {
        var harness = DecoderHarness.ForInMotionV2_1();
        var decoder = harness.Decoder.ProtocolDecoder;
        var asked = new List<byte[]>();

        decoder.WriteRequested += request =>
        {
            asked.Add(request);
            if (!answering) return;

            byte[]? answer = AnswerTo(request, carType, answerBms);
            if (answer is not null) decoder.Decode(answer);
        };

        harness.Time.Advance(span);
        return asked;
    }

    private static byte[]? AnswerTo(byte[] request, string carType, bool answerBms)
    {
        if (request.SequenceEqual(InMotionV2Message.GetCarType().WriteBuffer())) return Convert.FromHexString(carType);
        if (request.SequenceEqual(InMotionV2Message.GetSerialNumber().WriteBuffer())) return Convert.FromHexString(SerialNumber);
        if (IsSummary(request)) return answerBms ? SummaryFrame() : null;
        if (IsDirect(request)) return answerBms ? DirectAnswer(request[5], request[6]) : null;
        return Convert.FromHexString(P6RealTimeStanding);
    }

    private static byte[] DirectAnswer(byte address, byte selector) =>
        selector == 2 ? CellsFrame(address, 4110, 4100) : RealtimeFrame(address);

    /// <summary>Сводка: два слота по восемь байт — напряжение, ток заряда, ток разряда, флаги.</summary>
    private static byte[] SummaryData()
    {
        var data = new byte[16];
        Put16(data, 0, 23004);  // 230,04 В
        Put16(data, 2, 0);      // заряд не идёт
        Put16(data, 4, 250);    // разряд 2,50 А
        Put16(data, 6, 0x0003); // обнаружен, включён
        Put16(data, 8, 22900);
        Put16(data, 10, 0);
        Put16(data, 12, 240);
        Put16(data, 14, 0x0003);
        return data;
    }

    private static byte[] SummaryFrame() => Frame(0x14, 0x05, SummaryData());

    private static byte[] RealtimeData()
    {
        var data = new byte[31];
        Put16(data, 6, 23004);  // напряжение пака, 230,04 В
        Put16(data, 8, 0);      // ток заряда
        Put16(data, 10, 250);   // ток разряда, 2,50 А
        Put16(data, 12, 20000); // ёмкость, мА·ч
        Put16(data, 14, 15000);
        Put16(data, 16, 42);    // циклов
        Put16(data, 18, 4110);  // максимальная банка
        Put16(data, 20, 4050);  // минимальная
        data[28] = 0xD0;        // −48 + 80 = 32 °C
        data[29] = 0xD2;        // 34 °C
        data[30] = 0x80;        // −128 + 80 = −48 °C, вне окна: место датчика пустое
        return data;
    }

    private static byte[] RealtimeFrame(byte address) => Frame(0x16, address, [2, 1, .. RealtimeData()]);

    private static byte[] CellsFrame(byte address, params int[] cells) =>
        Frame(0x16, address, [2, 2, .. Bytes16(cells)]);

    private static byte[] Bytes16(params int[] values)
    {
        var data = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) Put16(data, i * 2, values[i]);
        return data;
    }

    private static void Put16(byte[] data, int at, int value)
    {
        data[at] = (byte)value;
        data[at + 1] = (byte)(value >> 8);
    }

    /// <summary>Кадр как он приходит с провода: заголовок, конверт, длина, первый байт тела
    /// (подкоманда либо адрес платы), данные, байт XOR — с экранированием, но не самой суммы.</summary>
    private static byte[] Frame(byte envelope, byte head, byte[] data)
    {
        byte[] body = [envelope, (byte)(data.Length + 1), head, .. data];

        byte check = 0;
        foreach (byte b in body) check ^= b;

        List<byte> wire = [0xAA, 0xAA];
        foreach (byte b in body)
        {
            if (b is 0xAA or 0xA5) wire.Add(0xA5);
            wire.Add(b);
        }
        wire.Add(check);
        return [.. wire];
    }
}
