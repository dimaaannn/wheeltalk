using WheelTalk.Core.Contracts;
using WheelTalk.Core.Metrics;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Metrics;

/// <summary>
/// Каталог величин — таблица, и проверять в нём нечего, кроме двух вещей, которые таблица сломать
/// умеет: <b>молчание колеса не должно выглядеть нулём</b> (план 23 §3.1) и <b>обещанная колонка
/// должна существовать</b> — иначе график шага 6 упадёт на опечатке, которую компилятор не видит.
/// </summary>
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
