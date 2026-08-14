using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using WheelTalk.Core.Diagnostics;
using WheelTalk.Droid.App;
using WheelTalk.Droid.Logging;
using WheelTalk.Storage;

namespace WheelTalk.Droid.Diagnostics;

/// <summary>
/// Комплект под раздачу (план 11 §4.3): вместо голого <c>diagnostics.log</c> уходит архив, в
/// котором есть чем объяснить увиденное в журнале.
/// <para>
/// <b>Контекст и изменённые настройки лежат внутри журнала, а не отдельными файлами</b> — так их
/// уже кладёт <see cref="CrashReport.CollectOnDemand"/>, и это не лень, а решение. Контекст
/// («колесо, протокол, шла ли запись») ценен своим <b>местом в ленте</b>: он стоит рядом со
/// строками того же часа, и вырванный в отдельный файл теряет привязку ко времени. Дублировать же
/// его вторым файлом значило бы завести два источника одного и того же — а они расходятся при
/// первой правке.
/// </para>
/// <para>
/// <b>Сырого дампа BLE и файла базы здесь нет и не будет.</b> Мегабайт на десять минут и вся
/// история поездок — это данные владельца устройства целиком, а не отладочная информация; им
/// положена отдельная кнопка с отдельным вопросом. Запрет держит не эта сборка, а
/// <see cref="DiagnosticsBundlePlan"/> — белым списком расширений, под тестом.
/// </para>
/// </summary>
public static class DiagnosticsBundle
{
    /// <summary>Сколько последних поездок описывать. Десяти хватает, чтобы увидеть неделю езды, и мало, чтобы выжимка осталась выжимкой.</summary>
    private const int Rides = 10;

    private const string RidesFile = "rides.txt";

    /// <summary>
    /// Формат метки времени в именах, которые видит получатель пересылки: тот же, что у архива —
    /// один способ отличать «новое» от «старое», а не два похожих рядом.
    /// </summary>
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    /// <summary>
    /// Собрать части. Журнал пересобирается тут же (<see cref="CrashReport.CollectOnDemand"/> —
    /// он и дописывает свежий контекст с настройками), выжимка по поездкам пишется в кэш.
    /// <para>
    /// Возвращает то, что <b>реально есть</b>: пустые и запрещённые части отсеивает
    /// <see cref="DiagnosticsBundlePlan.Compose"/>, и на экране состава человек видит ровно то, что
    /// уйдёт.
    /// </para>
    /// </summary>
    public static IReadOnlyList<DiagnosticsPart> Prepare()
    {
        string log = CrashReport.CollectOnDemand();
        string previous = log + ".1";
        string rides = WriteRides();

        return DiagnosticsBundlePlan.Compose(
        [
            Part("diagnostics.log", log),
            Part("diagnostics.log.1", previous),
            Part(RidesFile, rides),
        ]);
    }

    /// <summary>Имя среза полной ленты — и на диске, и внутри архива, и на экране состава.</summary>
    public const string FullLogFile = "wheeltalk-log-24h.log";

    /// <summary>
    /// Срез полной ленты журнала за сутки (решение владельца 15.08.2026) — <b>отдельная кнопка, не
    /// часть комплекта</b>: в комплекте лежит выжимка <c>diagnostics.log</c>, а это лента целиком, и
    /// смешивать их значило бы каждый раз пересылать мегабайты там, где хватает килобайтов.
    /// <para>
    /// Обе поколения ленты по порядку — сперва прошлое, затем нынешнее: окно суток почти всегда
    /// пересекает ротацию, и без предыдущего файла срез обрывался бы на самом интересном.
    /// </para>
    /// <para>
    /// Пусто (журнал не заведён или сутки прошли молча) — <see cref="DiagnosticsBundlePlan.Compose"/>
    /// вернёт пустой состав, и экран честно скажет, что отправлять нечего. Пустой архив, который
    /// «вроде отправился», — худший из ответов.
    /// </para>
    /// </summary>
    public static IReadOnlyList<DiagnosticsPart> PrepareFullLog()
    {
        string path = System.IO.Path.Combine(CacheRoot(), FullLogFile);

        try
        {
            // Построчно и потоком: два файла по два мегабайта потолком, тянуть их в память целиком
            // незачем — тем более что срез обычно короче самих файлов.
            using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
            foreach (string line in LogWindow.Tail(Lines(), DateTime.Now)) writer.WriteLine(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Не отдать срез — обидно; уронить приложение на кнопке диагностики — стыдно.
            File.WriteAllText(path, $"Срез журнала недоступен: {ex.Message}{Environment.NewLine}");
        }

        return DiagnosticsBundlePlan.Compose([Part(FullLogFile, path)]);
    }

    /// <summary>Лента по порядку: прошлое поколение, затем нынешнее. Пропавшего файла просто нет.</summary>
    private static IEnumerable<string> Lines()
    {
        foreach (string path in (string[])[FileLog.PreviousPath, FileLog.Path])
        {
            if (!File.Exists(path)) continue;

            foreach (string line in File.ReadLines(path)) yield return line;
        }
    }

    /// <summary>
    /// Упаковать отобранное в один архив в кэше. Имя с датой — чтобы в переписке два отчёта не
    /// оказались одним файлом, и чтобы было видно, когда он снят.
    /// </summary>
    public static string Pack(IReadOnlyList<DiagnosticsPart> parts) =>
        Pack(parts, "wheeltalk-diagnostics", CompressionLevel.Optimal);

    /// <summary>
    /// Тот же архив для среза полной ленты, но <b>сжатый до предела</b> (<see cref="CompressionLevel.SmallestSize"/>,
    /// решение владельца 15.08.2026: «максимальное сжатие»). У комплекта это лишняя работа ради
    /// килобайтов, а здесь — мегабайты однообразных строк, которые ужимаются в разы, и передаёт их
    /// человек со своего мобильного.
    /// </summary>
    public static string PackFullLog(IReadOnlyList<DiagnosticsPart> parts) =>
        Pack(parts, "wheeltalk-log24h", CompressionLevel.SmallestSize);

    private static string Pack(IReadOnlyList<DiagnosticsPart> parts, string name, CompressionLevel level)
    {
        string path = System.IO.Path.Combine(
            CacheRoot(),
            // Метка в начале — тем же порядком, что у частей: имена пересылок сортируются по
            // времени, и никакая пара не тёзки.
            $"{DateTimeOffset.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture)}-{name}.zip");

        if (File.Exists(path)) File.Delete(path);

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var part in parts)
        {
            // Ещё одна проверка на входе в архив, а не только при отборе: между показом состава и
            // нажатием «Отправить» список проходит через чужие руки — экран, намерение, — и
            // единственное место, где обещание «дампа и базы здесь нет» обязано держаться, это
            // мгновение записи.
            if (!DiagnosticsBundlePlan.Allows(part.Name)) continue;

            archive.CreateEntryFromFile(part.Path, part.Name, level);
        }

        return path;
    }

