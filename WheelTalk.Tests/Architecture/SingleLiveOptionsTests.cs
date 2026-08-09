using System.Text;

namespace WheelTalk.Tests.Architecture;

/// <summary>
/// Замок на правило плана 29 §29.2: у каждого options-класса ровно один живой экземпляр на процесс.
/// <para>
/// <c>IOptionsMonitor</c>/<c>IOptionsSnapshot</c> держат СВОЙ кэш и свой экземпляр: правку в
/// <c>IOptions.Value</c> они не видят, компилятору второй экземпляр не виден, а стреляет он через
/// неделю (09.08.2026 — графики плиток показывали колесо, с которым приложение запустилось).
/// </para>
/// <para>
/// Тесты не референсят android-проекты, поднять их DI отсюда нельзя — потому запрет проверяется по
/// исходникам. Корень репозитория ищется по <c>WheelTalk.slnx</c>, генерируемое (<c>obj</c>,
/// <c>bin</c>, скрытые каталоги вроде <c>.claude</c>) не читается.
/// </para>
/// </summary>
public class SingleLiveOptionsTests
{
    private static readonly string[] BannedTypes = ["IOptionsMonitor", "IOptionsSnapshot"];

    [Fact]
    public void No_production_source_asks_for_a_second_live_options_instance()
    {
        var sources = ProductionSources(RepoRoot()).ToList();

        // Пустой обход прошёл бы «зелёным» навсегда: замок должен видеть код, а не отсутствие кода.
        Assert.True(sources.Count > 100, $"Обход исходников нашёл всего {sources.Count} файлов — ищет не там.");

        var offenders = sources
            .SelectMany(FindBannedTypes)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Второй живой экземпляр настроек (план 29 §29.2 — монитор и снимок запрещены):"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> FindBannedTypes(string path)
    {
        var lines = CodeWithoutComments(path);
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var type in BannedTypes)
            {
                if (lines[i].Contains(type, StringComparison.Ordinal))
                {
                    yield return $"  {path}:{i + 1} — {type}";
                }
            }
        }
    }

    /// <summary>
    /// Строки файла с вырезанными комментариями: правило запрещает тип в коде, а не упоминание его
    /// в пояснении рядом с <c>IOptions</c>. Строковые литералы не разбираются — «//» внутри строки
    /// обрежет её хвост, и это осознанно: цена разбора выше вреда.
    /// </summary>
    private static string[] CodeWithoutComments(string path)
    {
        var lines = File.ReadAllLines(path);
        var insideBlockComment = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var code = new StringBuilder(line.Length);

            for (var j = 0; j < line.Length; j++)
            {
                var pair = j + 1 < line.Length ? line.Substring(j, 2) : string.Empty;

                if (insideBlockComment)
                {
                    if (pair == "*/")
                    {
                        insideBlockComment = false;
                        j++;
                    }
                    continue;
                }

                if (pair == "//") break;
                if (pair == "/*")
                {
                    insideBlockComment = true;
                    j++;
                    continue;
                }

                code.Append(line[j]);
            }

            lines[i] = code.ToString();
        }

        return lines;
    }

    /// <summary>Боевые исходники: всё, кроме самих тестов, генерируемого и скрытых каталогов.</summary>
    private static IEnumerable<string> ProductionSources(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
        {
            yield return file;
        }

        foreach (var subdirectory in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(subdirectory);
            if (name.StartsWith('.') || name is "obj" or "bin" or "WheelTalk.Tests")
            {
                continue;
            }

            foreach (var file in ProductionSources(subdirectory))
            {
                yield return file;
            }
        }
    }

    /// <summary>Корень — там, где лежит решение; путь абсолютным не хардкодится.</summary>
    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WheelTalk.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"WheelTalk.slnx не найден вверх от {AppContext.BaseDirectory} — корень репозитория определить нечем.");
    }
}
