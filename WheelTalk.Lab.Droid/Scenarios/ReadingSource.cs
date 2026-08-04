using WheelTalk.Core.Contracts;
using WheelTalk.Dashboard.Droid;

namespace WheelTalk.Lab.Droid.Scenarios;

/// <summary>
/// Превращает сценарий в то, что рисует панель, и делает это как чистая функция от позиции:
/// одна и та же секунда даёт один и тот же кадр, сколько бы раз на неё ни встали. Иначе снять
/// пять вариантов в одной точке было бы нельзя — сглаживание с накопленным состоянием отдавало бы
/// каждому варианту своё значение.
/// <para>
/// Здесь же живёт интерполяция между кадрами. Телеметрия идёт пять раз в секунду, и лента,
/// обновляемая пять раз в секунду, ползёт ступеньками; панель обновляется в шесть раз чаще, а
/// промежуточные значения берутся отсюда.
/// </para>
/// <para>
/// Перенесено из <c>WheelTalk.Lab/Scenarios/ReadingSource.cs</c> без изменений — MAUI-типов в этом
/// файле не было, поменялось только пространство имён панели.
/// </para>
/// </summary>
public sealed class ReadingSource
{
    /// <summary>Окно производной. Секунда — столько же, сколько смотрит тревожный оцениватель.</summary>
    private static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(1);

    /// <summary>Окно пика «где значение только что было».</summary>
    private static readonly TimeSpan PeakWindow = TimeSpan.FromSeconds(3);

    /// <summary>Окно тревоги — то же, что <c>Alerts:Hold</c> в приложении.</summary>
    private static readonly TimeSpan AlertWindow = TimeSpan.FromSeconds(0.5);

    /// <summary>Ток, ниже которого колесо считается разгруженным, — опора для просадки, ампер.</summary>
    private const double NoLoadCurrent = 2;

    private readonly double[] _smoothed;
    private readonly double[] _rate;
    private readonly double[] _speedRate;
    private readonly double[] _peak;
    private readonly double[] _intensity;
    private readonly double[] _minVoltage;
    private readonly double[] _maxVoltage;
    private readonly double[] _noLoadVoltage;
    private readonly double[] _maxSag;
    private readonly int[] _maxTemperature;
    private readonly int _packCells;

    public ReadingSource(Timeline timeline, DashboardOptions options)
    {
        Timeline = timeline;
        int count = timeline.Frames.Count;
        _smoothed = new double[count];
        _rate = new double[count];
        _speedRate = new double[count];
        _peak = new double[count];
        _intensity = new double[count];
        _minVoltage = new double[count];
        _maxVoltage = new double[count];
        _noLoadVoltage = new double[count];
        _maxSag = new double[count];
        _maxTemperature = new int[count];
        _packCells = TrackHistory();
        Retune(options);
    }

    /// <summary>
    /// То, что накапливается по ходу поездки и от настроек не зависит: следы минимума и максимума,
    /// опорное холостое напряжение и размер пакета. Считается один раз — в отличие от сглаживания,
    /// крутить тут нечего.
    /// </summary>
    private int TrackHistory()
    {
        var frames = Timeline.Frames;

        double lowest = double.MaxValue;
        double highest = 0;
        double deepestSag = 0;
        int hottest = int.MinValue;

        // Опора для просадки — последнее напряжение на околонулевом токе. Пока такого не было,
        // берём наибольшее виденное: на стоянке в начале записи это одно и то же.
        double noLoad = frames[0].Snapshot.VoltageV;

        for (int i = 0; i < frames.Count; i++)
        {
            var snapshot = frames[i].Snapshot;
            double volts = snapshot.VoltageV;

            if (volts > 0)
            {
                lowest = Math.Min(lowest, volts);
                highest = Math.Max(highest, volts);

                // Опора обновляется каждый раз, когда колесо разгружено, и только тогда. Брать
                // максимум за поездку нельзя: пак за час честно разряжается на несколько вольт, и
                // тогда любая просадка складывалась бы с разрядом и врала тем сильнее, чем дольше
                // едешь.
                if (Math.Abs(snapshot.CurrentA) < NoLoadCurrent) noLoad = volts;
                deepestSag = Math.Max(deepestSag, noLoad - volts);
            }

            hottest = Math.Max(hottest, snapshot.TemperatureC);

            _minVoltage[i] = lowest == double.MaxValue ? 0 : lowest;
            _maxVoltage[i] = highest;
            _noLoadVoltage[i] = noLoad;
            _maxSag[i] = deepestSag;
            _maxTemperature[i] = hottest;
        }

        // Число банок колесо не сообщает. Пакет не бывает заряжен выше 4,2 В на банку, поэтому
        // округление вверх от наибольшего виденного напряжения даёт ближайший разумный размер.
        return highest > 0 ? (int)Math.Ceiling(highest / 4.2) : 0;
    }

