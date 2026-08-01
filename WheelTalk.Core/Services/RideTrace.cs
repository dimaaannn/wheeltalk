using WheelTalk.Core.Contracts;

namespace WheelTalk.Core.Services;

/// <summary>
/// То, что известно про поездку, но чего нет ни в одном отдельном снимке телеметрии: куда ШИМ
/// движется, где он только что был, насколько просел пак под нагрузкой и до чего дошли величины за
/// поездку. Всё это выводится из потока снимков и накапливается кадр за кадром.
/// <para>
/// Живёт в ядре, а не рядом с приборной панелью, по двум причинам. Это факты о поездке, а не о том,
/// как её рисовать: производная ШИМ — единственный способ показать райдеру время до предела, и
/// нужна она будет не только панели. И это арифметика с состоянием, накопленным за час езды, —
/// ровно то, что проверяется тестами, а не глазами на телефоне.
/// </para>
/// <para>
/// Стенд считает то же самое разом по всей записи, потому что запись у него целиком на руках.
/// У живой поездки будущего нет, поэтому здесь кольцевой буфер на несколько последних секунд и
/// набор величин, которые назад не ходят.
/// </para>
/// </summary>
public sealed class RideTrace(TimeProvider timeProvider)
{
    /// <summary>Окно производной. Секунда — столько же, сколько смотрит тревожный оцениватель.</summary>
    public static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(1);

    /// <summary>Окно недавнего пика: «где значение только что было», а не «где было за поездку».</summary>
    public static readonly TimeSpan PeakWindow = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Ток, ниже которого колесо считается разгруженным, ампер. Опора для просадки берётся только
    /// на разгруженном колесе — иначе «просадка» и «разряд за поездку» сложились бы в одну величину.
    /// </summary>
    public const double NoLoadCurrentA = 2;

    /// <summary>
    /// Сколько кадров держать. Пять в секунду на трёхсекундном окне — пятнадцать; сто двадцать
    /// восемь дают запас на колесо, которое говорит чаще, и остаются пустяком по памяти.
    /// </summary>
    private const int History = 128;

    private readonly Frame[] _frames = new Frame[History];

    private int _count;
    private int _next;
    private double _smoothed;

    private double _smoothingSeconds = 0.15;

    /// <summary>
    /// Постоянная времени сглаживания ШИМ, секунд; ноль отдаёт сырое значение. Сглаживание врёт
    /// про скачки, а именно скачок и важен, поэтому это ручка, а не константа. Читается через
    /// <see cref="SmoothingSecondsSource"/>, если он задан (план 19 Б5): экран заводит его один раз
    /// при сборке из настройки панели, и зеркалировать её на каждом кадре больше не нужно — <see cref="Push"/>
    /// сам берёт живое значение. Сеттер по-прежнему пишет обычное поле — тесты, у которых источника
    /// нет, продолжают задавать число им напрямую.
    /// </summary>
    public double SmoothingSeconds
    {
        get => SmoothingSecondsSource is { } source ? source() : _smoothingSeconds;
        set => _smoothingSeconds = value;
    }

    /// <summary>Внешний источник <see cref="SmoothingSeconds"/> — см. её описание.</summary>
    public Func<double>? SmoothingSecondsSource { get; set; }

    public bool HasData => _count > 0;

    /// <summary>Сглаженный ШИМ — то, из чего считается производная и что показывает лента.</summary>
    public double Pwm => _count == 0 ? 0 : Latest.Smoothed;

    /// <summary>Скорость изменения ШИМ, процентов в секунду.</summary>
    public double PwmRate { get; private set; }

    /// <summary>Ускорение, км/ч в секунду.</summary>
    public double SpeedRate { get; private set; }

    /// <summary>Пик ШИМ за последние секунды.</summary>
    public double RecentPwmPeak { get; private set; }

    public double MinVoltageV { get; private set; }
    public double MaxVoltageV { get; private set; }

    /// <summary>
    /// Опорное напряжение без нагрузки: последнее, виденное на околонулевом токе. Просадка — это
    /// разница между ним и текущим, и без опоры её не посчитать: 77 В сами по себе не говорят,
    /// просело колесо или пак просто разряжен.
    /// </summary>
    public double NoLoadVoltageV { get; private set; }

    /// <summary>Самая глубокая просадка за поездку, вольт. Не «сколько сейчас», а «на что способно».</summary>
    public double MaxSagV { get; private set; }

    public int MaxTemperatureC { get; private set; }

