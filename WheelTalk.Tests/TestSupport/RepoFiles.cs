namespace WheelTalk.Tests.TestSupport;

/// <summary>
/// Исходники репозитория для тех проверок, которым нужен <b>боевой код целиком</b>, а поднять его
/// нельзя: тесты не референсят android-проекты, и ни каталог настроек, ни состав шторки отсюда не
/// собрать. Такие правила читаются по исходникам — путь честный, пока он не хардкодит корень и не
/// заглядывает в генерируемое.
/// </summary>
public static class RepoFiles
{
    /// <summary>Корень — там, где лежит решение; абсолютных путей в тестах нет.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Файл репозитория по пути от корня, через <c>/</c>.</summary>
    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WheelTalk.slnx"))) return directory.FullName;
        }

        throw new InvalidOperationException(
            $"WheelTalk.slnx не найден вверх от {AppContext.BaseDirectory} — корень репозитория определить нечем.");
    }

    /// <summary>
    /// Тело метода по его сигнатуре — от первой открывающей скобки до её пары. Для списков-описаний
    /// (<c>=> [ … ];</c>) годится так же: скобка первого элемента и есть начало.
    /// </summary>
    public static string MethodBody(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        if (at < 0) throw new InvalidOperationException($"В исходнике нет «{signature}» — метод переименован?");

        int open = source.IndexOfAny(['{', '['], at + signature.Length);
        char opening = source[open];
        char closing = opening == '{' ? '}' : ']';

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == opening) depth++;
            else if (source[i] == closing && --depth == 0) return source[open..i];
        }

        throw new InvalidOperationException($"У «{signature}» не сошлись скобки.");
    }
}
