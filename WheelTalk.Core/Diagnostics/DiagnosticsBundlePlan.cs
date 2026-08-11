namespace WheelTalk.Core.Diagnostics;

/// <summary>Одна часть комплекта диагностики: как называется, откуда взята и сколько весит.</summary>
/// <param name="Name">Имя файла внутри архива — оно же то, что человек читает на экране состава.</param>
/// <param name="Path">Откуда взять. Пустая строка — части нет.</param>
/// <param name="Bytes">Вес. Ноль — класть нечего.</param>
public readonly record struct DiagnosticsPart(string Name, string Path, long Bytes);

/// <summary>
/// Что кладётся в комплект диагностики, а что не кладётся никогда (план 11 §4.3). Правило вынесено
/// из платформенного кода сюда <b>затем, чтобы его можно было проверить тестом</b>: android-проекта
/// тесты не видят, а «дамп и база наружу не уходят» — ровно то обещание, которое нельзя однажды
/// нарушить молча.
/// <para>
/// <b>Запрет важнее состава.</b> Сырой дамп BLE (мегабайт на десять минут) и файл базы со всей
/// историей поездок — это не отладочная информация, а данные владельца устройства целиком; им
/// положена отдельная кнопка с отдельным вопросом, а не попутный проезд в общем архиве. Поэтому
/// здесь не «список исключений», а <b>белый список расширений</b>: незнакомое не попадает внутрь
/// по умолчанию, и новая папка с чем угодно не протечёт наружу сама.
/// </para>
/// </summary>
public static class DiagnosticsBundlePlan
{
    /// <summary>
    /// Что разрешено класть: журнал приложения (в том числе поколение ротации <c>.log.1</c>) и
    /// текстовые выжимки, которые мы делаем сами. Всё остальное — включая <c>.csv</c> сырого дампа
    /// и <c>.db</c> базы с её спутниками <c>-wal</c>/<c>-shm</c> — не проходит.
    /// </summary>
    public static bool Allows(string fileName)
    {
        if (fileName.Length == 0) return false;

        // Поколение ротации: «diagnostics.log.1» — тот же журнал, только прошлый.
        string name = fileName;
        int lastDot = name.LastIndexOf('.');
        if (lastDot > 0 && int.TryParse(name[(lastDot + 1)..], out _)) name = name[..lastDot];

        return name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Отобрать части комплекта: по порядку, без запрещённых, без пустых и без пропавших. Пустую
    /// часть выбрасываем не из бережливости, а из честности — строка «0 байт» на экране состава
    /// обещает содержимое, которого нет.
    /// </summary>
    public static IReadOnlyList<DiagnosticsPart> Compose(IEnumerable<DiagnosticsPart> candidates) =>
        [.. candidates.Where(part => part.Bytes > 0 && part.Path.Length > 0 && Allows(part.Name))];

    /// <summary>Общий вес — то число, ради которого экран состава и существует: «столько уйдёт».</summary>
    public static long TotalBytes(IEnumerable<DiagnosticsPart> parts) => parts.Sum(part => part.Bytes);

    /// <summary>
    /// Вес словами. Считается по 1024, как показывает файловый менеджер: человек сверяет число с
    /// тем, что видит у себя, а не с десятичным килобайтом.
    /// </summary>
    public static string Weigh(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} КБ",
        _ => $"{bytes / (1024.0 * 1024.0):F1} МБ",
    };
}
