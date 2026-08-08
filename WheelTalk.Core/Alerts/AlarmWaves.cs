namespace WheelTalk.Core.Alerts;

/// <summary>
/// Каким сигналом звучит тревога. Двумя — это всё, что осталось после отбора на слух
/// (<c>docs/android-plan-26-alarm-sound.md</c>, восемь вариантов на телефоне 08.08.2026).
/// </summary>
public enum AlarmWave
{
    /// <summary>
    /// «4+6 · двухтон стопками»: попеременно две стопки гармоник, от 500 и от 750 Гц. Первый выбор
    /// владельца — «звучит очень громко, по уровню кажется громче других».
    /// </summary>
    TwoToneStack,

    /// <summary>
    /// «6 · стопка 500…2500»: пять равных гармоник в боевой сетке 200 мс. Второй выбор владельца —
    /// «более понятно».
    /// </summary>
    Stack,
}

/// <summary>
/// Волна тревоги: <b>чистая функция времени и интенсивности</b>, одна на приложение и на стенд.
/// <para>
/// Здесь она потому, что копий её быть не должно. Стенд подбирал сигнал на слух, приложение его
/// играет, и разойдись эти две записи хоть на гармонику — на выезде сравнивали бы одно, а ехали бы
/// с другим. Платформы функция не требует, звукового потока не знает и оттого <b>проверяется
/// тестом</b> (<c>AlarmWavesTests</c>): NaN, размах и громкость видны без телефона.
/// </para>
/// <para>
/// Время абсолютное, от начала звучания. Ритм, стало быть, тоже здесь: сетка и паузы — часть
/// рисунка, а не то, чем дёргают снаружи. Проигрывателю остаётся счётчик отсчётов, и <b>только по
/// нему</b> двадцатимиллисекундный писк попадает в свою сетку — таймеру интерфейса поверх
/// девяностомиллисекундного буфера это не удавалось никогда.
/// </para>
/// <para>
/// Отсчёты уже выровнены по громкости: <see cref="Sample"/> отдаёт волну, приведённую к
/// <see cref="TargetRms"/> с потолком по пику. Это тот же уровень, на котором варианты слушали на
/// стенде, и та же громкость, какой тревога звучала до выбора звука.
/// </para>
/// <para>
/// Откуда сами приёмы — план 26: рабочая полоса 1–2 кГц (снизу режет микродинамик, сверху ткань
/// кармана, а ветер выше килогерца маскирует слабее), широкая стопка гармоник по IEC 60601-1-8
/// (замаскировали одну составляющую — остались прочие), двухтон как смена и по спектру, и по
/// времени.
/// </para>
/// </summary>
public static class AlarmWaves
{
    /// <summary>
    /// К какому среднеквадратичному уровню приводятся волны, −9 дБ от полной шкалы. Число не
    /// вкусовое: на нём варианты сравнивали на стенде, и им же звучала тревога до этой работы.
    /// </summary>
    public const double TargetRms = 0.35;

    /// <summary>Потолок по пику: у шкалы оставлен запас, чтобы не ловить ограничение тракта.</summary>
    public const double PeakCeiling = 0.98;

    /// <summary>
    /// Основа стопки и её же нижняя составляющая. Пять гармоник от неё дают ровно полосу ISO 7731 —
    /// 500, 1000, 1500, 2000, 2500 Гц.
    /// </summary>
    public const double StackRoot = 500;

    /// <summary>Вторая основа стопки, квинтой выше первой: слышно как смену высоты, а не как расстройку.</summary>
    public const double StackFifth = 750;

    /// <summary>
    /// Нарастание и спад импульса. IEC требует фронта не мгновенного (иначе щелчок и дребезг
    /// динамика) и не медленного (иначе импульсы сливаются); 4 мс — середина его диапазона.
    /// </summary>
    private const double Edge = 0.004;

    /// <summary>Длина одного тона двухтона.</summary>
    private const double ToneSegment = 0.120;

    /// <summary>Пауза между парами тонов на пороге тревоги.</summary>
    private const double ToneSilence = 0.400;

    private const int StackHarmonics = 5;

    /// <summary>Сколько секунд волны меряется при выравнивании: больше самого длинного рисунка.</summary>
    private const double MeasureSeconds = 2;

    private const int MeasureRate = 44100;

