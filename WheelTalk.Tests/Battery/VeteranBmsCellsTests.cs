using System.IO.Hashing;
using WheelTalk.Core.Battery;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Battery;

/// <summary>
/// Кадры банок Ветерана: счёт банок по ним и живучесть самого разбора. Кадры собраны, а не взяты из
/// фикстур — в записях владельца страниц BMS хватает, но нужен ряд заведомо известный, чтобы
/// проверялся счёт, а не совпадение.
/// <para>
/// BMS шлёт банки тремя блоками: <c>pnum</c> 1/5 — банки 0–14, 2/6 — 15–29, 3/7 — 30–41 (и там же
/// декодер досчитывает агрегаты). Порядок прихода блоков не гарантирован ничем.
/// </para>
/// </summary>
public class VeteranBmsCellsTests
{
    /// <summary>Длина кадра. Больше 80: страница банок читает байты вплоть до 82-го.</summary>
    private const int Len = 84;

    private const double LiveCell = 3.9;

    /// <summary>Ряд, который отдаёт версия протокола 5 — ступень под BMS, ею и проверяется молчание.</summary>
    private const int ProtocolSeries = 36;

    /// <summary>
    /// Путь сюда открыл §27.4: до него ряд приходил только от версии протокола — максимум 42, — а
    /// массив банок у <c>SmartBms</c> на 56 мест. Ряд 60 законен и есть в списке правдоподобных, и
    /// без ограничения цикл вышел бы за массив. Такое падение не видно в журналах: приложение
    /// просто исчезает — посреди поездки, на кадре BMS.
    /// </summary>
    [Theory]
    [InlineData(0)]    // ряд не задан — считает версия протокола
    [InlineData(36)]   // обычный Sherman L
    [InlineData(56)]   // ровно длина массива
    [InlineData(60)]   // больше массива: тот самый случай
    public void A_bms_frame_survives_any_series_the_settings_allow(int cellsInSeries)
    {
        var harness = DecoderHarness.ForVeteran(config => config.CellsInSeries = cellsInSeries);

        harness.Decoder.Feed(BmsPage(3, LiveCell, packVolts: 0));

        var bms = harness.Snapshot().Bms1;

        // Считаем ровно по тем банкам, что есть: среднее, поделённое на число, которого не считали,
        // врало бы в ту же сторону, что и вылет за массив.
        Assert.Equal(cellsInSeries == 0 ? 36 : Math.Min(cellsInSeries, bms.Cells.Length), bms.CellCount);
    }

    /// <summary>
    /// Счёт по BMS сошёлся с напряжением — каскад берёт его, и с источником <c>SmartBms</c>: это
    /// измерение, а не догадка, и оно выше знания протокола.
    /// </summary>
    [Fact]
    public void A_series_that_adds_up_to_the_pack_voltage_answers_as_smart_bms()
    {
        // Тридцать банок по 3,9 В — это 117 В, и ряд заведомо не тот, что даст версия протокола.
        var harness = FedWithCells(cells: 30, packVolts: 30 * LiveCell);

        Assert.Equal(new CellCount(30, CellCountSource.SmartBms), harness.Snapshot().AutoPackCells);
    }

    /// <summary>
    /// Сумма встала между двумя рядами — ответ неоднозначен, и ступень молчит. Так выглядит кадр под
    /// нагрузкой: пакет просел, и сумма банок не сходится ни с одним рядом. Отвечает протокол, а
    /// человек нажмёт кнопку ещё раз.
    /// </summary>
    [Fact]
    public void A_sum_that_lands_between_two_series_stays_silent()
    {
        // Полбанки мимо: ни 30, ни 31 не годятся, и выбрать между ними не из чего.
        var harness = FedWithCells(cells: 30, packVolts: 30 * LiveCell + LiveCell / 2);

        Assert.Equal(new CellCount(ProtocolSeries, CellCountSource.Protocol), harness.Snapshot().AutoPackCells);
    }

    /// <summary>Напряжение вовсе не из этого пакета — сходиться нечему, и ступень молчит.</summary>
    [Fact]
    public void A_sum_that_matches_nothing_stays_silent()
    {
        var harness = FedWithCells(cells: 30, packVolts: 200);

        Assert.Equal(new CellCount(ProtocolSeries, CellCountSource.Protocol), harness.Snapshot().AutoPackCells);
    }

