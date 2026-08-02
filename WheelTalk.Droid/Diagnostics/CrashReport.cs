using System.Globalization;
using System.Text;
using WheelTalk.Core.Logging;
using WheelTalk.Droid.Logging;

namespace WheelTalk.Droid.Diagnostics;

/// <summary>
/// Отладочная информация: собирается при падении и по кнопке.
/// <para>
/// Здесь стояло «источник один: буфер» — своего файлового журнала не было намеренно, чтобы не
/// заводить два формата и два места правды. 01.08.2026 это опровергнуто полем: Huawei (EMUI 15)
/// не отдаёт приложению системный буфер вовсе, и собранный кнопкой файл пришёл пустым. Поэтому
/// источников два, и роли у них разные: <see cref="FileLog"/> — то, что писали мы (есть всегда),
/// буфер — то, что писала среда выполнения (`monodroid`, `art`; есть, где прошивка позволяет).
/// </para>
/// <para>
/// Повод — <b>отсутствие флага штатного завершения</b>. Пока приложение живо и должно быть живо,
/// рядом с журналами лежит метка. Уходим по-человечески — метку убираем; умерли — она осталась, и
/// следующий запуск это видит. Это дешевле любой периодической записи: при нормальной работе не
/// тратится ничего, ни на старте, ни в поездке.
/// </para>
/// <para>
/// <b>Чего здесь не будет.</b> Системные строки — `am_anr`, `am_kill`, нативный трейс `F DEBUG` —
/// пишут другие процессы, и `logd` отдаёт приложению только его собственный uid. Проверено на
/// телефоне 29.07.2026: в собранном файле ровно два наших pid и ни одной чужой строки. За
/// системной стороной по-прежнему нужен `adb` с компьютера (см. roadmap).
/// </para>
/// </summary>
public static class CrashReport
{
    /// <summary>Потолок строк в отчёте — после отсева шума, а не до него.</summary>
    private const int Lines = 2000;

    /// <summary>
    /// Окно журнала: полчаса до сбора. Столько живёт эпизод, который стоит разбирать — уход в фон,
    /// обрыв, смерть процесса; всё, что дальше, к причине уже не относится.
    /// </summary>
    private const int WindowMinutes = 30;

    private const long MaxBytes = 2 * 1024 * 1024;

    private static readonly Lock Gate = new();

    private static bool _activityAlive;
    private static bool _serviceAlive;

    /// <summary>
    /// Wired once, from <c>CrashGuard.SubscribeAppLevelHandlers</c>, right after the container is
    /// built — <see cref="BleFrameTail.FormatSection"/> itself, not a snapshot taken now. Left
    /// unset (as in tests, or if collection races container startup), the section is just skipped
    /// — a diagnostics report that can't show the ring is still a report.
    /// </summary>
    public static Func<string>? BleFrames { get; set; }

    public static string Path => System.IO.Path.Combine(RideFiles.Root, "diagnostics.log");

    private static string MarkerPath => System.IO.Path.Combine(RideFiles.Root, "running.marker");

    /// <summary>
    /// Проверка при старте: если метка на месте, прошлый запуск не попрощался. Тогда — и только
    /// тогда — собираем хвост буфера. Возвращает <c>true</c>, если сбор был.
    /// </summary>
    public static bool CollectIfPreviousRunCrashed()
    {
        if (!File.Exists(MarkerPath)) return false;

        // В метке лежит время, когда приложение в последний раз подтвердило, что живо, — то есть
        // почти время смерти. Оно и задаёт окно журнала: без него отчёт, собранный через три часа
        // после убийства (телефон в кармане, приложение никто не открывал), содержал бы последние
        // полчаса чужой жизни и ни одной строки о том, что случилось.
        var died = ReadMarkerTime();
        string when = died is { } time
            ? $"падение: прошлый запуск не завершился штатно, жив был в {time.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
            : "падение: прошлый запуск не завершился штатно";

        Collect(when, since: died?.AddMinutes(-5));
        return true;
    }

