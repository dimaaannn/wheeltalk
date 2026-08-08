using WheelTalk.Core.Battery;
using WheelTalk.Core.Contracts;

namespace WheelTalk.Tests.Battery;

/// <summary>
/// Отбор банок для среднего (план 27 §27.4). Половины две, и проверяются обе: счёт говорит, докуда
/// смотреть, границы — что внутри этих пределов вообще является показанием банки.
/// </summary>
public class BmsCellsTests
{
    [Fact]
    public void Unfilled_places_inside_the_count_are_not_cells()
    {
        // Пришёл только последний блок: банки есть, остальные места массива — нули.
        var bms = new SmartBms { CellCount = 36 };
        bms.Cells[30] = 3.9;
        bms.Cells[31] = 3.9;

        Assert.Equal(3.9, BmsCells.Average(bms, new SmartBms()), 3);
    }

    /// <summary>
    /// Хвост последнего блока заезжает за конец пакета и приносит байты кадра: у 36-баночного
    /// Sherman L на банке 41 наблюдали 33,365 «вольта». Границы такое отсекают.
    /// </summary>
    [Fact]
    public void A_number_that_cannot_be_a_cell_at_all_is_dropped()
    {
        var bms = Pack(3.9, 3.9, 33.365);

        Assert.Equal(3.9, BmsCells.Average(bms, new SmartBms()), 3);
    }

    /// <summary>
    /// А вот тот же хвост, прочитавшийся как правдоподобные 2,5 В, от настоящей банки не отличить
    /// ничем — кроме счёта. Оттого счёт и оставлен: границы одни его не заменяют.
    /// </summary>
    [Fact]
    public void Rubbish_past_the_count_is_cut_off_by_the_count()
    {
        var bms = Pack(3.9, 3.9);
        bms.Cells[2] = 2.5;   // байт кадра, а не банка: счёт до сюда не доходит

        Assert.Equal(3.9, BmsCells.Average(bms, new SmartBms()), 3);
    }

    /// <summary>
    /// Глубоко разряженная банка — всё ещё банка, и в среднее она входит, даже утягивая его вниз:
    /// это настоящее состояние пакета, а не помеха. Решать, здорова ли банка, не наше дело.
    /// </summary>
    [Fact]
    public void A_deeply_discharged_cell_still_counts()
    {
        var bms = Pack(3.9, 3.9, 1.2);

        Assert.Equal(3.0, BmsCells.Average(bms, new SmartBms()), 3);
    }

    /// <summary>Счёт не может завести за массив: 60S законны, а мест в массиве 56.</summary>
    [Fact]
    public void A_count_beyond_the_array_does_not_reach_past_it()
    {
        var bms = new SmartBms { CellCount = 60 };
        bms.Cells[0] = 3.9;

        Assert.Equal(3.9, BmsCells.Average(bms, new SmartBms()), 3);
    }

    /// <summary>Два пакета Sherman L — один ответ: банки складываются в одно среднее, а не в два.</summary>
    [Fact]
    public void Both_packs_make_one_average()
    {
        Assert.Equal(3.7, BmsCells.Average(Pack(3.6, 3.6), Pack(3.8, 3.8)), 3);
    }

    [Fact]
    public void No_cells_at_all_means_no_answer_rather_than_zero_volts()
    {
        Assert.Equal(0, BmsCells.Average(new SmartBms(), new SmartBms()));
    }

    /// <summary>Gotway называет счёт <c>CellNum</c>, Ветеран — <c>CellCount</c>; берётся то, что заполнено.</summary>
    [Fact]
    public void Either_name_of_the_count_is_understood()
    {
        var gotway = new SmartBms { CellNum = 2 };
        gotway.Cells[0] = 3.9;
        gotway.Cells[1] = 3.9;
        gotway.Cells[2] = 2.5;   // за счётом

        Assert.Equal(3.9, BmsCells.Average(gotway, new SmartBms()), 3);
    }

    private static SmartBms Pack(params double[] cells)
    {
        var bms = new SmartBms { CellCount = cells.Length };
        cells.CopyTo(bms.Cells, 0);
        return bms;
    }
}
