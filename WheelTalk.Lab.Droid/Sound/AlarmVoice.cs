using WheelTalk.Core.Alerts;

namespace WheelTalk.Lab.Droid.Sound;

/// <summary>
/// Вариант звука тревоги: имя, строка о том, что в нём проверяется, и сама волна.
/// <para>
/// Волна — чистая функция от времени и интенсивности, а не поток: тогда её слышно и меряется она
/// одинаково, а весь звуковой тракт (дорожка, поток, выравнивание громкости) живёт в одном месте —
/// <see cref="AlarmVoicePlayer"/>. Время абсолютное, от начала звучания варианта, поэтому фаза
/// непрерывна сама собой и щёлкать нечему.
/// </para>
/// </summary>
/// <param name="Sample">Отсчёт −1…1. Первый аргумент — секунды от начала, второй — интенсивность 0…1.</param>
public sealed record AlarmVoice(string Id, string Title, string Note, Func<double, double, double> Sample);

/// <summary>
/// Набор вариантов для выезда. <b>Каждый следующий отличается от опорного ровно одним приёмом</b> —
/// иначе на улице выяснится, что «седьмой лучше третьего», и непонятно чем именно.
/// <para>
/// Откуда взяты приёмы (план 26, ресерч 08.08.2026):
/// </para>
/// <list type="bullet">
/// <item><b>Рабочая полоса уже, чем в ISO 7731.</b> Стандарт называет 500–2500 Гц, но снизу её режет
/// сам телефон: микродинамик имеет резонанс около 800–1000 Гц, ниже спад 12 дБ на октаву. Сверху
/// режет ткань — до 2,5 кГц однослойный хлопок почти прозрачен, выше глушит. Ветер при этом падает
/// примерно на 10 дБ на октаву от 250 Гц, то есть выше килогерца маскирует слабее всего. Пересечение
/// трёх — <b>1–2 кГц</b>.</item>
/// <item><b>Пачки и тишина</b> вместо ровной сетки — IEC 60601-1-8: импульс высокой срочности
/// 25–75 мс, группа импульсов, пауза. ISO 7731 хочет повторение 0,5–4 Гц; наши 5 Гц выше диапазона,
/// а на потолке сигнал и вовсе сплошной.</item>
/// <item><b>Свип</b> — едущий спектральный пик уходит из-под маскирования, а восходящий тон читается
/// как рост срочности.</item>
/// <item><b>Глубокая модуляция</b> — у ветра огибающая ровная, и по этой оси он пуст.</item>
/// <item><b>Широкая стопка гармоник</b> — тракт «карман плюс динамик» непредсказуем: замаскировали
/// одну составляющую, остались остальные.</item>
/// </list>
/// </summary>
public static class AlarmVoices
{
    /// <summary>
    /// Нарастание и спад импульса. IEC требует фронта не мгновенного (иначе щелчок и дребезг
    /// динамика) и не медленного (иначе импульсы сливаются); 4 мс — середина его диапазона.
    /// </summary>
    private const double Edge = 0.004;

    public static readonly IReadOnlyList<AlarmVoice> All =
    [
        new("combat", "0 · Как сейчас",
            "Опорный: 440 Гц со второй волной 1,34f, сетка 200 мс. То, что стоит в приложении.",
            (t, i) => Grid(t, i) * CombatWave(t, 440)),

        new("high", "1 · Та же волна, основа 880",
            "Меняется только высота. Основа 440 из динамика телефона почти не выходит — слышны её гармоники.",
            (t, i) => Grid(t, i) * CombatWave(t, 880)),

        new("burst", "2 · Пачки по IEC",
            "Меняется только рисунок: импульс 40 мс, пять в группе, тишина. Пауза сжимается к пределу.",
            (t, i) => Burst(t, i, pulse: 0.040, step: 0.100, count: 5) * CombatWave(t, 880)),

        new("sweep", "3 · Свип 1→2 кГц",
            "Меняется только волна: внутри импульса частота едет вверх. Сетка прежняя.",
            (t, i) =>
            {
                double length = ToneLength(i);
                double at = t % Period;
                return Envelope(at, length) * Sweep(at, length, 1000, 2000);
            }),

        new("twotone", "4 · Двухтон 1000/1500",
            "Меняется только волна: два тона по 120 мс подряд. Ни на что вокруг не похоже.",
            (t, i) =>
            {
                double at = t % (0.240 + 0.400 * (1 - Clamp(i)));
                if (at >= 0.240) return 0;
                double inSegment = at % 0.120;
                return Envelope(inSegment, 0.120) * Math.Sin(2 * Math.PI * (at < 0.120 ? 1000 : 1500) * t);
            }),

        new("modulated", "5 · 1,4 кГц с модуляцией",
            "Сплошной тон, но громкость дышит 6…16 раз в секунду. Тишины нет вовсе — интенсивность правит скорость дыхания.",
            (t, i) =>
            {
                double rate = 6 + 10 * Clamp(i);
                double depth = 0.5 - 0.5 * Math.Cos(2 * Math.PI * rate * t);
                return depth * Math.Sin(2 * Math.PI * 1400 * t);
            }),

        new("stack", "6 · Стопка 500…2500",
            "Меняется только спектр: пять равных гармоник от 500 Гц — вся полоса ISO разом. Сетка прежняя.",
            (t, i) => Grid(t, i) * Harmonics(t, 500, 5)),

        new("sweepburst", "7 · Свип пачкой",
            "Оба сильных приёма вместе: свип 1,2→2,2 кГц импульсами по 60 мс, пять в группе, тишина.",
            (t, i) =>
            {
                double at = BurstPosition(t, i, pulse: 0.060, step: 0.120, count: 5);
                return at < 0 ? 0 : Envelope(at, 0.060) * Sweep(at, 0.060, 1200, 2200);
            }),
    ];

