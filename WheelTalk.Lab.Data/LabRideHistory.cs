using WheelTalk.Core.Contracts;

namespace WheelTalk.Lab.Data;

/// <summary>
/// Придуманная покатушка длиной в несколько часов — то, чем стенд набивает свою базу, чтобы графики
/// было из чего строить. Нарезка из <c>replay/</c> для этого не годится: там минуты, а график
/// смотрят на часах, и склеенная в кольцо минута дала бы ровный узор с периодом, по которому о виде
/// графика судить нельзя.
/// <para>
/// <b>Считается физикой, а не рисуется кривой.</b> Скорость идёт целями с ограниченным ускорением,
/// мощность складывается из качения, воздуха и разгона, ток берётся из мощности, напряжение
/// проседает на токе и уходит вниз по мере расхода, ШИМ следует за скоростью и напряжением,
/// температуры догоняют нагрузку с запаздыванием. Отсюда и связи между графиками: просадка
/// напряжения совпадает с пиком тока, а не живёт своей жизнью. Нарисованные по отдельности кривые
/// этого не дают — и по ним нельзя увидеть, что график врёт.
/// </para>
/// <para>
/// Колесо — семейства Gotway/Begode на 24S (100.8 В заряженным): тогда температура двигателя есть, а
/// наклон молчит (<see cref="WheelReports"/>), и на экране видны обе стороны правила про прочерк.
/// </para>
/// </summary>
public static class LabRideHistory
{
    /// <summary>
    /// Выдуманный MAC стенда. Поток принадлежит колесу (план 23 §5.1), и у стенда оно одно — но
    /// назваться обязано, иначе строку некуда положить.
    /// </summary>
    public const string Mac = "LA:B0:00:00:00:01";

    public const string Protocol = "Gotway";

    /// <summary>Пять отсчётов в секунду — та же частота, с какой поток пишет приложение.</summary>
    public const double Hz = 5;

    private const int Cells = 24;
    private const double CapacityWh = 1800;
    private const double MassKg = 100;
    private const double AmbientC = 24;

    /// <summary>Внутреннее сопротивление пака: на 40 А даёт около 3.6 В просадки.</summary>
    private const double PackOhm = 0.09;

    /// <summary>Одометр, с которого колесо начинает: график пробега должен расти от чего-то.</summary>
    private const double OdometerStartKm = 4213;

