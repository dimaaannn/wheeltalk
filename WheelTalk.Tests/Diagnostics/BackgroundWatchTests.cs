using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
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
/// </summary>
public class BackgroundWatchTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wheeltalk-beat-" + Guid.NewGuid().ToString("N"));

    private string BeatFile => System.IO.Path.Combine(_folder, "background.beat");

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
        var killed = new BackgroundWatch(BeatFile, states, time, new CapturingLogger<BackgroundWatch>());
        states.OnNext(ConnectionState.Reconnecting);
        time.Advance(BackgroundBeat.Period);
        killed.Dispose();

        // Так выглядит смерть процесса: отметка на диске осталась, штатной остановки не было.
        Assert.True(File.Exists(BeatFile));

        time.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(37));
        var logger = new CapturingLogger<BackgroundWatch>();
        using var restarted = new BackgroundWatch(
            BeatFile, new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected), time, logger);

        var gap = restarted.TakeGap();

        Assert.NotNull(gap);
        Assert.Equal(157, (int)gap.Value.TotalMinutes);
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

        using var watch = new BackgroundWatch(BeatFile, states, time, logger);
        states.OnNext(ConnectionState.Reconnecting);

        time.Sleep(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(36));

        var gap = watch.TakeGap();

        Assert.NotNull(gap);
        Assert.Equal(156, (int)gap.Value.TotalMinutes);
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

        var stopped = new BackgroundWatch(BeatFile, states, time, new CapturingLogger<BackgroundWatch>());
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
            BeatFile, new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected), time, logger);

        Assert.Null(restarted.TakeGap());
        Assert.Empty(logger.Entries);
    }

    /// <summary>(в) Без работы нет и сердцебиения: отключённая сессия на диск не пишет вовсе.</summary>
    [Fact]
    public void Nothing_is_written_while_the_session_is_disconnected()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 13, 20, 0, 0, TimeSpan.Zero));
        var states = new BehaviorSubject<ConnectionState>(ConnectionState.Disconnected);

        using var watch = new BackgroundWatch(BeatFile, states, time, new CapturingLogger<BackgroundWatch>());

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

        using var watch = new BackgroundWatch(BeatFile, states, time, new CapturingLogger<BackgroundWatch>());
        states.OnNext(ConnectionState.Connected);

        time.Sleep(TimeSpan.FromMinutes(2));

        Assert.Null(watch.TakeGap());
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

        string guard = body.Split('\n').First(line => line.Contains("TakeGap()"));

        Assert.Contains("CrashReport.PreviousRunCrashed", guard);
        Assert.Contains("return;", guard);
    }
}
