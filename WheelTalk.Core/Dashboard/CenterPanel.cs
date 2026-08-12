namespace WheelTalk.Core.Dashboard;

/// <summary>Какое показание величины стоит в центре: сейчас, крайнее сверху или крайнее снизу.</summary>
public enum CenterAspect
{
    /// <summary>То, что колесо говорит прямо сейчас.</summary>
    Current,

    /// <summary>Наибольшее за поездку — «на что оказалось способно».</summary>
    Max,

    /// <summary>Наименьшее за поездку — след самой тяжёлой минуты.</summary>
    Min,
}

/// <summary>Одно показание: величина каталога и её сторона.</summary>
public readonly record struct CenterReading(string Metric, CenterAspect Aspect);

/// <summary>
/// Строка справочного блока: одно показание, а при нужде — <b>два в одной строке</b>.
/// <para>
/// Пара — не украшение, а способ уместиться: «t° тек / макс» и «заряд % / мин В» стоят так с
/// прогона 3, и место в центре тесное (около 152 dp высоты на эталонном экране). Расщепи их на
/// отдельные строки — и четыре смысла станут шестью строками, то есть самым мелким кеглем.
/// </para>
/// </summary>
public readonly record struct CenterRow(CenterReading First, CenterReading? Second)
{
    public CenterRow(string metric, CenterAspect aspect = CenterAspect.Current)
        : this(new CenterReading(metric, aspect), null)
    {
    }

    /// <summary>Оба показания строки по порядку — тем, кто рисует и меряет.</summary>
    public IEnumerable<CenterReading> Readings()
    {
        yield return First;
        if (Second is { } second) yield return second;
    }
}

/// <summary>
/// Состав справочного блока в центре главного экрана: список строк, собранный человеком
/// (решение владельца 12.08.2026 — «взять подход табличек»).
/// <para>
/// <b>Умолчание — те же четыре смысла, что стояли жёстко</b>: макс ШИМ, температура «тек / макс»,
/// пробег поездки и «заряд % / мин В». Совместимость глаза дороже красоты списка: человек смотрит
/// в этот центр каждый выезд, и новая установка не должна показывать ему другой набор.
/// </para>
/// <para>
/// <b>Потолок — шесть строк.</b> Считано, а не выбрано: центр даёт около 152 dp высоты, строка при
/// поле читаемости (<see cref="CenterTypography"/>) занимает около 25 dp вместе с подписью, и
/// седьмая строка не влезает уже ни при каком кегле. Больше шести — это не «мельче», это «не
/// показать».
/// </para>
/// </summary>
public static class CenterLayout
{
    public const int MaxRows = 6;

    public static IReadOnlyList<CenterRow> Default =>
    [
        new("pwm", CenterAspect.Max),
        new(new CenterReading("system_temp", CenterAspect.Current),
            new CenterReading("system_temp", CenterAspect.Max)),
        new("distance"),
        new(new CenterReading("battery_level", CenterAspect.Current),
            new CenterReading("voltage", CenterAspect.Min)),
    ];

    /// <summary>
    /// Список, приведённый к тому, что панель вправе показать: не длиннее потолка и без пустых
    /// строк. Пустой список значит «показывать нечего» — и это законный выбор человека, снявшего
    /// все строки: центр остаётся со скоростью, как в самой первой панели.
    /// </summary>
    public static IReadOnlyList<CenterRow> Sane(IEnumerable<CenterRow>? rows) => rows is null
        ? Default
        : rows.Where(row => row.First.Metric.Length > 0).Take(MaxRows).ToList();
}
