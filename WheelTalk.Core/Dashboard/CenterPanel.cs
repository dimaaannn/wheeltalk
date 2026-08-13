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
/// Пара — не украшение, а способ уместиться: «t° / ▲» и «Заряд % / V ▼» стоят так с
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
/// <b>Умолчание — те же четыре смысла, что стояли жёстко</b>: «ШИМ % ▲», «t° / ▲», пробег поездки
/// и «Заряд % / V ▼». Совместимость глаза дороже красоты списка: человек смотрит
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

    /// <summary>
    /// Можно ли сложить эту строку с нижней в пару. Нельзя трижды: под последней строкой соседа нет,
    /// в паре уже двое, и третьему показанию в строке места нет — «А / Б / В» не влезает ни по
    /// ширине, ни по смыслу.
    /// </summary>
    public static bool CanMerge(IReadOnlyList<CenterRow> rows, int at) =>
        at >= 0 && at + 1 < rows.Count && rows[at].Second is null && rows[at + 1].Second is null;

    /// <summary>
    /// Сложить строку с нижней: верхняя становится первой половиной, нижняя — второй (решение
    /// владельца 13.08.2026). Пара разных величин законна — так стоит «Заряд % / V ▼» с самого
    /// первого состава.
    /// <para>
    /// Нельзя — состав возвращается <b>как был</b>: молчаливый отказ честнее молчаливой потери
    /// показания.
    /// </para>
    /// </summary>
    public static IReadOnlyList<CenterRow> Merge(IReadOnlyList<CenterRow> rows, int at)
    {
        if (!CanMerge(rows, at)) return rows;

        var merged = rows.ToList();
        merged[at] = new CenterRow(merged[at].First, merged[at + 1].First);
        merged.RemoveAt(at + 1);

        return merged;
    }

    /// <summary>
    /// Можно ли разобрать пару обратно. Дело не в самой паре, а в месте: разделение <b>добавляет
    /// строку</b>, и на потолке её поставить некуда. Отказ здесь честнее, чем разделить и дать
    /// <see cref="Sane"/> срезать хвост: срезанное — это половина показания, пропавшая молча.
    /// </summary>
    public static bool CanSplit(IReadOnlyList<CenterRow> rows, int at) =>
        at >= 0 && at < rows.Count && rows[at].Second is not null && rows.Count < MaxRows;

    /// <summary>
    /// Разобрать пару: первая половина остаётся на своём месте, вторая встаёт сразу под ней —
    /// порядок тот же, каким человек его видел, только строк стало две.
    /// </summary>
    public static IReadOnlyList<CenterRow> Split(IReadOnlyList<CenterRow> rows, int at)
    {
        if (!CanSplit(rows, at) || rows[at].Second is not { } second) return rows;

        var split = rows.ToList();
        split[at] = new CenterRow(split[at].First, null);
        split.Insert(at + 1, new CenterRow(second, null));

        return split;
    }
}