    /// <summary>
    /// Отсчёты за <paramref name="span"/> до <paramref name="endsAt"/>, по порядку времени.
    /// Лениво: их десятки тысяч, и держать их все разом незачем — они уходят в запись по одному.
    /// </summary>
    public static IEnumerable<(DateTimeOffset At, TelemetrySnapshot Snapshot)> Generate(
        DateTimeOffset endsAt, TimeSpan span, int seed)
    {
        var random = new Random(seed);
        double dt = 1 / Hz;
        int steps = (int)(span.TotalSeconds * Hz);
        var startedAt = endsAt - span;

        double speed = 0;             // м/с
        double soc = 0.94;            // доля заряда
        double consumedWh = 0;
        double distanceM = 0;
        double boardC = AmbientC;
        double motorC = AmbientC;
        double topSpeedKmh = 0;
        double maxPwm = 0;

        var phase = Phase.Next(random, 0);

        for (int i = 0; i < steps; i++)
        {
            double t = i * dt;
            if (t >= phase.Until) phase = Phase.Next(random, t);

            // Скорость догоняет цель с человеческим ускорением: мгновенный скачок дал бы пик тока,
            // которого на колесе не бывает.
            double target = phase.TargetMs;
            double limit = target > speed ? 1.8 : 2.6;
            double wanted = Math.Clamp(target - speed, -limit * dt, limit * dt);
            double before = speed;
            speed = Math.Max(0, speed + wanted + (random.NextDouble() - 0.5) * 0.06);

            double accel = (speed - before) / dt;

            double rolling = 0.02 * MassKg * 9.81 * speed;
            double aero = 0.5 * 1.2 * 0.55 * speed * speed * speed;
            double kinetic = MassKg * accel * speed;
            double mechanical = rolling + aero + kinetic;
            // Под тягой платим за КПД, на торможении возвращаем далеко не всё; 25 Вт стоит само
            // колесо, даже когда стоит на месте.
            double watts = (mechanical >= 0 ? mechanical / 0.85 : mechanical * 0.55) + 25;

            double cellRest = 3.30 + 0.90 * Math.Pow(Math.Clamp(soc, 0, 1), 0.7);
            double restV = cellRest * Cells;
            // Ток от напряжения, напряжение от тока: одного уточнения хватает, чтобы просадка сошлась.
            double amps = watts / restV;
            double volts = restV - amps * PackOhm;
            amps = watts / Math.Max(volts, 1);
            volts = restV - amps * PackOhm;

            consumedWh += watts * dt / 3600;
            soc = Math.Clamp(0.94 - consumedWh / CapacityWh, 0, 1);

            double speedKmh = speed * 3.6;
            // ШИМ — это доля напряжения, которую мотор просит на такой скорости, плюс небольшая
            // добавка на тягу. Отсюда и то, что на севшем паке та же скорость стоит дороже.
            double pwm = Math.Clamp(speedKmh / (volts * 0.70) * 100 + amps * 0.05, 0, 115);
            double phaseAmps = amps / Math.Max(pwm / 100, 0.05);

            // Температуры догоняют нагрузку с запаздыванием: плата быстрее, двигатель медленнее и
            // выше. Без запаздывания график температуры повторял бы график мощности.
            boardC += (AmbientC + watts / 40 - boardC) / 180 * dt;
            motorC += (AmbientC + watts / 22 - motorC) / 300 * dt;

            distanceM += speed * dt;
            topSpeedKmh = Math.Max(topSpeedKmh, speedKmh);
            maxPwm = Math.Max(maxPwm, pwm);

            yield return (startedAt.AddSeconds(t), new TelemetrySnapshot
            {
                SpeedRaw = Round(speedKmh * 100),
                VoltageRaw = Round(volts * 100),
                CurrentRaw = Round(amps * 100),
                PhaseCurrentRaw = Round(phaseAmps * 100),
                PowerRaw = Round(watts * 100),
                Pwm = pwm,
                MaxPwm = maxPwm,
                TopSpeedRaw = Round(topSpeedKmh * 100),
                // Проценты колесо считает по напряжению под нагрузкой — потому они и приседают на
                // разгоне, а не ползут ровно вниз.
                Battery = Math.Clamp(Round((volts / Cells - 3.3) / 0.9 * 100), 0, 100),
                TemperatureRaw = Round(boardC * 100),
                Temperature2Raw = Round(motorC * 100),
                WheelDistance = (long)distanceM,
                DistanceFromStart = (long)distanceM,
                TotalDistance = (long)(OdometerStartKm * 1000 + distanceM),
                Model = "Стенд · история",
                WheelType = WheelType.GotWay,
            });
        }
    }

    private static int Round(double value) => (int)Math.Round(value);

    /// <summary>
    /// Кусок покатушки с одной целью по скорости. Из них и складывается правдоподобие: ровный ход,
    /// светофоры, рывки и долгие стоянки идут вперемежку, а не по расписанию.
    /// </summary>
    private readonly record struct Phase(double TargetMs, double Until)
    {
        public static Phase Next(Random random, double now)
        {
            double roll = random.NextDouble();
            return roll switch
            {
                // Стоянка: постоял у магазина, поговорил, подождал на переходе.
                < 0.12 => new Phase(0, now + 20 + random.NextDouble() * 220),
                // Рывок: обгон или подъём — здесь и живут пики тока и ШИМ.
                < 0.28 => new Phase(Kmh(34 + random.NextDouble() * 12), now + 8 + random.NextDouble() * 14),
                // Медленный ход: двор, тротуар, разбитый асфальт.
                < 0.45 => new Phase(Kmh(8 + random.NextDouble() * 7), now + 25 + random.NextDouble() * 60),
                // Ровный ход — то, чем покатушка занята большую часть времени.
                _ => new Phase(Kmh(20 + random.NextDouble() * 12), now + 40 + random.NextDouble() * 150),
            };
        }

        private static double Kmh(double kmh) => kmh / 3.6;
    }
}
