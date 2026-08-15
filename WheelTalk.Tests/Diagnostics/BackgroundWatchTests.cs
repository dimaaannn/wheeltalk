using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Diagnostics;
using WheelTalk.Core.Services;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Diagnostics;

/// <summary>
/// Замки «фон остановлен системой» (заказ владельца 14.08.2026). Случай, ради которого всё
/// заведено: 13.08.2026 EMUI заморозил процесс посреди погони — последний кадр BLE в 20:36:37
/// оборван на середине, следующий в 23:13:04, — и приложение не сказало об этом ни слова. Прежняя
/// метка <c>running.marker</c> этого и не могла: она спрашивается только при создании процесса, а
/// процесс не умирал.
/// <para>
/// Отсюда два случая в замках, и они разные: <b>убитый</b> процесс (отметка осталась на диске,
/// читает её следующий запуск) и <b>замороженный</b> (отметка осталась в памяти, читает её тот же
/// процесс на возвращении экрана). Второй проверяется <see cref="SleepyTimeProvider"/> — часы
/// уходят вперёд, тиков нет вовсе.
/// </para>
/// <para>
/// Замки (ж), (з), (и) — вторая правда, добытая разбором полной ленты 15.08.2026: мерить надо
/// простой <b>работы</b>, а не простой своего таймера. Оттаивание без работы перерыв не закрывает,
/// иначе числа занижены (13 минут вместо 29) и слитная заморозка разваливается на куски (74 минуты
/// пятью обрывками).
/// </para>
/// </summary>
public class BackgroundWatchTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wheeltalk-beat-" + Guid.NewGuid().ToString("N"));

    /// <summary>Кадры с колеса — единственный признак того, что работа и вправду идёт.</summary>
    private readonly Subject<TelemetrySnapshot> _frames = new();

    private string BeatFile => System.IO.Path.Combine(_folder, "background.beat");

    private void Frame() => _frames.OnNext(new TelemetrySnapshot());

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    /// <summary>(а) Процесс пропал посреди погони — следующий запуск говорит об этом и словом, и строкой журнала.</summary>
    [Fact]
    public void A_run_that_vanished_while_chasing_is_reported_at_the_next_start()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 13, 20, 35, 0, TimeSpan.Zero));
        var states = new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected);

        // Процесс с незаконченной работой: связь оборвалась, идёт погоня.
        var killed = new BackgroundWatch(BeatFile, states, _frames, time, new CapturingLogger<BackgroundWatch>());
        states.OnNext(ConnectionState.Reconnecting);
        time.Advance(BackgroundBeat.Period);
        killed.Dispose();

        // Так выглядит смерть процесса: отметка на диске осталась, штатной остановки не было.
        Assert.True(File.Exists(BeatFile));

        time.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(37));
        var logger = new CapturingLogger<BackgroundWatch>();
        using var restarted = new BackgroundWatch(
            BeatFile, new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected), _frames, time, logger);

        var gap = restarted.TakeGap();

        Assert.NotNull(gap);
        Assert.Equal(157, (int)gap.Value.Missed.TotalMinutes);

        // Момент обнаружения — миг рестарта: показ бывает часами позже, и без этой метки сообщение
        // не привязать к журналу (слово владельца 15.08.2026).
        Assert.Equal(time.GetUtcNow(), gap.Value.NoticedAt);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("Background.Stopped"));

        // Сказано один раз: второй экран того же запуска повторять это не станет.
        Assert.Null(restarted.TakeGap());
    }

    /// <summary>
    /// (а') Тот же случай, но процесс выжил — путь владельца. Часы ушли на два с половиной часа,
    /// тиков не было ни одного, и заметить это обязано само возвращение экрана.
    /// </summary>
    [Fact]
    public void A_frozen_process_notices_the_gap_when_the_screen_comes_back()
    {
        var time = new SleepyTimeProvider();
        var states = new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected);
        var logger = new CapturingLogger<BackgroundWatch>();

        using var watch = new BackgroundWatch(BeatFile, states, _frames, time, logger);
        states.OnNext(ConnectionState.Reconnecting);

        time.Sleep(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(36));

        var gap = watch.TakeGap();

        Assert.NotNull(gap);
        Assert.Equal(156, (int)gap.Value.Missed.TotalMinutes);
        Assert.Equal(time.GetUtcNow(), gap.Value.NoticedAt);
        Assert.Single(logger.Entries, entry => entry.Message.Contains("Background.Stopped"));

        // Оттаявший таймер приходит запоздалым тиком — и второй раз про тот же перерыв не пишет.
        time.Tick();
        Assert.Null(watch.TakeGap());
        Assert.Single(logger.Entries, entry => entry.Message.Contains("Background.Stopped"));
    }

    /// <summary>(б) Штатная остановка — тишина: отметка снята, следующему запуску сказать нечего.</summary>
    [Fact]
    public void A_clean_disconnect_leaves_nothing_to_report()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 13, 20, 0, 0, TimeSpan.Zero));
        var states = new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected);

        var stopped = new BackgroundWatch(BeatFile, states, _frames, time, new CapturingLogger<BackgroundWatch>());
        states.OnNext(ConnectionState.Connected);
        time.Advance(BackgroundBeat.Period);
        Assert.True(File.Exists(BeatFile));

        states.OnNext(ConnectionState.Disconnected);
        Assert.False(File.Exists(BeatFile));

        // (в) И больше не появится: без работы сердцебиения нет — таймер снят вместе с ней.
        time.Advance(BackgroundBeat.Period * 3);
        Assert.False(File.Exists(BeatFile));
        stopped.Dispose();

        time.Advance(TimeSpan.FromHours(3));
        var logger = new CapturingLogger<BackgroundWatch>();
        using var restarted = new BackgroundWatch(
            BeatFile, new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected), _frames, time, logger);

        Assert.Null(restarted.TakeGap());
        Assert.Empty(logger.Entries);
    }

    /// <summary>(в) Без работы нет и сердцебиения: отключённая сессия на диск не пишет вовсе.</summary>
    [Fact]
    public void Nothing_is_written_while_the_session_is_disconnected()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 13, 20, 0, 0, TimeSpan.Zero));
        var states = new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected);

        using var watch = new BackgroundWatch(BeatFile, states, _frames, time, new CapturingLogger<BackgroundWatch>());

        time.Advance(BackgroundBeat.Period * 10);

        Assert.False(File.Exists(BeatFile));
    }

    /// <summary>
    /// (д) Заминка — не перерыв. Мера пропуска стоит на пяти минутах, и о меньшем человеку не
    /// говорят: иначе сообщение приходило бы на каждый сон экрана и перестало бы значить хоть что-то.
    /// </summary>
    [Fact]
    public void A_short_hiccup_says_nothing()
    {
        var time = new SleepyTimeProvider();
        var states = new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected);

        using var watch = new BackgroundWatch(BeatFile, states, _frames, time, new CapturingLogger<BackgroundWatch>());
        states.OnNext(ConnectionState.Connected);

        time.Sleep(TimeSpan.FromMinutes(2));

        Assert.Null(watch.TakeGap());
    }

    /// <summary>
    /// (ж) Слитная заморозка не разваливается на куски. EMUI отпускает процесс на минуту-другую и
    /// морозит снова — работа при этом не возобновляется: колесо говорит, а слушать по-прежнему
    /// некому. Прежняя мера считала такой тик концом перерыва, и 74 минуты простоя легли в журнал
    /// 15.08.2026 пятью обрывками. Теперь перерыв закрывает кадр с колеса, и сообщение выходит одно
    /// — с полной длительностью.
    /// </summary>
    [Fact]
    public void One_freeze_broken_by_a_brief_thaw_is_still_one_stop()
    {
        var time = new SleepyTimeProvider();
        var states = new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected);
        var logger = new CapturingLogger<BackgroundWatch>();

        using var watch = new BackgroundWatch(BeatFile, states, _frames, time, logger);
        states.OnNext(ConnectionState.Connected);
        var frozenAt = time.GetUtcNow();

        time.Sleep(TimeSpan.FromMinutes(40));

        // Оттаяло на минуту: два тика подряд — и снова заморозка. Кадров за эту минуту не было.
        time.Tick();
        time.Sleep(TimeSpan.FromMinutes(1));
        time.Tick();

        time.Sleep(TimeSpan.FromMinutes(33));

        // А вот теперь работа и вправду возобновилась.
        Frame();

        var gap = watch.TakeGap();

        Assert.NotNull(gap);
        Assert.Equal(74, (int)gap.Value.Missed.TotalMinutes);
        Assert.Equal(frozenAt + TimeSpan.FromMinutes(74), gap.Value.NoticedAt);
        Assert.Single(logger.Entries, entry => entry.Message.Contains("стояла 74 мин"));
    }

    /// <summary>
    /// (з) Считается простой <b>работы</b>, а не простой таймера. Разница между ними и есть та ложь,
    /// из-за которой 15.08.2026 в журнале стояло 13 минут вместо двадцати девяти: процесс оттаял и
    /// тикнул, но кадры с колеса пошли только через четверть часа — и все эти минуты приложение с
    /// колесом не разговаривало.
    /// </summary>
    [Fact]
    public void The_number_is_the_idle_time_of_work_not_of_the_timer()
    {
        var time = new SleepyTimeProvider();
        var states = new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected);
        var logger = new CapturingLogger<BackgroundWatch>();

        using var watch = new BackgroundWatch(BeatFile, states, _frames, time, logger);
        states.OnNext(ConnectionState.Connected);
        var frozenAt = time.GetUtcNow();

        time.Sleep(TimeSpan.FromMinutes(13));
        time.Tick();

        time.Sleep(TimeSpan.FromMinutes(15));
        Frame();

        var gap = watch.TakeGap();

        Assert.NotNull(gap);
        Assert.Equal(28, (int)gap.Value.Missed.TotalMinutes);
        Assert.Equal(frozenAt + TimeSpan.FromMinutes(28), gap.Value.NoticedAt);
        Assert.Single(logger.Entries, entry => entry.Message.Contains("стояла 28 мин"));
    }

    /// <summary>
    /// (и) Выключенное колесо не выдумывает простоя. Кадров нет и не будет — райдер выключил колесо
    /// и забыл отключиться, — но процесс жив и гоняется за ним как ни в чём не бывало. Растить
    /// перерыв на этом значило бы соврать в другую сторону: к утру в кармане набежало бы восемь
    /// часов «остановленного фона».
    /// <para>
    /// Мера доказательства — своё же ровное сердцебиение: <see cref="BackgroundBeat.Missed"/> тиков
    /// в срок подряд бывают только у работающего. Двадцать минут настоящей заморозки при этом
    /// названы полностью, и концом их назван миг оттаивания, а не та минута, на которой процесс
    /// это доказал.
    /// </para>
    /// </summary>
    [Fact]
    public void A_wheel_that_is_simply_off_grows_no_phantom_stop()
    {
        var time = new SleepyTimeProvider();
        var states = new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected);
        var logger = new CapturingLogger<BackgroundWatch>();

        using var watch = new BackgroundWatch(BeatFile, states, _frames, time, logger);
        states.OnNext(ConnectionState.Reconnecting);
        var frozenAt = time.GetUtcNow();

        time.Sleep(TimeSpan.FromMinutes(20));

        // Отпустили насовсем: сердцебиение идёт минута в минуту полтора часа. Кадров нет ни одного.
        for (int minute = 0; minute < 90; minute++)
        {
            time.Sleep(TimeSpan.FromMinutes(1));
            time.Tick();
        }

        var gap = watch.TakeGap();

        Assert.NotNull(gap);
        Assert.Equal(21, (int)gap.Value.Missed.TotalMinutes);
        Assert.Equal(frozenAt + TimeSpan.FromMinutes(21), gap.Value.NoticedAt);
        Assert.Single(logger.Entries, entry => entry.Message.Contains("Background.Stopped"));
    }

    /// <summary>Отметка читается такой же, какой записана, — иначе следующий запуск судит по мусору.</summary>
    [Fact]
    public void A_beat_survives_the_round_trip_and_a_broken_one_is_ignored()
    {
        var beat = new BackgroundBeat(
            new DateTimeOffset(2026, 8, 13, 20, 36, 37, TimeSpan.FromHours(3)), ConnectionState.Reconnecting);

        Assert.Equal(beat, BackgroundBeat.Parse(beat.Format()));
        Assert.Null(BackgroundBeat.Parse(beat.Format()[..12]));
        Assert.Null(BackgroundBeat.Parse(""));
        Assert.Null(BackgroundBeat.Parse(null));
    }

    /// <summary>
    /// (г) Краш уже говорит своим диалогом — второго окна об одном и том же не будет. Показ живёт в
    /// android-проекте, тестам не видном: замок читает боевой исходник текстом, как
    /// <c>CrashPromptTests</c>.
    /// </summary>
    [Fact]
    public void The_notice_yields_to_the_crash_prompt_but_still_takes_the_gap()
    {
        string body = RepoFiles.MethodBody(
            RepoFiles.Read("WheelTalk.Droid/Main/MainActivity.cs"), "private void TellAboutStoppedBackground()");

        // Подавление стоит ПОСЛЕ забора пропуска (пропуск не должен всплыть позже) и одноразовое:
        // статичный флаг краха ночью 14→15.08 съел семь показов подряд — потратив один, гаснет.
        Assert.Contains("CrashReport.PreviousRunCrashed && !s_crashHushSpent", body);
        Assert.Contains("s_crashHushSpent = true;", body);
        Assert.Contains("return;", body);
    }

    /// <summary>
    /// (е) Показ — тоже строка журнала (слово владельца 15.08.2026): обнаружение и показ разнесены
    /// часами, и без строки показа разбор гадает, видел ли человек сообщение. В баннер при этом
    /// едет момент обнаружения — вторым аргументом формата.
    /// </summary>
    [Fact]
    public void The_telling_itself_is_logged_and_carries_the_moment_it_was_noticed()
    {
        string body = RepoFiles.MethodBody(
            RepoFiles.Read("WheelTalk.Droid/Main/MainActivity.cs"), "private void TellAboutStoppedBackground()");

        Assert.Contains("Background.Told", body);
        Assert.Contains("told.NoticedAt.LocalDateTime", body);

        Assert.Contains("замечено {1:HH:mm}",
            RepoFiles.Read("WheelTalk.Droid/Resources/Strings/AppStrings.resx"));
    }
}
