using System.IO.Hashing;
using WheelTalk.Core.Battery;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Battery;

/// <summary>
/// Кадры банок Ветерана — и то, что из них берёт шкала. Кадры собраны, а не взяты из фикстур: в
/// записях владельца страниц BMS нет, а именно они ведут и в цикл по банкам, и в среднее.
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

        harness.Decoder.Feed(BmsPage(3, LiveCell));

        var bms = harness.Snapshot().Bms1;

        // Считаем ровно по тем банкам, что есть: среднее, поделённое на число, которого не считали,
        // врало бы в ту же сторону, что и вылет за массив.
        Assert.Equal(cellsInSeries == 0 ? 36 : Math.Min(cellsInSeries, bms.Cells.Length), bms.CellCount);
    }

    /// <summary>
    /// Последний блок пришёл первым — обычное дело после подключения. Пока двух третей банок нет,
    /// среднее по <b>живым</b> местам остаётся честным: нули в него не входят.
    /// <para>
    /// Без отбора это была бы вспышка полностью красной шкалы при каждом подключении Ветерана:
    /// сумма по всем 56 местам при заполненных двенадцати дала бы около 0,8 В на банку — ниже всех
    /// зон разом.
    /// </para>
    /// </summary>
    [Fact]
    public void The_last_page_arriving_first_does_not_drag_the_average_down()
    {
        var harness = DecoderHarness.ForVeteran();

        harness.Decoder.Feed(BmsPage(3, LiveCell));

        var snapshot = harness.Snapshot();
        Assert.Equal(LiveCell, BmsCells.Average(snapshot.Bms1, snapshot.Bms2), 3);

        // Остальные блоки ничего не меняют: банки те же самые, среднее то же самое.
        harness.Decoder.Feed(BmsPage(1, LiveCell));
        harness.Decoder.Feed(BmsPage(2, LiveCell));

        snapshot = harness.Snapshot();
        Assert.Equal(LiveCell, BmsCells.Average(snapshot.Bms1, snapshot.Bms2), 3);
    }

    /// <summary>До первого кадра с банками среднего нет — и шкала вернётся к вольтам пакета.</summary>
    [Fact]
    public void Without_a_single_cell_page_there_is_no_average()
    {
        var harness = DecoderHarness.ForVeteran();
        var snapshot = harness.Snapshot();

        Assert.Equal(0, BmsCells.Average(snapshot.Bms1, snapshot.Bms2));
    }

    /// <summary>
    /// Кадр Ветерана с одной страницей банок. Раскладка — та же, что читает <c>VeteranUnpacker</c>:
    /// заголовок, длина, тело, CRC32.
    /// </summary>
    private static byte[] BmsPage(int pnum, double cellVolts)
    {
        byte[] frame = new byte[Len + 4];
        frame[0] = 0xDC;
        frame[1] = 0x5A;
        frame[2] = 0x5C;
        frame[3] = Len;

        // Версия протокола 5: ниже пятой Ветеран страниц BMS не шлёт вовсе. Она же даёт ряд 36,
        // которым считается случай «ряд не задан».
        frame[28] = 0x13;
        frame[29] = 0x88;

        frame[46] = (byte)pnum;

        // Банки лежат парами байт, милливольтами. Страницы 1 и 2 читают их с 53-го байта, страница
        // 3 — с 59-го: до него идут шесть температур.
        int from = pnum == 3 ? 59 : 53;
        int cells = pnum == 3 ? 12 : 15;
        for (int i = 0; i < cells; i++)
        {
            int offset = from + i * 2;
            if (offset + 1 >= Len) break;

            int millivolts = (int)Math.Round(cellVolts * 1000);
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
