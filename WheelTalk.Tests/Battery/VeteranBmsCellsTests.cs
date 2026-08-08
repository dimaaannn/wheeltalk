using System.IO.Hashing;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Battery;

/// <summary>
/// Живучесть разбора страницы банок Ветерана при любом ряде, какой позволяют настройки. Кадр собран,
/// а не взят из фикстур: нужен ряд заведомо известный и заведомо больший массива банок, а в записях
/// владельца такого колеса нет.
/// <para>
/// Счёта банок по этим кадрам здесь нет и не будет: ступень выведенного счёта по BMS убрана
/// (план 27 §27.4, решение владельца 08.08.2026) — хвостовые места пакета держат значения
/// <b>внутри</b> облака живых банок, и отделить их от настоящих нечем. Ряд Ветерана называет версия
/// протокола.
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

        harness.Decoder.Feed(LastBmsPage(LiveCell));

        var bms = harness.Snapshot().Bms1;

        // Считаем ровно по тем банкам, что есть: среднее, поделённое на число, которого не считали,
        // врало бы в ту же сторону, что и вылет за массив.
        Assert.Equal(cellsInSeries == 0 ? 36 : Math.Min(cellsInSeries, bms.Cells.Length), bms.CellCount);
    }

    /// <summary>
    /// Кадр Ветерана со страницей банок 3 — той, на которой декодер досчитывает агрегаты и потому
    /// ходит по массиву банок до самого ряда. Раскладка — та же, что читает <c>VeteranUnpacker</c>:
    /// заголовок, длина, тело, CRC32.
    /// </summary>
    private static byte[] LastBmsPage(double cellVolts)
    {
        byte[] frame = new byte[Len + 4];
        frame[0] = 0xDC;
        frame[1] = 0x5A;
        frame[2] = 0x5C;
        frame[3] = Len;

        // Версия протокола 5: ниже пятой Ветеран страниц BMS не шлёт вовсе. Она же даёт ряд 36,
        // которым отвечает ступень протокола, когда ряд не задан человеком.
        frame[28] = 0x13;
        frame[29] = 0x88;

        frame[46] = 3;

        // Банки лежат парами байт, милливольтами. Страница 3 читает их с 59-го байта: до него идут
        // шесть температур.
        int millivolts = (int)Math.Round(cellVolts * 1000);
        for (int i = 0; i < 12; i++)
        {
            int offset = 59 + i * 2;
            if (offset + 1 >= Len) break;

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