    public static AlarmVoice ById(string? id) =>
        All.FirstOrDefault(v => v.Id == id) ?? All[0];

    private static double Period => AlertRhythm.Period.TotalSeconds;

    private static double ToneLength(double intensity) => AlertRhythm.ToneLength(intensity).TotalSeconds;

    private static double Clamp(double intensity) => Math.Clamp(intensity, 0, 1);

    /// <summary>Боевая сетка: сигнал занимает начало периода и растёт с интенсивностью до всего периода.</summary>
    private static double Grid(double t, double intensity) => Envelope(t % Period, ToneLength(intensity));

    /// <summary>
    /// Волна приложения (<c>WheelTalk.Droid/Alerts/AlarmTone.cs</c>): основа, негармоничная вторая
    /// волна 1,34f и две гармоники. Повторена здесь, а не вызвана оттуда: стенд на боевой проект не
    /// ссылается, и <b>боевой сигнал этой работой не трогается</b>.
    /// </summary>
    private static double CombatWave(double t, double frequency)
    {
        double phase = 2 * Math.PI * frequency * t;
        return Math.Sin(phase)
            + Math.Sin(1.34 * phase)
            + 0.5 * Math.Sin(2 * phase)
            + 0.25 * Math.Sin(4 * phase);
    }

    /// <summary>
    /// Равные гармоники — так требует IEC: четыре сильнейшие в пределах 15 дБ друг от друга.
    /// Фазы разведены по Шрёдеру, иначе все синусы складываются в один острый пик, и на пик уходит
    /// весь запас громкости при копеечной мощности.
    /// </summary>
    private static double Harmonics(double t, double fundamental, int count)
    {
        double sum = 0;
        for (int n = 1; n <= count; n++)
        {
            double schroeder = Math.PI * n * (n - 1) / count;
            sum += Math.Sin(2 * Math.PI * fundamental * n * t + schroeder);
        }
        return sum;
    }

    /// <summary>
    /// Свип внутри импульса. Фаза — интеграл частоты, а не «частота × время»: иначе на конце свипа
    /// фаза не та, что слышна, и стык импульсов щёлкает.
    /// </summary>
    private static double Sweep(double at, double length, double from, double to)
    {
        double phase = 2 * Math.PI * (from * at + (to - from) * at * at / (2 * length));
        return Math.Sin(phase);
    }

    private static double Burst(double t, double intensity, double pulse, double step, int count)
    {
        double at = BurstPosition(t, intensity, pulse, step, count);
        return at < 0 ? 0 : Envelope(at, pulse);
    }

    /// <summary>
    /// Где мы внутри импульса пачки, или −1, если сейчас тишина. Пауза между группами сжимается с
    /// интенсивностью до нуля — тот же закон, что у боевой сетки: ближе к пределу плотнее.
    /// </summary>
    private static double BurstPosition(double t, double intensity, double pulse, double step, int count)
    {
        double group = step * count;
        double silence = 0.600 * (1 - Clamp(intensity));
        double at = t % (group + silence);
        if (at >= group) return -1;

        double inPulse = at % step;
        return inPulse < pulse ? inPulse : -1;
    }

    /// <summary>
    /// Огибающая импульса: подъём, полка, спад. Скачок амплитуды слышен как щелчок, и на коротком
    /// импульсе щелчков было бы больше, чем самого сигнала.
    /// </summary>
    private static double Envelope(double at, double length)
    {
        if (at < 0 || at >= length) return 0;

        double edge = Math.Min(Edge, length / 2);
        if (at < edge) return at / edge;
        if (at > length - edge) return (length - at) / edge;
        return 1;
    }
}