    /// <summary>
    /// Новая поездка — новый след. Максимум ШИМ и самая глубокая просадка от прошлой поездки на
    /// шкале этой означали бы то, чего в ней не было.
    /// </summary>
    public void Reset()
    {
        _count = 0;
        _next = 0;
        _smoothed = 0;
        PwmRate = 0;
        SpeedRate = 0;
        RecentPwmPeak = 0;
        MinVoltageV = 0;
        MaxVoltageV = 0;
        NoLoadVoltageV = 0;
        MaxSagV = 0;
        MaxTemperatureC = 0;
    }

    /// <summary>
    /// «Сброс максимумов» с панели — обнуляет только то, что накоплено «с начала поездки», не
    /// живое сглаживание/окна: производная и трёхсекундный пик каждый кадр пересчитываются заново
    /// из кольцевого буфера, а не копятся, так что сбрасывать в них нечего.
    /// </summary>
    public void ResetPeaks()
    {
        MinVoltageV = 0;
        MaxVoltageV = 0;
        NoLoadVoltageV = 0;
        MaxSagV = 0;
        MaxTemperatureC = 0;
    }

    public void Push(TelemetrySnapshot snapshot)
    {
        var at = timeProvider.GetUtcNow();

        // Знаки убраны везде: колесо отдаёт скорость и ШИМ знаковыми, и в записи знак нужен — он
        // показывает рекуперацию. Здесь важна близость к пределу, а 22 % назад так же близко к
        // нему, как вперёд.
        double pwm = Math.Abs(snapshot.Pwm);
        double speed = Math.Abs(snapshot.SpeedKmh);

        // Экспоненциальное сглаживание по времени, а не по кадрам: промежутки неровные, и фильтр
        // «по кадрам» врал бы сильнее всего там, где связь и так хуже всего.
        double seconds = _count == 0 ? 0 : (at - Latest.At).TotalSeconds;
        double alpha = SmoothingSeconds <= 0 || seconds <= 0
            ? 1
            : 1 - Math.Exp(-seconds / SmoothingSeconds);
        _smoothed += (pwm - _smoothed) * alpha;

        _frames[_next] = new Frame(at, pwm, _smoothed, speed);
        _next = (_next + 1) % History;
        if (_count < History) _count++;

        TrackWindows();
        TrackTrip(snapshot);
    }

    private void TrackWindows()
    {
        var now = Latest;
        var back = Before(RateWindow);
        double span = (now.At - back.At).TotalSeconds;

        PwmRate = span <= 0 ? 0 : (now.Smoothed - back.Smoothed) / span;
        SpeedRate = span <= 0 ? 0 : (now.Speed - back.Speed) / span;
        RecentPwmPeak = PeakOver(PeakWindow);
    }

    /// <summary>
    /// Следы поездки. Копятся, а не считаются: минимум напряжения и самая тяжёлая просадка — это
    /// «на что колесо оказалось способно», и назад такие величины не ходят.
    /// </summary>
    private void TrackTrip(TelemetrySnapshot snapshot)
    {
        double volts = snapshot.VoltageV;
        if (volts > 0)
        {
            MinVoltageV = MinVoltageV <= 0 ? volts : Math.Min(MinVoltageV, volts);
            MaxVoltageV = Math.Max(MaxVoltageV, volts);

            // Пока разгруженным колесо ни разу не видели, опорой служит первое же напряжение: до
            // первого разгона оно и есть холостое, а без опоры просадка не считается вовсе.
            if (NoLoadVoltageV <= 0 || Math.Abs(snapshot.CurrentA) < NoLoadCurrentA) NoLoadVoltageV = volts;
            MaxSagV = Math.Max(MaxSagV, NoLoadVoltageV - volts);
        }

        MaxTemperatureC = Math.Max(MaxTemperatureC, snapshot.TemperatureC);
    }

    /// <summary>Куда придёт ШИМ через <paramref name="seconds"/>, если производная сохранится.</summary>
    public double PwmIn(double seconds) => Pwm + PwmRate * seconds;

    private Frame Latest => _frames[(_next - 1 + History) % History];

    /// <summary>Самый ранний кадр, ещё попадающий в окно, — или самый старый, что есть.</summary>
    private Frame Before(TimeSpan window)
    {
        var limit = Latest.At - window;
        var oldest = Latest;
        for (int i = 1; i <= _count; i++)
        {
            oldest = _frames[(_next - i + History) % History];
            if (oldest.At <= limit) break;
        }

        return oldest;
    }

    private double PeakOver(TimeSpan window)
    {
        var limit = Latest.At - window;
        double peak = 0;
        for (int i = 1; i <= _count; i++)
        {
            var frame = _frames[(_next - i + History) % History];
            peak = Math.Max(peak, frame.Pwm);
            if (frame.At <= limit) break;
        }

        return peak;
    }

    private readonly record struct Frame(DateTimeOffset At, double Pwm, double Smoothed, double Speed);
}
