using System.Globalization;
using System.IO.Compression;
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

    /// <summary>
    /// Упаковать отобранное в один архив в кэше. Имя с датой — чтобы в переписке два отчёта не
    /// оказались одним файлом, и чтобы было видно, когда он снят.
    /// </summary>
    public static string Pack(IReadOnlyList<DiagnosticsPart> parts)
    {
        string path = System.IO.Path.Combine(
            CacheRoot(),
            $"wheeltalk-diagnostics-{DateTimeOffset.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture)}.zip");

        if (File.Exists(path)) File.Delete(path);

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var part in parts)
        {
            // Ещё одна проверка на входе в архив, а не только при отборе: между показом состава и
            // нажатием «Отправить» список проходит через чужие руки — экран, намерение, — и
            // единственное место, где обещание «дампа и базы здесь нет» обязано держаться, это
            // мгновение записи.
            if (!DiagnosticsBundlePlan.Allows(part.Name)) continue;

            archive.CreateEntryFromFile(part.Path, part.Name, CompressionLevel.Optimal);
        }

        return path;
    }

    /// <summary>
    /// Имя, под которым часть комплекта уходит наружу при пересылке одним файлом (кнопка
    /// «Открыть»): дисковое имя не трогаем — по нему живут ротация журнала и сбор комплекта, —
    /// а получателю показываем другое, с меткой времени пересылки. Без неё получатель складывает
    /// одноимённые файлы рядом, и старый открывается вместо нового (так уже было с APK в
    /// «Загрузках»).
    /// </summary>
    public static string DisplayName(string diskName)
    {
        string stem = System.IO.Path.GetFileNameWithoutExtension(diskName);
        string extension = System.IO.Path.GetExtension(diskName);
        string stamp = DateTimeOffset.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture);

        return $"{stem}-{stamp}{extension}";
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
