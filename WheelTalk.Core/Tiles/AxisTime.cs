namespace WheelTalk.Core.Tiles;

/// <summary>
/// Какому мгновению отвечает точка на оси времени графика. По оси отложены <b>секунды от начала
/// окна</b>, а не время суток: <c>float</c> считает секунды окна точно, а миллисекунды эпохи в него
/// уже не помещаются без потери.
/// <para>
/// <b>Ответ бывает «никакому», и ради этого здесь всё и написано.</b> Спрашивает не только разметка
/// нарисованных меток: система доступности читает график вслух и просит подписать значения ещё до
/// первой отрисовки, когда видимая область пуста, — и подставляет туда крайние числа <c>float</c>
/// (<c>Float.MaxValue</c>, бесконечность, <c>NaN</c>). Прибавленные к дате, они уносят её за
/// пределы календаря: телефон владельца, сборка 20, — открытие полноэкранного графика роняло
/// приложение с <c>ArgumentOutOfRangeException</c> внутри <c>AddSeconds</c>.
/// </para>
/// <para>
/// <b>Здесь счёт, а не подпись.</b> Как написать полученное мгновение — «HH:mm» на оси, «HH:mm:ss»
/// у выбранной точки — решает тот, кто рисует; ядру про буквы знать нечего.
/// </para>
/// </summary>
public static class AxisTime
{
    /// <summary>Мгновение этой секунды окна либо <c>null</c>, если такой точки на оси нет.</summary>
    /// <param name="from">Начало окна — ему отвечает нулевая секунда оси.</param>
    /// <param name="seconds">
    /// Секунда оси. Приходит из чужой библиотеки, и доверия ей нет никакого — ни в числе, ни в знаке.
    /// </param>
    /// <param name="windowSeconds">
    /// Длина окна: ею размечена ось (<c>AxisMinimum</c> 0 … <c>AxisMaximum</c>). Мерится с запасом в
    /// целое окно по обе стороны — ось спрашивает и про краевые метки, а зум и сглаживание двигают
    /// их за край; мусор же оторван от окна не на проценты, а на порядки.
    /// </param>
    public static DateTimeOffset? At(DateTimeOffset from, double seconds, double windowSeconds)
    {
        if (!double.IsFinite(seconds) || !double.IsFinite(windowSeconds) || windowSeconds < 0) return null;

        if (seconds < -windowSeconds || seconds > windowSeconds * 2) return null;

        // Календарь конечен, а длина окна приходит из сохранённой раскладки — то есть может быть
        // любой. Невозможную дату не строим даже тогда, когда невозможно само окно: секунда с
        // запасом от края нужна потому, что доли секунды при переводе в тики округляются вверх.
        double earliest = (DateTimeOffset.MinValue - from).TotalSeconds + 1;
        double latest = (DateTimeOffset.MaxValue - from).TotalSeconds - 1;

        return seconds >= earliest && seconds <= latest ? from.AddSeconds(seconds) : null;
    }
}
