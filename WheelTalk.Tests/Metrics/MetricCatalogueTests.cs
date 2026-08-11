using WheelTalk.Core.Battery;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Metrics;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Metrics;

/// <summary>
/// Каталог величин — таблица, и проверять в нём нечего, кроме двух вещей, которые таблица сломать
/// умеет: <b>молчание колеса не должно выглядеть нулём</b> (план 23 §3.1) и <b>обещанная колонка
/// должна существовать</b> — иначе график шага 6 упадёт на опечатке, которую компилятор не видит.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class MetricCatalogueTests
{
    /// <summary>
    /// Величины, которых это семейство протоколов не сообщает, читаются как «нет значения». Ноль на
    /// их месте был бы показанием, которого колесо не давало, — то же правило, по которому база
    /// пишет туда <c>NULL</c>.
    /// </summary>
    [Theory]
    [InlineData(WheelType.Veteran, "torque")]
    [InlineData(WheelType.Veteran, "roll")]
    [InlineData(WheelType.Veteran, "cpu_load")]
    [InlineData(WheelType.Veteran, "temp2")]
    [InlineData(WheelType.KingSong, "tilt")]
    [InlineData(WheelType.KingSong, "imu_temp")]
    [InlineData(WheelType.GotWay, "sleep_timer")]
    [InlineData(WheelType.Inmotion, "torque")]
    public void A_silent_wheel_has_no_value_at_all(WheelType type, string metric)
    {
        Assert.Null(Read(metric, type));
    }

    /// <summary>Своё каждое семейство отдаёт — включая ровный ноль, который и есть показание.</summary>
    [Theory]
    [InlineData(WheelType.Veteran, "tilt")]
    [InlineData(WheelType.Veteran, "sleep_timer")]
    [InlineData(WheelType.GotWay, "temp2")]
    [InlineData(WheelType.KingSong, "cpu_load")]
    [InlineData(WheelType.InmotionV2, "torque")]
    [InlineData(WheelType.Inmotion, "roll")]
    public void What_the_wheel_does_report_comes_through(WheelType type, string metric)
    {
        Assert.NotNull(Read(metric, type));
    }

    /// <summary>Скорость и напряжение сообщают все — у них семейства нет вовсе.</summary>
    [Fact]
    public void Common_metrics_are_read_from_every_protocol()
    {
        foreach (var type in Enum.GetValues<WheelType>())
        {
            Assert.Equal(10.0, Read("speed", type));
            Assert.Equal(84.5, Read("voltage", type));
        }
    }

    /// <summary>
    /// Вольт на банку считается из ряда ячеек, а не приходит кадром (план 27). Ряда нет — плитка
    /// молчит прочерком: колесо без BMS и без числа в настройках — обычный день, а не поломка.
    /// Неправдоподобное частное молчит тем же прочерком — 4,9 В на банку значат неверный ряд, и
    /// печатать такое райдеру нельзя.
    /// </summary>
    [Fact]
    public void Volts_per_cell_speak_only_when_the_series_is_known()
    {
        var metric = MetricCatalogue.Find("cell_voltage");
        Assert.NotNull(metric);

        // 84,5 В на 24 ячейки — 3,52 В, живая банка.
        Assert.Equal(3.52, metric.Read(Frame(new CellCount(24, CellCountSource.UserSetting))) ?? 0, 2);

        Assert.Null(metric.Read(Frame(CellCount.Unknown)));
        Assert.Null(metric.Read(Frame(new CellCount(16, CellCountSource.VoltageGuess))));
    }

    private static TelemetrySnapshot Frame(CellCount cells) => new()
    {
        WheelType = WheelType.Veteran,
        VoltageRaw = 8450,
        PackCells = cells,
    };

    /// <summary>
    /// Пол — свойство величины, а не графика (владелец 11.08.2026): ШИМ, скорость, напряжение и
    /// подобные меньше нуля не бывают, а ток, мощность, момент и температура — бывают (рекуперация,
    /// мороз, наклон в другую сторону).
    /// </summary>
    [Theory]
    [InlineData("speed")]
    [InlineData("top_speed")]
    [InlineData("speed_limit")]
    [InlineData("pwm")]
    [InlineData("max_pwm")]
    [InlineData("hw_pwm")]
    [InlineData("battery_level")]
    [InlineData("voltage")]
    [InlineData("cell_voltage")]
    [InlineData("distance")]
    [InlineData("distance_from_start")]
    [InlineData("totaldistance")]
    [InlineData("cpu_load")]
    [InlineData("fan_status")]
    [InlineData("sleep_timer")]
    [InlineData("current_limit")]
    public void A_floor_belongs_to_magnitudes_that_cannot_go_below_it(string id)
    {
        var metric = MetricCatalogue.Find(id);
        Assert.NotNull(metric);

        Assert.Equal(0, metric.Floor);
    }

    /// <summary>Ток, мощность, момент, температура и крен уходят в минус законно — пола у них нет.</summary>
    [Theory]
    [InlineData("current")]
    [InlineData("phase_current")]
    [InlineData("power")]
    [InlineData("motor_power")]
    [InlineData("torque")]
    [InlineData("system_temp")]
    [InlineData("temp2")]
    [InlineData("tilt")]
    public void A_floor_is_absent_for_magnitudes_that_may_go_negative(string id)
    {
        var metric = MetricCatalogue.Find(id);
        Assert.NotNull(metric);

        Assert.Null(metric.Floor);
    }

    /// <summary>
    /// Колонка в описании — обещание, что по величине можно построить график (план 23 §3.2). Пустая
    /// колонка — честное «графика нет»; выдуманная — падение запроса на шаге 6.
    /// </summary>
    [Fact]
    public void Every_promised_column_exists_in_the_telemetry_table()
    {
        using var temp = new TempDatabase();
        temp.Open();

        foreach (var metric in MetricCatalogue.All)
        {
            if (metric.Column is not { } column) continue;

            Assert.Equal(1L, temp.Scalar(
                $"SELECT COUNT(*) FROM pragma_table_info('telemetry') WHERE name = '{column}';"));
        }
    }

    private static double? Read(string id, WheelType type)
    {
        var metric = MetricCatalogue.Find(id);
        Assert.NotNull(metric);

        return metric.Read(new TelemetrySnapshot
        {
            WheelType = type,
            SpeedRaw = 1000,
            VoltageRaw = 8450,
        });
    }
}