    public Timeline Timeline { get; }

    /// <summary>Пересчитать всё, что зависит от настроек. Зовётся после правки ручек в стенде.</summary>
    public void Retune(DashboardOptions options)
    {
        var frames = Timeline.Frames;

        double filtered = Math.Abs(frames[0].Snapshot.Pwm);
        for (int i = 0; i < frames.Count; i++)
        {
            double raw = Math.Abs(frames[i].Snapshot.Pwm);
            double seconds = i == 0 ? 0 : (frames[i].At - frames[i - 1].At).TotalSeconds;

            // Экспоненциальное сглаживание по времени, а не по кадрам: у записи промежутки
            // неровные, и фильтр «по кадрам» на пропуске врал бы сильнее всего там, где связь и так
            // хуже всего.
            double alpha = options.SmoothingSeconds <= 0 || seconds <= 0
                ? 1
                : 1 - Math.Exp(-seconds / options.SmoothingSeconds);
            filtered += (raw - filtered) * alpha;
            _smoothed[i] = filtered;
        }

        for (int i = 0; i < frames.Count; i++)
        {
            int back = IndexBefore(i, RateWindow);
            double span = (frames[i].At - frames[back].At).TotalSeconds;
            _rate[i] = span <= 0 ? 0 : (_smoothed[i] - _smoothed[back]) / span;
            _speedRate[i] = span <= 0
                ? 0
                : (Math.Abs(frames[i].Snapshot.SpeedKmh) - Math.Abs(frames[back].Snapshot.SpeedKmh)) / span;

            _peak[i] = MaxOver(i, PeakWindow);
            _intensity[i] = Intensity(MaxOver(i, AlertWindow), options);
        }
    }

    /// <summary>
    /// Снимок сценария в этой позиции — как его отдало бы колесо, без сглаживания и интерполяции:
    /// плитки экрана «Цифры» показывают то, что колесо сказало, а не то, что посчитала панель
    /// (план 23 §3.2).
    /// </summary>
    public TelemetrySnapshot SnapshotAt(TimeSpan position) =>
        Timeline.Frames[Timeline.IndexAt(position)].Snapshot;

    public DashboardReading At(TimeSpan position)
    {
        var frames = Timeline.Frames;
        int index = Timeline.IndexAt(position);
        var frame = frames[index];

        double pwm = _smoothed[index];
        double speed = Math.Abs(frame.Snapshot.SpeedKmh);

        if (index + 1 < frames.Count)
        {
            var next = frames[index + 1];
            double span = (next.At - frame.At).TotalSeconds;
            if (span > 0)
            {
                double weight = Math.Clamp((position - frame.At).TotalSeconds / span, 0, 1);
                pwm += (_smoothed[index + 1] - pwm) * weight;
                speed += (Math.Abs(next.Snapshot.SpeedKmh) - speed) * weight;
            }
        }

        return DashboardReading.From(frame.Snapshot, _rate[index], _intensity[index], _peak[index])
            with
            {
                SpeedKmh = speed,
                Pwm = pwm,
                SpeedRate = _speedRate[index],
                MinVoltageV = _minVoltage[index],
                MaxVoltageV = _maxVoltage[index],
                NoLoadVoltageV = _noLoadVoltage[index],
                MaxSagV = _maxSag[index],
                MaxTemperatureC = _maxTemperature[index],
                PackCells = _packCells,
            };
    }

    private int IndexBefore(int index, TimeSpan window)
    {
        var frames = Timeline.Frames;
        var limit = frames[index].At - window;
        int i = index;
        while (i > 0 && frames[i - 1].At >= limit) i--;
        return i;
    }

    private double MaxOver(int index, TimeSpan window)
    {
        var frames = Timeline.Frames;
        double peak = 0;
        for (int i = IndexBefore(index, window); i <= index; i++)
        {
            peak = Math.Max(peak, Math.Abs(frames[i].Snapshot.Pwm));
        }
        return peak;
    }

    /// <summary>
    /// Та же форма, что у <c>AlertEvaluator</c>: ноль ниже порога предупреждения, единица на
    /// критическом, линейно между ними. Пороги берутся из настроек панели, а не из настроек тревог
    /// приложения — в стенде их крутят, и две независимые пары порогов расходились бы на глазах.
    /// </summary>
    private static double Intensity(double pwm, DashboardOptions options)
    {
        var thresholds = options.Thresholds;
        if (pwm < thresholds.WarnPwm) return 0;
        if (pwm >= thresholds.DangerPwm) return 1;

        double span = thresholds.DangerPwm - thresholds.WarnPwm;
        return span <= 0 ? 1 : (pwm - thresholds.WarnPwm) / span;
    }
}
