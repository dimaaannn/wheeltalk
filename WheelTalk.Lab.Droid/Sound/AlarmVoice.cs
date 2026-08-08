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
/// Набор вариантов для выезда: три отобранных приёма, их пары и все три вместе.
/// <para>
/// <b>Первый отбор уже прошёл на телефоне 08.08.2026.</b> Из восьми вариантов первого захода
/// владелец оставил три — пачки по IEC, стопку гармоник и двухтон, — а свип, дыхание громкости и
/// голый подъём основы отсеял на слух. Номера сохранены от первого захода (0, 2, 4, 6): по ним уже
/// говорят, и переномеровать их значит потерять общий язык с тем, кто слушал.
/// </para>
/// <para>
/// <b>Отсеянный вариант 1 не пропал.</b> Он был не рисунком, а правкой несущей: основа 440 из
/// динамика телефона почти не выходит (резонанс микродинамика 800–1000 Гц, ниже спад 12 дБ на
/// октаву), слышны только её гармоники. Эта правка изначально заложена внутрь варианта 2 — его
/// несущая 880, а не 440, — и живёт в нём дальше. Ниже 500 Гц несущей нет ни у одного варианта,
/// кроме опорного, которому положено быть тем, что стоит в приложении.
/// </para>
/// <para>
/// Откуда приёмы (план 26, ресерч 08.08.2026):
/// </para>
/// <list type="bullet">
/// <item><b>Рабочая полоса уже, чем в ISO 7731.</b> Стандарт называет 500–2500 Гц, но снизу её режет
/// сам телефон, сверху — ткань кармана (до 2,5 кГц однослойный хлопок почти прозрачен, выше глушит),
/// а ветер падает примерно на 10 дБ на октаву от 250 Гц, то есть выше килогерца маскирует слабее
/// всего. Пересечение трёх — <b>1–2 кГц</b>.</item>
/// <item><b>Пачки и тишина</b> вместо ровной сетки — IEC 60601-1-8: импульс высокой срочности
/// 25–75 мс, группа импульсов, пауза. ISO 7731 хочет повторение 0,5–4 Гц; наши 5 Гц выше диапазона,
/// а на потолке сигнал и вовсе сплошной.</item>
/// <item><b>Широкая стопка гармоник</b> — тракт «карман плюс динамик» непредсказуем: замаскировали
/// одну составляющую, остались остальные. IEC требует того же: четыре сильнейшие в пределах 15 дБ.</item>
/// <item><b>Двухтон</b> — смена и по спектру, и по времени; ни на что вокруг не похоже.</item>
/// </list>
/// <para>
/// Порядок списка: опорный, три приёма поодиночке, потом их пары и все три вместе — от простого к
/// сложному, чтобы на выезде было слышно, что даёт каждое усложнение.
/// </para>
/// </summary>
public static class AlarmVoices
{
    /// <summary>
    /// Нарастание и спад импульса. IEC требует фронта не мгновенного (иначе щелчок и дребезг
    /// динамика) и не медленного (иначе импульсы сливаются); 4 мс — середина его диапазона.
    /// </summary>
    private const double Edge = 0.004;

    /// <summary>Импульс пачки: середина диапазона высокой срочности IEC (25–75 мс).</summary>
    private const double BurstPulse = 0.040;

    /// <summary>От начала одного импульса пачки до начала следующего.</summary>
    private const double BurstStep = 0.100;

    /// <summary>Сколько импульсов в группе. Десять у IEC — это две таких группы подряд.</summary>
    private const int BurstCount = 5;

    /// <summary>Пауза между группами импульсов на пороге тревоги.</summary>
    private const double BurstSilence = 0.600;

    /// <summary>Длина одного тона двухтона.</summary>
    private const double ToneSegment = 0.120;

    /// <summary>Пауза между парами тонов на пороге тревоги.</summary>
    private const double ToneSilence = 0.400;

    /// <summary>
    /// Основа стопки и её же нижняя составляющая. Пять гармоник от неё дают ровно полосу ISO —
    /// 500, 1000, 1500, 2000, 2500 Гц.
    /// </summary>
    private const double StackRoot = 500;

    /// <summary>Вторая основа стопки, квинтой выше первой: слышно как смену высоты, а не как расстройку.</summary>
    private const double StackFifth = 750;

    private const int StackHarmonics = 5;