    // Считается один раз на процесс, при первом обращении к волне: около двухсот тысяч отсчётов.
    private static readonly double TwoToneStackGain = GainFor(RawTwoToneStack);
    private static readonly double StackGain = GainFor(RawStack);

    /// <summary>
    /// Отсчёт −1…1, выровненный по громкости. <paramref name="t"/> — секунды от начала звучания,
    /// <paramref name="intensity"/> — насколько близко к пределу, 0…1: она правит <b>рисунок</b>
    /// (длину сигнала и паузу), а не уровень.
    /// </summary>
    public static double Sample(AlarmWave wave, double t, double intensity) => wave switch
    {
        AlarmWave.Stack => StackGain * RawStack(t, intensity),
        _ => TwoToneStackGain * RawTwoToneStack(t, intensity),
    };

    /// <summary>Та же волна отдельной функцией — для того, кто держит набор волн списком.</summary>
    public static Func<double, double, double> Of(AlarmWave wave) => (t, intensity) => Sample(wave, t, intensity);

    /// <summary>
    /// Во сколько раз поднять волну, чтобы она звучала вровень с остальными: по среднеквадратичному
    /// уровню, но не выше потолка по пику. Замер идёт на полной интенсивности и <b>по звучащим
    /// отсчётам</b> — тишина рисунка в громкость не считается, потому что сравнивают приёмы, а не
    /// скважность.
    /// </summary>
    public static double GainFor(Func<double, double, double> wave)
    {
        double squares = 0;
        double peak = 0;
        int sounding = 0;

        for (int n = 0; n < MeasureSeconds * MeasureRate; n++)
        {
            double sample = wave(n / (double)MeasureRate, 1);
            double level = Math.Abs(sample);
            if (level < 0.001) continue;

            squares += sample * sample;
            peak = Math.Max(peak, level);
            sounding++;
        }

        if (sounding == 0 || peak <= 0) return 1;

        double rms = Math.Sqrt(squares / sounding);
        return Math.Min(TargetRms / rms, PeakCeiling / peak);
    }

    /// <summary>
    /// Боевая сетка <see cref="AlertRhythm"/>: сигнал занимает начало периода и растёт с
    /// интенсивностью до всего периода.
    /// </summary>
    public static double Grid(double t, double intensity) =>
        Envelope(t % AlertRhythm.Period.TotalSeconds, AlertRhythm.ToneLength(intensity).TotalSeconds);

    /// <summary>Два тона подряд по 120 мс, потом пауза; пауза сжимается с интенсивностью.</summary>
    public static double Alternating(double t, double intensity,
        double first, double second, Func<double, double, double> wave)
    {
        double pair = 2 * ToneSegment;
        double at = t % (pair + Silence(intensity, ToneSilence));
        if (at >= pair) return 0;

        double inSegment = at % ToneSegment;
        return Envelope(inSegment, ToneSegment) * wave(t, at < ToneSegment ? first : second);
    }

    /// <summary>
    /// Тишина между группами: сжимается до нуля к пределу. Тот же закон, что у боевой сетки —
    /// ближе к пределу плотнее, — и он же держит повторение в диапазоне ISO 7731 (0,5–4 Гц).
    /// </summary>
    public static double Silence(double intensity, double full) => full * (1 - Math.Clamp(intensity, 0, 1));

    public static double Tone(double t, double frequency) => Math.Sin(2 * Math.PI * frequency * t);

    /// <summary>
    /// Равные гармоники — так требует IEC: четыре сильнейшие в пределах 15 дБ друг от друга. Фазы
    /// разведены по Шрёдеру, иначе все синусы складываются в один острый пик, и на пик уходит весь
    /// запас громкости при копеечной мощности.
    /// </summary>
    public static double Stack(double t, double fundamental)
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
    public static double Envelope(double at, double length)
    {
        if (at < 0 || at >= length) return 0;

        double edge = Math.Min(Edge, length / 2);
        if (at < edge) return at / edge;
        if (at > length - edge) return (length - at) / edge;
        return 1;
    }

    private static double RawStack(double t, double intensity) => Grid(t, intensity) * Stack(t, StackRoot);

    private static double RawTwoToneStack(double t, double intensity) =>
        Alternating(t, intensity, StackRoot, StackFifth, Stack);
}
