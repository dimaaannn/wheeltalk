using System.Globalization;
using WheelTalk.Core.Dashboard;
using WheelTalk.Core.Metrics;
using WheelTalk.Dashboard.Droid.Screen.Tiles;

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
    /// Счётчик поездки — путь, накопленный от точки отсчёта, которую двигает <b>только рука</b>
    /// хозяина (решение владельца 12.08.2026: «как „Поездка A/B“ в машине»). Ни новая поездка, ни
    /// смена колеса, ни перезапуск его не обнуляют — только кнопка шторки.
    /// </summary>
    public const string TripCounter = "trip_counter";

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
        // Счётчик — только «сейчас»: он сам себе максимум, а нулём его делает кнопка, не поездка.
        new(TripCounter, [CenterAspect.Current]),
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
            (TripCounter, CenterAspect.Current) => frame.TripCounterKm,
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

        int decimals = tenths ? Shape(reading.Metric).Decimals : 0;

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
        var (_, decimals, digits) = Shape(reading.Metric);
        string whole = new('8', digits);

        return decimals > 0 ? whole + "." + new string('8', decimals) : whole;
    }

    /// <summary>
    /// Подпись строки <b>на панели</b>: знак величины и знаки сторон — «ШИМ % ▲», «t° / ▲»,
    /// «Заряд % / V ▼» (решение владельца 12.08.2026: «оптимизировать текст, оставив минимально
    /// понятным; знаки макс и мин — уже принятые»). Слова приходят снаружи: библиотека ресурсов
    /// приложения не видит (тот же порядок, что у плиток).
    /// </summary>
    public static string Caption(CenterRow row, Func<string, string> words) => Line(row, words, tight: true);

    /// <summary>
    /// Та же строка <b>для меню правки</b> — полными именами: «Скорость ▲», «Напряжение ▼»,
    /// «Температура». Сокращения заведены ради тесноты панели (решение владельца 12.08.2026 —
    /// «сокращения только для отображения на панели»), а в меню теснота другая: там строка занимает
    /// всю ширину окна и переносится, зато выбирают по ней вслепую — знак «V» в списке из двенадцати
    /// строк узнать труднее, чем слово.
    /// </summary>
    public static string Title(CenterRow row, Func<string, string> words) => Line(row, words, tight: false);

    private static string Line(CenterRow row, Func<string, string> words, bool tight)
    {
        if (row.Second is not { } second) return Name(row.First, words, tight);

        // Пара одной величины называется один раз: «t° / ▲», а не «t° / t° ▲». Держится это на
        // знаке у второй стороны — ему и достаётся весь смысл «а это максимум». Знака нет (вторая
        // сторона тоже текущая) — сливать нечего, иначе за косой осталась бы пустота.
        return second.Metric == row.First.Metric && Mark(second.Aspect).Length > 0
            ? Named(Bare(row.First, words, tight), Mark(row.First.Aspect)) + " / " + Mark(second.Aspect)
            : $"{Name(row.First, words, tight)} / {Name(second, words, tight)}";
    }

    private static string Name(CenterReading reading, Func<string, string> words, bool tight) =>
        Named(Bare(reading, words, tight), Mark(reading.Aspect));

    /// <summary>Имя и знак через пробел — либо одно имя: висячий пробел в подписи не нужен никому.</summary>
    private static string Named(string bare, string mark) => mark.Length > 0 ? bare + " " + mark : bare;

    /// <summary>
    /// Знак стороны — те же «▲» и «▼», что на плитках (решение владельца 12.08.2026 — «использовать
    /// уже принятые знаки макс и мин»): язык знаков на экране один, и глифы взяты у плитки, а не
    /// написаны здесь второй раз. У текущего показания знака нет вовсе: голое имя величины и значит
    /// «сейчас», а слово «тек» занимало место и не говорило ничего.
    /// </summary>
    private static string Mark(CenterAspect aspect) => aspect switch
    {
        CenterAspect.Max => TileView.MarkHighest,
        CenterAspect.Min => TileView.MarkLowest,
        _ => "",
    };

    /// <summary>
    /// Имя величины без знака стороны — <b>двумя мерами</b>, по месту, куда оно встанет.
    /// <para>
    /// <b>Панели — знак, и он старше короткого имени.</b> Единиц центр не рисует вовсе (места нет:
    /// строка со значением и подписью живёт в 25 dp), поэтому единица переехала в саму подпись —
    /// «км/ч», «ШИМ %», «V», «t°», «Заряд %» (решение владельца 12.08.2026: «общепринятые обозначения
    /// величин»). Коротким именем (<c>…Short</c>) этого не сделать: то же имя стоит на четвертной
    /// плитке, а там единица нарисована рядом с числом, и «V» превратило бы её в «V 78,4 В».
    /// </para>
    /// <para>
    /// <b>Меню — имя целиком</b> (<c>…Full</c>, а нет его — само имя величины). Ключ заведён лишь
    /// тем двум, чьё имя в ресурсах названо от раздела экрана «Данные» и в одиночку не читается:
    /// «Плата» — это температура платы, «За поездку» — пробег за поездку, и оба владелец забраковал
    /// именно как строки, стоящие сами по себе. Менять сами имена нельзя: на «Данных» они стоят под
    /// своим разделом и рядом с соседями («Двигатель», «За сеанс», «Одометр»), где сокращать их до
    /// «Температуры» значило бы потерять, о чём речь.
    /// </para>
    /// </summary>
    private static string Bare(CenterReading reading, Func<string, string> words, bool tight)
    {
        if (Shape(reading.Metric).LabelKey is not { } label) return reading.Metric;

        return tight
            ? Said(words, label + "Sign") ?? Said(words, label + "Short") ?? words(label)
            : Said(words, label + "Full") ?? words(label);
    }

    /// <summary>
    /// Слово под ключом либо <c>null</c>, если его нет. «Слова нет» словари отвечают по-разному
    /// (приложение рисует «!Ключ!», стенд возвращает сам ключ), и пропажей считаются оба ответа —
    /// тем же правилом, что у подписи четвертной плитки.
    /// </summary>
    private static string? Said(Func<string, string> words, string key)
    {
        string word = words(key);

        return word.Length == 0 || word == key || word.StartsWith('!') ? null : word;
    }

    /// <summary>
    /// Величина самой панели: та, которой в каталоге телеметрии нет и быть не может — её не прочесть
    /// из снимка, она считается из одометра и точки отсчёта.
    /// </summary>
    private sealed record OwnMetric(string LabelKey, int Decimals, int Digits);

    private static readonly Dictionary<string, OwnMetric> Own = new(StringComparer.Ordinal)
    {
        // Счётчик поездки: путь копится неделями, и четвёртый разряд ему нужен, в отличие от
        // пробега за поездку. Десятая доля километра — как у всех пробегов (каталог, 10.08.2026).
        [TripCounter] = new("MetricTripCounter", Decimals: 1, Digits: 4),
    };

    /// <summary>
    /// Чем величина подписана и каким числом мерится. Незнакомая величина (состав с чужого телефона
    /// либо из сборки, где она была) остаётся без ключа: подпишется своим идентификатором, а не
    /// пропадёт молча.
    /// </summary>
    private static (string? LabelKey, int Decimals, int Digits) Shape(string metric)
    {
        if (Own.TryGetValue(metric, out var own)) return (own.LabelKey, own.Decimals, own.Digits);

        var found = MetricCatalogue.Find(metric);

        // Четыре разряда — только у пробегов: сотни километров за поездку бывают, сотни процентов и
        // градусов — нет.
        return (found?.LabelKey, found?.Decimals ?? 0, metric == "distance" ? 4 : 3);
    }
}