    private static DateTimeOffset? ReadMarkerTime()
    {
        try
        {
            return DateTimeOffset.TryParse(File.ReadAllText(MarkerPath), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var time) ? time : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// «Мы должны быть живы». Метка ставится, пока экран активен **или** работает сервис связи с
    /// колесом: поездка идёт с погашенным экраном, и падение в ней — тоже падение. Снимается,
    /// когда неверно и то, и другое: свёрнутое приложение без соединения Android имеет полное
    /// право убрать, и жаловаться на это было бы ложной тревогой каждый день.
    /// </summary>
    public static void ActivityAlive(bool alive)
    {
        _activityAlive = alive;
        UpdateMarker();
    }

    public static void ServiceAlive(bool alive)
    {
        _serviceAlive = alive;
        UpdateMarker();
    }

    /// <summary>Собрать по требованию — кнопкой из настроек, когда что-то ведёт себя странно.</summary>
    public static string CollectOnDemand()
    {
        Collect("собрано вручную");
        return Path;
    }

    /// <summary>
    /// Вызывается напрямую из глобальных обработчиков необработанных исключений (план 11 §1.1):
    /// <c>AndroidEnvironment.UnhandledExceptionRaiser</c>, <c>AppDomain.UnhandledException</c>,
    /// <c>TaskScheduler.UnobservedTaskException</c>. Это единственный момент, когда ещё можно
    /// синхронно записать что-то в свои файлы до того, как процесс уйдёт, — и единственная причина
    /// эту запись делать: хвост системного буфера объясняет, что упало, но не что в этот момент
    /// делало приложение (какое колесо, шла ли запись), а именно этих полей не хватило при разборе
    /// падений 28.07.2026. Сама запись — не попытка что-то спасти: обработчик, вызвавший это,
    /// обязан дать исключению упасть дальше, а не проглотить его.
    /// </summary>
    public static void CollectCrash(string source, Exception exception, string context)
    {
        // Сначала в ленту журнала: diagnostics.log — эпизод, а журнал — непрерывная лента, и
        // падение обязано стоять в ней на своём месте между обычными строками.
        FileLog.Fatal(source, exception);
        Collect($"падение: {source}", $"{exception}{Environment.NewLine}{context}");
    }

    private static void UpdateMarker()
    {
        try
        {
            if (_activityAlive || _serviceAlive)
            {
                Directory.CreateDirectory(RideFiles.Root);
                File.WriteAllText(MarkerPath, DateTimeOffset.Now.ToString("O"));
            }
            else
            {
                File.Delete(MarkerPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Метка — удобство диагностики. Уронить из-за неё приложение было бы смешно.
        }
    }

    private static void Collect(string reason, string? extra = null, DateTimeOffset? since = null)
    {
        lock (Gate)
        {
            try
            {
                Rotate();
                using var file = new StreamWriter(Path, append: true,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                file.WriteLine();
                file.WriteLine($"===== {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} · {reason} =====");
                file.WriteLine(Describe());
                if (extra is not null)
                {
                    file.WriteLine("----- исключение и контекст приложения -----");
                    file.WriteLine(extra);
                }
                var from = since ?? DateTimeOffset.Now.AddMinutes(-WindowMinutes);

                file.WriteLine("----- журнал приложения -----");
                file.WriteLine(ReadJournal(from));
                string bleFrames = BleFrames?.Invoke() ?? "";
                if (bleFrames.Length > 0) file.Write(bleFrames);
                file.WriteLine("----- системный буфер, только свой uid -----");
                file.WriteLine(ReadOwnBuffer(from));
                file.Flush();
            }
            catch (Exception ex)
            {
                // Средство разбора поломок, которое само роняет приложение, — худшее из возможных.
                TryNote(ex);
            }
        }
    }

    /// <summary>
    /// Что за телефон и что за сборка. Без этого чужой файл читается вслепую — а кнопка заведена
    /// ровно для чужих файлов.
    /// <para>
    /// MAUI отдавал версию через <c>AppInfo.Current</c>; здесь тот же ответ даёт
    /// <c>PackageManager</c> — тот же <c>versionName</c>/<c>versionCode</c>, только через
    /// платформенный API напрямую (опись §1.2).
    /// </para>
    /// </summary>
    private static string Describe()
    {
        var context = Android.App.Application.Context;
        var package = context.PackageManager?.GetPackageInfo(context.PackageName!, 0);
        string version = package?.VersionName ?? "?";
        // LongVersionCode wants API 28 — minSdk уже 28, отдельной проверки версии не нужно.
        long build = package?.LongVersionCode ?? 0;

        return $"устройство {Android.OS.Build.Manufacturer} {Android.OS.Build.Model}, Android {Android.OS.Build.VERSION.Release}" +
               $"{Environment.NewLine}приложение {version} ({build})";
    }

    /// <summary>
    /// Окно собственного журнала — обоих поколений, если ротация пришлась на окно. Отметка
    /// времени в строке («yyyy-MM-dd HH:mm:ss.fff») лексикографически сравнима, поэтому фильтр —
    /// сравнение строк, без разбора дат. Строки без отметки (продолжения исключений) следуют
    /// судьбе своей начальной строки.
    /// </summary>
    private static string ReadJournal(DateTimeOffset from)
    {
        string cutoff = from.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

        var kept = new List<string>();
        bool including = false;
        foreach (string path in (string[])[FileLog.PreviousPath, FileLog.Path])
        {
            try
            {
                if (!File.Exists(path)) continue;

                foreach (string line in File.ReadLines(path))
                {
                    if (line.Length >= cutoff.Length && char.IsAsciiDigit(line[0]))
                    {
                        including = string.CompareOrdinal(line, 0, cutoff, 0, cutoff.Length) >= 0;
                    }

                    if (including) kept.Add(line);
                }
            }
            catch (IOException)
            {
                // Журнал пишется параллельно этой читке; что успели прочитать — то и отдаём.
            }
        }

        return kept.Count == 0
            ? "(журнал за окно пуст)"
            : string.Join(Environment.NewLine, kept.TakeLast(Lines));
    }

    /// <summary>
    /// Хвост системного журнала — своего uid, чужого Android не отдаёт. Окно задаётся **временем, а
    /// не числом строк** (план 11 §4.1): тысяча строк — это пять секунд при шторме и полчаса в
    /// покое, то есть мера, которая коротка ровно тогда, когда нужнее всего.
    /// <para>
    /// Строки-шум выбрасываются здесь, а не фильтром logcat: имена шумных тегов зависят от
    /// устройства (у ART тег — обрезанное имя процесса, у эмулятора свой графический поток), и
    /// список тегов, собранный по одному телефону, на другом промолчал бы. Проверено на убийстве в
    /// фоне 30.07.2026: из тысячи строк 932 были кадровой статистикой эмулятора и сборкой мусора,
    /// а наших — десять.
    /// </para>
    /// </summary>
    private static string ReadOwnBuffer(DateTimeOffset from)
    {
        string since = from.LocalDateTime.ToString("MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "logcat",
            // threadtime добавляет pid/tid: по ним видно, где кончился прошлый процесс и начался этот.
            // Буфер crash — отдельный от main, и в нём лежит то, ради чего файл вообще собирается.
            Arguments = $"-d -v threadtime -b main -b crash -t \"{since}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        });
        if (process is null) return "(logcat не запустился)";

        string text = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);

        var kept = text.Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line) && !IsNoise(line))
            .TakeLast(Lines)
            .ToArray();

        // Пустота — не только вытеснение: Huawei выключает журналирование приложений на уровне
        // прошивки, и logcat отдаёт ноль строк всегда (полевой факт 31.07.2026, PLR-L29).
        return kept.Length == 0
            ? "(буфер пуст — вытеснен либо прошивка не отдаёт журнал приложениям)"
            : string.Join(Environment.NewLine, kept);
    }

    /// <summary>
    /// Строки, которые в разборе никогда не пригождались и вытесняют те, что пригождаются:
    /// кадровая статистика графики и сборка мусора. Всё остальное — включая незнакомые теги
    /// системы — остаётся: именно строка среды выполнения однажды объяснила падение на эмуляторе.
    /// </summary>
    private static bool IsNoise(string line) =>
        line.Contains("EGL_emulation", StringComparison.Ordinal)
        || line.Contains("GC freed", StringComparison.Ordinal)
        || line.Contains("app_time_stats", StringComparison.Ordinal);

    private static void Rotate()
    {
        var file = new FileInfo(Path);
        if (!file.Exists || file.Length < MaxBytes) return;

        string previous = Path + ".1";
        File.Delete(previous);
        File.Move(Path, previous);
    }

    private static void TryNote(Exception ex)
    {
        try
        {
            File.AppendAllText(Path, $"{Environment.NewLine}(сбор не удался: {ex.Message}){Environment.NewLine}");
        }
        catch (Exception nested) when (nested is IOException or UnauthorizedAccessException)
        {
        }
    }
}
