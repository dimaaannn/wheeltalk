using WheelTalk.Core.Contracts;

namespace WheelTalk.Lab.Droid.Scenarios;

/// <summary>
/// Придуманные сценарии — не замена записям, а то, чего в записях нет. В нарезке есть спокойная
/// езда до 59 % и мгновенный раскрут в воздухе до 110 %, а между ними — ничего: плавного подхода
/// к 95 % ни одна запись не содержит, потому что на колесе так ездить незачем. При этом именно
/// подход к пределу панель и должна показывать лучше всего.
/// <para>
/// Перенесено из <c>WheelTalk.Lab/Scenarios/SyntheticTimeline.cs</c> без изменений.
/// </para>
/// </summary>
public static class SyntheticTimeline
{
    private const double Hz = 5;

    public static Timeline Approach() => Build(
        "Подход к пределу",
        "ШИМ ровно ползёт 50 → 105 за полминуты: главный случай, которого нет ни в одной записи",
        TimeSpan.FromSeconds(30),
        t => (20 + t * 1.2, 50 + t * 55 / 30.0));

    public static Timeline Step() => Build(
        "Ступенька ШИМ",
        "60 %, скачок до 96 на три секунды и обратно — проверка того, что сглаживание не съедает пик",
        TimeSpan.FromSeconds(24),
        t => (32, t % 12 < 8 ? 60 : 96));

    public static Timeline Jitter() => Build(
        "Дрожание на 84",
        "ШИМ болтается вокруг 84 на пару процентов — сколько мельтешения даёт вариант в самой неприятной точке",
        TimeSpan.FromSeconds(30),
        t => (38, 84 + 2.5 * Math.Sin(t * 9.1) + 1.5 * Math.Sin(t * 27.7)));

    /// <summary>
    /// Разгоны вперемежку с накатом. Единственный сценарий, где просадку видно как явление: под
    /// нагрузкой напряжение приседает, на накате возвращается — и именно возврат даёт опору, без
    /// которой глубину просадки не посчитать. В записях с MTen3 на спокойной езде это доли вольта,
    /// то есть приём есть, а увидеть его не на чем.
    /// </summary>
    public static Timeline Sag() => Build(
        "Просадка под нагрузкой",
        "Рывки и накат по очереди: напряжение приседает под тягой и отпускает на выбеге",
        TimeSpan.FromSeconds(48),
        t => t % 12 < 7 ? (25 + 4 * (t % 12), 55 + 6 * (t % 12)) : (30, 2));

    public static Timeline Sawtooth() => Build(
        "Пила 60…95",
        "Медленные качели через оба порога: видно, где именно вариант меняет цвет и форму",
        TimeSpan.FromSeconds(40),
        t => (30 + 10 * Triangle(t, 10), 60 + 35 * Triangle(t, 10)));

    private static double Triangle(double t, double period)
    {
        double phase = t % period / period;
        return phase < 0.5 ? phase * 2 : 2 - phase * 2;
    }

    private static Timeline Build(string title, string subtitle, TimeSpan duration, Func<double, (double Speed, double Pwm)> shape)
    {
        var frames = new List<TimelineFrame>();
        double maxPwm = 0;
        double topSpeed = 0;
        double distance = 0;

        for (double t = 0; t <= duration.TotalSeconds; t += 1 / Hz)
        {
            (double speed, double pwm) = shape(t);
            maxPwm = Math.Max(maxPwm, pwm);
            topSpeed = Math.Max(topSpeed, speed);
            distance += speed / 3.6 / Hz;

            // Ток лепится из ШИМ, а напряжение проседает от тока. Без тока опора для просадки не
            // работает вовсе: колесо всё время выглядит разгруженным, и просадка выходит нулевой.
            double current = pwm * 0.55;

            frames.Add(new TimelineFrame(TimeSpan.FromSeconds(t), new TelemetrySnapshot
            {
                SpeedRaw = (int)Math.Round(speed * 100),
                Pwm = pwm,
                MaxPwm = maxPwm,
                TopSpeedRaw = (int)Math.Round(topSpeed * 100),
                CurrentRaw = (int)Math.Round(current * 100),
                // Просадка под нагрузкой примерно как у 20S-пака MTen3: цифры должны быть
                // правдоподобными, иначе на панели вылезет несуществующая проблема вёрстки.
                VoltageRaw = (int)Math.Round((80.4 - current * 0.05 - t * 0.01) * 100),
                Battery = (int)Math.Round(Math.Clamp(90 - t * 0.2, 0, 100)),
                TemperatureRaw = 4100,
                WheelDistance = (long)distance,
                Model = "Синтетика",
                WheelType = WheelType.GotWay,
            }));
        }

        return new Timeline(title, subtitle, frames);
    }
}