    /// <summary>
    /// Хвостовые места заполняются байтами кадра, и 2,5 В там от настоящей банки не отличить ничем —
    /// диапазон такое пропустит. Отсекает его сверка суммы: с лишней «банкой» сумма уходит от
    /// напряжения дальше, чем без неё.
    /// </summary>
    [Fact]
    public void Rubbish_past_the_last_cell_does_not_inflate_the_count()
    {
        var harness = FedWithCells(cells: 30, packVolts: 30 * LiveCell, tail: 2.5);

        Assert.Equal(new CellCount(30, CellCountSource.SmartBms), harness.Snapshot().AutoPackCells);
    }

    /// <summary>Пришёл не весь пакет — считать нечего: ряд подряд от начала короче настоящего.</summary>
    [Fact]
    public void A_pack_whose_pages_have_not_all_arrived_is_skipped()
    {
        var harness = DecoderHarness.ForVeteran();

        // Только средняя страница: банки 15–29 заполнены, 0–14 ещё нули.
        harness.Decoder.Feed(BmsPage(2, LiveCell, packVolts: 30 * LiveCell));

        Assert.Equal(new CellCount(ProtocolSeries, CellCountSource.Protocol), harness.Snapshot().AutoPackCells);
    }

    /// <summary>Живая фикстура Sherman L: 36 банок, и счёт по BMS обязан назвать именно их.</summary>
    [Fact]
    public void The_recorded_sherman_l_ride_counts_thirty_six()
    {
        var harness = DecoderHarness.ForVeteran();
        var counted = new HashSet<CellCount>();

        foreach (string line in File.ReadLines(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "shermanl_raw_ride_20260728.csv")))
        {
            int comma = line.IndexOf(',');
            if (comma < 0) continue;

            harness.Decoder.Feed(Convert.FromHexString(line[(comma + 1)..].Trim()));
            var answer = harness.Snapshot().AutoPackCells;
            if (answer.Source == CellCountSource.SmartBms) counted.Add(answer);
        }

        // Ряд один на всю поездку, сколько бы кадров ни ответило: разнобой означал бы, что сверка
        // суммы принимает случайные числа.
        Assert.Equal([new CellCount(36, CellCountSource.SmartBms)], counted);
    }

    /// <summary>Все три страницы одного пакета плюс напряжение, с которым сверяется сумма.</summary>
    private static DecoderHarness FedWithCells(int cells, double packVolts, double tail = 0)
    {
        var harness = DecoderHarness.ForVeteran();

        foreach (int page in (int[])[1, 2, 3])
        {
            harness.Decoder.Feed(BmsPage(page, LiveCell, packVolts, cells, tail));
        }

        return harness;
    }

    /// <summary>
    /// Кадр Ветерана с одной страницей банок. Раскладка — та же, что читает <c>VeteranUnpacker</c>:
    /// заголовок, длина, тело, CRC32.
    /// </summary>
    private static byte[] BmsPage(int pnum, double cellVolts, double packVolts, int cells = 42, double tail = 0)
    {
        byte[] frame = new byte[Len + 4];
        frame[0] = 0xDC;
        frame[1] = 0x5A;
        frame[2] = 0x5C;
        frame[3] = Len;

        // Напряжение пакета — то, с чем сверяется сумма банок.
        int centivolts = (int)Math.Round(packVolts * 100);
        frame[4] = (byte)(centivolts >> 8);
        frame[5] = (byte)centivolts;

        // Версия протокола 5: ниже пятой Ветеран страниц BMS не шлёт вовсе. Она же даёт ряд 36,
        // которым отвечает ступень протокола.
        frame[28] = 0x13;
        frame[29] = 0x88;

        frame[46] = (byte)pnum;

        // Банки лежат парами байт, милливольтами. Страницы 1 и 2 читают их с 53-го байта, страница
        // 3 — с 59-го: до него идут шесть температур.
        int from = pnum == 3 ? 59 : 53;
        int slots = pnum == 3 ? 12 : 15;
        int firstSlot = pnum == 3 ? 30 : (pnum - 1) * 15;

        for (int i = 0; i < slots; i++)
        {
            int offset = from + i * 2;
            if (offset + 1 >= Len) break;

            int slot = firstSlot + i;
            double volts = slot < cells ? cellVolts : slot == cells ? tail : 0;
            if (volts <= 0) continue;

            int millivolts = (int)Math.Round(volts * 1000);
            frame[offset] = (byte)(millivolts >> 8);
            frame[offset + 1] = (byte)millivolts;
        }

        uint crc = Crc32.HashToUInt32(frame.AsSpan(0, Len));
        frame[Len] = (byte)(crc >> 24);
        frame[Len + 1] = (byte)(crc >> 16);
        frame[Len + 2] = (byte)(crc >> 8);
        frame[Len + 3] = (byte)crc;

        return frame;
    }
}
