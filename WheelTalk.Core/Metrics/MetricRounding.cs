using System.Globalization;

namespace WheelTalk.Core.Metrics;

/// <summary>
/// Сколько знаков после запятой показывать. Умолчание задаёт сама величина
/// (<see cref="MetricDescriptor.Decimals"/>), поверх него — <b>своё число плитки</b>: одну и ту же
/// величину человек ставит и крупной, и справочной, и подробность у них разная (решение владельца
/// 10.08.2026).
/// <para>
/// <b>Округление — только показ.</b> В историю, в базу и в расчёты идёт сырое число, каким его
/// сообщило колесо: округлённое там стало бы показанием, которого не было, и графики с крайними
/// значениями разошлись бы с записью.
/// </para>
/// </summary>
public static class MetricRounding
{
    /// <summary>
    /// Самое подробное, что предлагается человеку, — сотые. Дальше не идём не из скупости: на плитке
    /// третий знак дрожит от шума АЦП и стоит целого разряда ширины, то есть кегля всему классу.
    /// </summary>
    public const int Most = 2;

    /// <summary>Числа, которые предлагает меню плитки. «По умолчанию» стоит там отдельным пунктом.</summary>
    public static IReadOnlyList<int> Choices { get; } = [0, 1, 2];

    /// <summary>
    /// Что из сохранённого считать выбором человека. <c>null</c> — «по умолчанию», и им же
    /// становится число вне <see cref="Choices"/>: чужая новизна из раскладки, собранной другой
    /// версией, — не мусор, и плитка от неё не пропадает (правило чтения раскладки в
    /// <c>TileLayoutJson</c>).
    /// </summary>
    public static int? Chosen(int? saved) => saved is >= 0 and <= Most ? saved : null;

    /// <summary>Знаков после запятой у этой плитки: своё число старше умолчания величины.</summary>
    public static int Decimals(MetricDescriptor metric, int? chosen) => chosen ?? metric.Decimals;

    /// <summary>Строка формата для <c>ToString</c> — «F1», «F2». Одна на все виды плиток.</summary>
    public static string Format(MetricDescriptor metric, int? chosen) =>
        "F" + Decimals(metric, chosen).ToString(CultureInfo.InvariantCulture);
}
