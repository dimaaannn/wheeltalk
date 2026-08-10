using WheelTalk.Core.Metrics;

namespace WheelTalk.Tests.Metrics;

/// <summary>
/// Размерности величин экрана «Цифры»: умолчание типа величины и своё число плитки поверх него
/// (решение владельца 10.08.2026).
/// <para>
/// <b>Зачем замок на таблицу умолчаний.</b> Знаки после запятой владелец назначал поимённо, и
/// сдвинуть их «заодно» — правка вида на всех плитках сразу: худшая строка класса становится уже
/// или шире, а с ней меняется кегль всего класса. Здесь заперты не все двадцать шесть чисел, а
/// названные владельцем и правила, по которым назначено остальное.
/// </para>
/// </summary>
public class MetricRoundingTests
{
    private static int Decimals(string id) =>
        MetricCatalogue.Find(id)?.Decimals ?? throw new InvalidOperationException($"нет величины «{id}»");

    /// <summary>
    /// Названо владельцем поимённо: ШИМ — целыми, скорость и напряжение пакета — десятыми, вольт на
    /// банку — сотыми. Сотые в каталоге остались одной этой величине.
    /// </summary>
    [Theory]
    [InlineData("pwm", 0)]
    [InlineData("speed", 1)]
    [InlineData("voltage", 1)]
    [InlineData("cell_voltage", 2)]
    public void The_owner_named_these_himself(string id, int decimals) => Assert.Equal(decimals, Decimals(id));

    /// <summary>
    /// Производная величина показывается так же, как основная: пара, которую читают рядом
    /// («ШИМ» и «Пик ШИМ», «Скорость» и «Предел»), не вправе разойтись видом.
    /// </summary>
    [Theory]
    [InlineData("max_pwm", "pwm")]
    [InlineData("hw_pwm", "pwm")]
    [InlineData("top_speed", "speed")]
    [InlineData("speed_limit", "speed")]
    [InlineData("current_limit", "current")]
    [InlineData("motor_power", "power")]
    [InlineData("temp2", "system_temp")]
    [InlineData("cpu_temp", "system_temp")]
    [InlineData("imu_temp", "system_temp")]
    public void A_derived_metric_reads_like_the_one_it_comes_from(string derived, string source) =>
        Assert.Equal(Decimals(source), Decimals(derived));

    /// <summary>
    /// Что колесо сообщает целым, целым и показывается: дробная часть у процентов заряда и градусов
    /// нарисована нами, а не измерена, — и стоит она разряда ширины на каждой плитке класса.
    /// </summary>
    [Theory]
    [InlineData("battery_level")]
    [InlineData("system_temp")]
    [InlineData("cpu_load")]
    [InlineData("fan_status")]
    [InlineData("sleep_timer")]
    public void What_the_wheel_reports_whole_is_shown_whole(string id) => Assert.Equal(0, Decimals(id));

    /// <summary>
    /// Пробег поездки подробнее одометра, но не бесконечно: сотня метров на плитке видна, десяток
    /// нет (владелец, 10.08.2026), а тысячам километров и десятой не надо.
    /// </summary>
    [Fact]
    public void A_trip_is_finer_than_the_odometer()
    {
        Assert.Equal(1, Decimals("distance"));
        Assert.Equal(1, Decimals("distance_from_start"));
        Assert.Equal(0, Decimals("totaldistance"));
    }

    /// <summary>
    /// Сотые — привилегия одной величины: на банке в целом вольте умещается весь пакет от пустого
    /// до полного. Появится вторая — это решение, а не мелочь, и здесь оно споткнётся.
    /// </summary>
    [Fact]
    public void Hundredths_belong_to_the_cell_alone()
    {
        var finest = MetricCatalogue.All.Where(metric => metric.Decimals >= 2).Select(metric => metric.Id);

        Assert.Equal(["cell_voltage"], finest);
    }

    /// <summary>Своё число плитки старше умолчания величины — ради него всё и заведено.</summary>
    [Fact]
    public void The_tile_has_the_last_word()
    {
        var speed = MetricCatalogue.Find("speed")!;

        Assert.Equal(1, MetricRounding.Decimals(speed, null));
        Assert.Equal(0, MetricRounding.Decimals(speed, 0));
        Assert.Equal(2, MetricRounding.Decimals(speed, 2));
        Assert.Equal("F0", MetricRounding.Format(speed, 0));
        Assert.Equal("F1", MetricRounding.Format(speed, null));
    }

    /// <summary>
    /// Число вне предложенного — не мусор, а чужая новизна: раскладка, собранная версией, которая
    /// предлагает больше знаков, читается здесь как «по умолчанию», и плитка от этого не пропадает.
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(3, null)]
    [InlineData(-1, null)]
    public void An_unknown_number_of_digits_means_the_default(int? saved, int? expected) =>
        Assert.Equal(expected, MetricRounding.Chosen(saved));
}
