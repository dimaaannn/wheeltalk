using System.Text;
using Microsoft.Extensions.Logging;
using WheelTalk.Droid.Logging;

namespace WheelTalk.Droid.Diagnostics;

/// <summary>
/// Журнал приложения в файл, рядом с поездками и дампами.
/// <para>
/// Первый раз заведён после выезда 28.07.2026 (три смерти процесса, кольцевой буфер провернулся —
/// ни строки о причинах), удалён 29.07.2026 в пользу схемы «один источник — системный буфер»
/// (план 11 §4). Возвращён 01.08.2026: полевой телефон Huawei (EMUI 15) не отдаёт приложению
/// системный буфер вовсе — журналирование приложений выключено прошивкой, и собранный кнопкой файл
/// пришёл пустым. Буфер остался дополнением (там строки среды выполнения — monodroid, art), но
/// единственный источник, который есть всегда, — свой файл.
/// </para>
/// <para>
/// Пишется всегда, без переключателя, — решение владельца 01.08.2026: журнал за галочкой уже
/// стоил разбора одного выезда, а кнопка «передать отладочную информацию» обязана отдавать
/// содержимое и тогда, когда о проблеме заранее никто не знал. Уровень — Information и выше:
/// это десятки строк в минуту, а не поток декодера.
/// </para>
/// <para>
/// Пишется в каталог внешних файлов, а не во внутренний: оттуда его забирает обычный
/// <c>adb pull</c>. Это не удобство, а условие — боевая сборка Release не отлаживаема, и
/// <c>run-as</c> к внутренним файлам не пускает.
/// </para>
/// </summary>
public static class FileLog
{
    /// <summary>
    /// Больше этого файл не растёт: при переполнении текущий становится предыдущим, а новый
    /// начинается пустым. Два файла, четыре мегабайта потолка. Предыдущий держится нарочно — иначе
    /// перезапуск после падения затирал бы ровно те строки, ради которых всё и заводилось.
    /// </summary>
    private const long MaxBytes = 2 * 1024 * 1024;

    private static readonly Lock Gate = new();

    private static StreamWriter? _writer;

    /// <summary>Сколько байт уже лежит в текущем файле — чтобы ротация срабатывала и у процесса,
    /// который живёт неделями: проверка только при открытии (как в первой версии) означала бы файл
    /// без потолка, пока приложение не перезапустят.</summary>
    private static long _length;

    public static string Path => System.IO.Path.Combine(RideFiles.Root, "wheeltalk.log");

    /// <summary>Предыдущее поколение журнала — для чтения окна, пересекающего ротацию.</summary>
    public static string PreviousPath => Path + ".1";

    public static void Line(string text)
    {
        lock (Gate) Write(text);
    }

    /// <summary>
    /// Необработанное исключение. Строка стоит один раз за жизнь процесса, а её отсутствие — целого
    /// выезда: diagnostics.log собирается отдельным механизмом, но именно журнал даёт ленту событий
    /// до самой смерти, и падение обязано в этой ленте стоять на своём месте.
    /// </summary>
    public static void Fatal(string source, Exception? exception)
    {
        lock (Gate)
        {
            Write($"!!! {source}");
            Write(exception?.ToString() ?? "(исключение не передано)");
        }
    }

    /// <summary>Вызывается под <see cref="Gate"/>.</summary>
    private static void Write(string text)
    {
        try
        {
            var writer = _writer ?? Open();
            writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {text}");

            // Грубая мера длины (текст + отметка времени + перевод строки); точность здесь не
            // нужна — потолок и так с запасом.
            _length += text.Length + 26;
            if (_length > MaxBytes) Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Журнал, который роняет приложение, когда на диске кончилось место, — это худшее из
            // всех возможных средств диагностики.
            Close();
        }
    }

    private static StreamWriter Open()
    {
        Directory.CreateDirectory(RideFiles.Root);
        Rotate();

        // AutoFlush: строка нужна на диске **до** того, как процесс умрёт, а умирает он как раз
        // тогда, когда буфер дописать некому. UTF8Encoding(false), а не Encoding.UTF8: второй
        // пишет BOM в начало нового файла, и первая строка перестаёт находиться grep'ом.
        _writer = new StreamWriter(Path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
        _length = new FileInfo(Path) is { Exists: true } file ? file.Length : 0;
        return _writer;
    }

    private static void Rotate()
    {
        var file = new FileInfo(Path);
        if (!file.Exists || file.Length < MaxBytes) return;

        File.Delete(PreviousPath);
        File.Move(Path, PreviousPath);
    }

    private static void Close()
    {
        _writer?.Dispose();
        _writer = null;
    }
}

/// <summary>
/// Тот же поток строк, что уходит в logcat, — но в файл и только начиная с Information. Отладочные
/// уровни отсечены нарочно: декодер говорит двадцать раз в секунду, и на диске от него остались бы
/// мегабайты того, что в разборе не помогает. Формат одного писателя — <see cref="FileLog"/> ставит
/// отметку времени, здесь уровень и категория, как в logcat-собрате.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class FileLogger(string category) : ILogger
    {
        private readonly string _shortCategory = category[(category.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string line = $"{Letter(logLevel)} [{_shortCategory}] {formatter(state, exception)}";
            if (exception is not null) line = $"{line}{Environment.NewLine}{exception}";

            FileLog.Line(line);
        }

        private static string Letter(LogLevel level) => level switch
        {
            LogLevel.Information => "I",
            LogLevel.Warning => "W",
            LogLevel.Error => "E",
            _ => "C",
        };
    }
}
