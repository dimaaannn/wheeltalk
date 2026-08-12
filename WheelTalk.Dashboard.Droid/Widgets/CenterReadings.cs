using System.Globalization;
using WheelTalk.Core.Dashboard;
using WheelTalk.Core.Metrics;

namespace WheelTalk.Dashboard.Droid.Widgets;

/// <summary>
/// Что панель умеет показать в справочном блоке и как достать это из кадра.
/// <para>
/// <b>Почему не сам каталог величин.</b> Плитки читают <c>TelemetrySnapshot</c> и потому знают все
/// двадцать шесть величин; панель рисует <see cref="DashboardReading"/> — свой, куда меньший набор
/// (он живёт и без колеса: стенд подаёт сюда придуманную поездку). Предложи здесь всё, что есть в
/// каталоге, — половина строк показывала бы прочерк вечно, и человек узнавал бы об этом, поставив
/// строку. Поэтому список тот, что панель <b>может</b>, а имя, единица и округление приходят из
/// каталога: два источника правды о величине заводить незачем.
/// </para>
/// </summary>
public static class CenterReadings
{
    /// <summary>Величина и стороны, которые у неё есть смысл: у пробега максимума нет, он и есть максимум.</summary>
    public sealed record Offer(string Metric, IReadOnlyList<CenterAspect> Aspects);

    /// <summary>
    /// Что можно поставить в центр. Порядок — порядок в выборе: сперва то, на что смотрят на ходу.
    /// </summary>
    public static IReadOnlyList<Offer> Offered =>
    [
        new("pwm", [CenterAspect.Current, CenterAspect.Max]),
        new("speed", [CenterAspect.Current, CenterAspect.Max]),
        new("system_temp", [CenterAspect.Current, CenterAspect.Max]),
        new("voltage", [CenterAspect.Current, CenterAspect.Min, CenterAspect.Max]),
        new("battery_level", [CenterAspect.Current]),
        new("distance", [CenterAspect.Current]),
    ];

    /// <summary>Есть ли такое показание у панели вовсе — им и проверяется прочитанный состав.</summary>
    public static bool Knows(CenterReading reading) =>
        Offered.Any(offer => offer.Metric == reading.Metric && offer.Aspects.Contains(reading.Aspect));

    /// <summary>
    /// Число показания из кадра. <c>null</c> — колесо об этом молчит: рисуется прочерк, а не ноль
    /// (общее правило показа, план 23 §3.1).
    /// </summary>
    public static double? Value(CenterReading reading, DashboardReading frame) =>
        (reading.Metric, reading.Aspect) switch
        {
            ("pwm", CenterAspect.Current) => frame.Pwm,
            ("pwm", CenterAspect.Max) => frame.MaxPwm,
            ("speed", CenterAspect.Current) => frame.SpeedKmh,
            ("speed", CenterAspect.Max) => frame.TopSpeedKmh,
            ("system_temp", CenterAspect.Current) => frame.TemperatureC,
            ("system_temp", CenterAspect.Max) => frame.MaxTemperatureC,
            ("voltage", CenterAspect.Current) => frame.VoltageV,
            ("voltage", CenterAspect.Min) => frame.MinVoltageV,
            ("voltage", CenterAspect.Max) => frame.MaxVoltageV,
            ("battery_level", CenterAspect.Current) => frame.Battery,
            ("distance", CenterAspect.Current) => frame.TripKm,
            _ => null,
        };

    /// <summary>
    /// Число словом. Округление — по типу величины (<see cref="MetricRounding"/>), то же, что на
    /// плитках: одно и то же напряжение не может быть «77,6» на плитке и «78» в центре.
    /// </summary>
    /// <param name="tenths">
    /// Показывать ли десятые. На ходу они лишние — рябь в углу глаза (<c>HideTenthsAbove</c>), и
    /// правило это старше самого блока: им же живёт цифра скорости.
    /// </param>
    public static string Text(CenterReading reading, DashboardReading frame, bool tenths)
    {
        if (Value(reading, frame) is not { } value) return "—";

        var metric = MetricCatalogue.Find(reading.Metric);
        int decimals = tenths ? metric?.Decimals ?? 0 : 0;

        return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Худшая строка блока — по ней садится кегль. Считается не по живому числу: подбор от
    /// показаний давал бы прыгающую разметку, а её здесь не делают (dashboard-feedback.md).
    /// </summary>
    public static (string Value, string Caption) Worst(
        IReadOnlyList<CenterRow> rows, Func<string, string> words)
    {
        string value = "888";
        string caption = "";

        foreach (var row in rows)
        {
            string line = string.Join(" / ", row.Readings().Select(Widest));
            string name = Caption(row, words);

            if (line.Length > value.Length) value = line;
            if (name.Length > caption.Length) caption = name;
        }

        return (value, caption);
    }

    /// <summary>
    /// Худшее показание этой величины: разряды по её природе, знаки после запятой — по каталогу.
    /// Не «888.8» на всех подряд: температура трёхзначна и без десятых, и мерить её как напряжение
    /// значит зря ужимать весь блок — а ужимается он на всех строках сразу, кегль-то один.
    /// </summary>
    private static string Widest(CenterReading reading)
    {
        var metric = MetricCatalogue.Find(reading.Metric);
        int decimals = metric?.Decimals ?? 0;

        // Четыре разряда — только у пробега: сотни километров за поездку бывают, сотни процентов и
        // градусов — нет.
        string whole = new('8', reading.Metric == "distance" ? 4 : 3);

        return decimals > 0 ? whole + "." + new string('8', decimals) : whole;
    }

    /// <summary>
    /// Подпись строки: имя величины и, у пары, обе стороны через косую — «t° тек / макс». Слова
    /// приходят снаружи: библиотека ресурсов приложения не видит (тот же порядок, что у плиток).
    /// </summary>
    public static string Caption(CenterRow row, Func<string, string> words)
    {
        if (row.Second is not { } second) return Name(row.First, words);

        // Пара одной величины называется один раз: «Темп. тек / макс», а не «Темп. тек / Темп. макс».
        return second.Metric == row.First.Metric
            ? $"{Bare(row.First, words)} {Side(row.First.Aspect, words)} / {Side(second.Aspect, words)}"
            : $"{Name(row.First, words)} / {Name(second, words)}";
    }

    private static string Name(CenterReading reading, Func<string, string> words) =>
        reading.Aspect == CenterAspect.Current
            ? Bare(reading, words)
            : $"{Bare(reading, words)} {Side(reading.Aspect, words)}";

    /// <summary>
    /// Имя величины без стороны — короткое, если оно у неё есть. «Слова нет» словари отвечают
    /// по-разному (приложение рисует «!Ключ!», стенд возвращает сам ключ), и пропажей считаются оба
    /// ответа — тем же правилом, что у подписи четвертной плитки.
    /// </summary>
    private static string Bare(CenterReading reading, Func<string, string> words)
    {
        var metric = MetricCatalogue.Find(reading.Metric);
        if (metric is null) return reading.Metric;

        string key = metric.LabelKey + "Short";
        string shortened = words(key);

        return shortened.Length == 0 || shortened == key || shortened.StartsWith('!')
            ? words(metric.LabelKey)
            : shortened;
    }

    private static string Side(CenterAspect aspect, Func<string, string> words) => aspect switch
    {
        CenterAspect.Max => words("CentreAspectMax"),
        CenterAspect.Min => words("CentreAspectMin"),
        _ => words("CentreAspectCurrent"),
    };
}