    public static readonly IReadOnlyList<AlarmVoice> All =
    [
        new("combat", "0 · Как сейчас",
            "Опорный: основа 440, слышны 590, 880, 1760. Сетка 200 мс. То, что стоит в приложении.",
            (t, i) => Grid(t, i) * CombatWave(t, 440)),

        new("burst", "2 · Пачки по IEC",
            "Рисунок: импульс 40 мс, пять в группе, тишина. Несущая 880, слышны 1180 и 1760.",
            (t, i) => Pulse(t, i) * CombatWave(t, 880)),

        new("twotone", "4 · Двухтон 1000/1500",
            "Два чистых тона по 120 мс подряд. Несущие ровно в рабочей полосе.",
            (t, i) => Alternating(t, i, first: 1000, second: 1500, Tone)),

        new("stack", "6 · Стопка 500…2500",
            "Пять равных гармоник от 500 — вся полоса ISO разом. Сетка прежняя, 200 мс.",
            (t, i) => Grid(t, i) * Stack(t, StackRoot)),

        new("burststack", "2+6 · Стопка пачками",
            "Спектр шестого, рисунок второго. Оба из IEC, потому и родня: 500…2500 импульсами по 40 мс.",
            (t, i) => Pulse(t, i) * Stack(t, StackRoot)),

        new("bursttwotone", "2+4 · Двухтон пачками",
            "Импульсы пачки чередуют 1000 и 1500 — смена высоты внутри группы, тишина между группами.",
            (t, i) => PulseAlternating(t, i, first: 1000, second: 1500, Tone)),

        new("twotonestack", "4+6 · Двухтон стопками",
            "Чередуются две стопки: от 500 и от 750, квинта. Широкий спектр, меняющийся по высоте.",
            (t, i) => Alternating(t, i, StackRoot, StackFifth, Stack)),

        new("all", "2+4+6 · Всё вместе",
            "Стопки от 500 и 750 попеременно, пачкой по IEC. Предел того, что дают три приёма разом.",
            (t, i) => PulseAlternating(t, i, StackRoot, StackFifth, Stack)),
    ];

    public static AlarmVoice ById(string? id) => All.FirstOrDefault(v => v.Id == id) ?? All[0];

    private static double Period => AlertRhythm.Period.TotalSeconds;

    private static double Clamp(double intensity) => Math.Clamp(intensity, 0, 1);

    /// <summary>Боевая сетка: сигнал занимает начало периода и растёт с интенсивностью до всего периода.</summary>
    private static double Grid(double t, double intensity) =>
        Envelope(t % Period, AlertRhythm.ToneLength(intensity).TotalSeconds);

    /// <summary>Огибающая пачки: пять импульсов, потом тишина. Ноль, пока идёт тишина.</summary>
    private static double Pulse(double t, double intensity)
    {
        var (_, at) = Slot(t, intensity);
        return at < 0 ? 0 : Envelope(at, BurstPulse);
    }

    /// <summary>Пачка, у которой соседние импульсы звучат разной высотой.</summary>
    private static double PulseAlternating(double t, double intensity,
        double first, double second, Func<double, double, double> wave)
    {
        var (index, at) = Slot(t, intensity);
        return at < 0 ? 0 : Envelope(at, BurstPulse) * wave(t, index % 2 == 0 ? first : second);
    }

    /// <summary>Два тона подряд по 120 мс, потом пауза; пауза сжимается с интенсивностью.</summary>
    private static double Alternating(double t, double intensity,
        double first, double second, Func<double, double, double> wave)
    {
        double pair = 2 * ToneSegment;
        double at = t % (pair + Silence(intensity, ToneSilence));
        if (at >= pair) return 0;

        double inSegment = at % ToneSegment;
        return Envelope(inSegment, ToneSegment) * wave(t, at < ToneSegment ? first : second);
    }

    /// <summary>
    /// Где мы внутри импульса пачки и какой это импульс по счёту. Позиция −1 значит тишину.
    /// </summary>
    private static (int Index, double At) Slot(double t, double intensity)
    {
        double group = BurstStep * BurstCount;
        double at = t % (group + Silence(intensity, BurstSilence));
        if (at >= group) return (0, -1);

        int index = (int)(at / BurstStep);
        double inPulse = at - index * BurstStep;
        return (index, inPulse < BurstPulse ? inPulse : -1);
    }

    /// <summary>
    /// Тишина между группами: сжимается до нуля к пределу. Тот же закон, что у боевой сетки —
    /// ближе к пределу плотнее, — и он же держит повторение в диапазоне ISO 7731 (0,5–4 Гц).
    /// <para>
    /// Пауза у пачки и у двухтона разная, и это не небрежность: оба звучали на телефоне 08.08.2026
    /// именно с этими паузами и с ними отобраны. Свести их к одному числу значило бы поменять то,
    /// что уже слушали, — и следующий выезд сравнивал бы не то, что предыдущий.
    /// </para>
    /// </summary>
    private static double Silence(double intensity, double full) => full * (1 - Clamp(intensity));

    private static double Tone(double t, double frequency) => Math.Sin(2 * Math.PI * frequency * t);

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
    private static double Stack(double t, double fundamental)
    {
        double sum = 0;
        for (int n = 1; n <= StackHarmonics; n++)
        {
            double schroeder = Math.PI * n * (n - 1) / StackHarmonics;
            sum += Math.Sin(2 * Math.PI * fundamental * n * t + schroeder);
        }
        return sum;
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