    /// <summary>
    /// Имя, под которым часть комплекта уходит наружу при пересылке одним файлом (кнопка
    /// «Открыть»): дисковое имя не трогаем — по нему живут ротация журнала и сбор комплекта, —
    /// а получателю показываем другое, с меткой времени пересылки. Без неё получатель складывает
    /// одноимённые файлы рядом, и старый открывается вместо нового (так уже было с APK в
    /// «Загрузках»).
    /// <para>
    /// Метка стоит <b>в начале</b> (решение владельца 14.08.2026): дисковое имя остаётся целым —
    /// «20260814-0830-diagnostics.log.1» читается, а попытка воткнуть метку перед расширением
    /// давала «diagnostics.log-….1». Заодно файлы у получателя сортируются по времени сами.
    /// </para>
    /// </summary>
    public static string DisplayName(string diskName)
    {
        string stamp = DateTimeOffset.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture);

        return $"{stamp}-{diskName}";
    }

    /// <summary>
    /// Итоги последних поездок — <b>выжимка, а не выгрузка</b>: дата, километры, длительность и
    /// сколько строк записано. По ним видно, шла ли запись вообще и похожи ли числа на правду; сами
    /// поездки остаются на устройстве.
    /// </summary>
    private static string WriteRides()
    {
        string path = System.IO.Path.Combine(CacheRoot(), RidesFile);

        try
        {
            var exporter = MainApplication.Services.GetRequiredService<RideExporter>();

            var lines = exporter.Rides()
                .OrderByDescending(ride => ride.StartedAt)
                .Take(Rides)
                .Select(Describe);

            File.WriteAllLines(path, ["Последние поездки (итоги; сами данные поездок не прилагаются)", .. lines]);
            return path;
        }
        catch (Exception ex)
        {
            // База не открылась или её нет вовсе — это не повод не отдать журнал: комплект
            // собирается из того, что есть. Причина уходит в тот же файл, чтобы разбирающий не
            // гадал, почему поездок нет.
            File.WriteAllText(path, $"Итоги поездок недоступны: {ex.Message}{Environment.NewLine}");
            return path;
        }
    }

    private static string Describe(RideSummary ride)
    {
        string when = ride.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string distance = ride.Totals is { } totals
            ? totals.DistanceKm.ToString("F2", CultureInfo.InvariantCulture) + " км"
            : "— км";
        string duration = ride.Duration is { } span
            ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : "в процессе";

        return $"{when} · {ride.Name} · {distance} · {duration} · строк {ride.Rows.ToString(CultureInfo.InvariantCulture)}";
    }

    private static DiagnosticsPart Part(string name, string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? new DiagnosticsPart(name, path, file.Length) : new DiagnosticsPart(name, "", 0);
    }

    /// <summary>
    /// Кэш, а не каталог журналов: собранное здесь — производное, его не жалко потерять, и система
    /// вправе прибрать его сама, когда на устройстве кончится место.
    /// </summary>
    private static string CacheRoot()
    {
        string root = Android.App.Application.Context.CacheDir?.AbsolutePath ?? RideFiles.Root;
        Directory.CreateDirectory(root);
        return root;
    }
}
