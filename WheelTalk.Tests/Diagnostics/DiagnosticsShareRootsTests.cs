using System.Text.RegularExpressions;
using System.Xml.Linq;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Diagnostics;

/// <summary>
/// Замок «каждый корень комплекта объявлен провайдеру». <c>FileProvider</c> раздаёт ссылку только на
/// то, что перечислено в <c>file_paths.xml</c>, и на незаявленный каталог отвечает не отказом, а
/// <c>IllegalArgumentException</c> — то есть крахом приложения прямо под пальцем.
/// <para>
/// <b>Повод у замка свой.</b> Телефон владельца, сборка 20: кнопка «Отправить» роняла приложение —
/// «Failed to find configured root that contains /data/data/…/cache/wheeltalk-diagnostics-*.zip».
/// Архив собирается в кэше с самого заведения комплекта, а объявлен был один внешний каталог. Ни
/// компилятор, ни ревью такого не видят: путь строится в одном файле, а разрешение живёт в другом,
/// и связывает их только запуск на устройстве.
/// </para>
/// <para>
/// <b>Как проверяется.</b> Тест идёт от активности к тем, кто даёт ей пути (<c>DiagnosticsBundle</c>,
/// а от него — по именам вроде <c>CrashReport.Path</c> и <c>RideFiles.Root</c>), собирает в них все
/// обращения к каталогам Android и переводит их в имена узлов провайдера. Список сходится с
/// объявленным <b>в обе стороны</b>: забытый корень — краха, лишний — открытая без нужды дверь.
/// </para>
/// </summary>
public class DiagnosticsShareRootsTests
{
    private const string Activity = "WheelTalk.Droid/Diagnostics/DiagnosticsShareActivity.cs";

    private const string Paths = "WheelTalk.Droid/Resources/xml/file_paths.xml";

    /// <summary>
    /// Каталог Android → узел, которым он объявляется провайдеру. Пары взяты из описания
    /// <c>FileProvider</c>: другого способа объявить внутренний кэш, кроме <c>cache-path</c>, нет.
    /// </summary>
    private static readonly (string Call, string Node)[] Roots =
    [
        ("Context.CacheDir", "cache-path"),
        ("Context.FilesDir", "files-path"),
        ("GetExternalFilesDir", "external-files-path"),
        ("GetExternalCacheDir", "external-cache-path"),
        ("Environment.ExternalStorageDirectory", "external-path"),
    ];

    [Fact]
    public void Every_root_the_bundle_lives_in_is_declared_to_the_provider()
    {
        var declared = Declared();
        var used = Used();

        Assert.NotEmpty(used);

        foreach (string node in used)
        {
            Assert.True(declared.Contains(node),
                $"Комплект лежит в «{node}», а провайдеру этот корень не объявлен — «Отправить» уронит приложение.");
        }

        foreach (string node in declared)
        {
            Assert.True(used.Contains(node),
                $"Корень «{node}» объявлен провайдеру, а комплект в нём не лежит — дверь открыта без нужды.");
        }
    }

    /// <summary>
    /// Ссылку просят только на то, что дал комплект: сам архив и путь его части. Появись здесь
    /// третий путь — проверка корней выше перестала бы отвечать за все, и молча.
    /// </summary>
    [Fact]
    public void The_screen_asks_for_a_link_only_to_what_the_bundle_gave_it()
    {
        var asked = Regex.Matches(RepoFiles.Read(Activity), @"GetUriForFile\([^)]*new Java\.IO\.File\((?<path>[^)]+)\)")
            .Select(match => match.Groups["path"].Value)
            .ToList();

        Assert.Equal(["part.Path", "archive"], asked);

        // «archive» — то, что вернул упаковщик комплекта, а не самодельный путь рядом.
        Assert.Contains("string archive = DiagnosticsBundle.Pack(_parts);", RepoFiles.Read(Activity));
    }

    private static HashSet<string> Declared() =>
        [.. XDocument.Parse(RepoFiles.Read(Paths)).Root!.Elements().Select(node => node.Name.LocalName)];

    /// <summary>
    /// Где лежит то, чем делятся. Обход начинается с активности и идёт по тем, у кого она берёт
    /// пути: <c>Xxx.Path</c>, <c>Xxx.Root</c>, <c>Xxx.Prepare</c> и подобное — если в
    /// <c>WheelTalk.Droid</c> есть файл <c>Xxx.cs</c>, он читается тоже. Так список источников не
    /// приходится держать руками: заведённый завтра поставщик путей войдёт в обход сам.
    /// </summary>
    private static HashSet<string> Used()
    {
        var seen = new HashSet<string> { Activity };
        var queue = new Queue<string>([Activity]);
        var nodes = new HashSet<string>();

        while (queue.Count > 0)
        {
            string source = RepoFiles.Read(queue.Dequeue());

            foreach (var (call, node) in Roots)
            {
                if (source.Contains(call, StringComparison.Ordinal)) nodes.Add(node);
            }

            foreach (string next in Named(source))
            {
                if (seen.Add(next)) queue.Enqueue(next);
            }
        }

        return nodes;
    }

    /// <summary>
    /// Файлы тех типов, у которых в исходнике берут <b>путь</b>: перечислены имена, которыми путь у
    /// нас и отдают. Пойди обход по всем упоминаниям подряд — он вытянул бы половину проекта, и
    /// провайдеру пришлось бы объявлять корни, к диагностике отношения не имеющие.
    /// </summary>
    private static IEnumerable<string> Named(string source) =>
        Regex.Matches(source, @"\b(?<type>[A-Z]\w+)\.(?:Path|Root|Prepare|Pack|CacheRoot|CollectOnDemand)\b")
            .Select(match => match.Groups["type"].Value)
            .Distinct()
            .Select(type => Directory
                .EnumerateFiles(Path.Combine(RepoFiles.Root, "WheelTalk.Droid"), type + ".cs",
                    SearchOption.AllDirectories)
                .FirstOrDefault())
            .OfType<string>()
            .Select(path => Path.GetRelativePath(RepoFiles.Root, path).Replace(Path.DirectorySeparatorChar, '/'));
}
