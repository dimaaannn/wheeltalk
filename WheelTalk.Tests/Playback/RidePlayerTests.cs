using Microsoft.Extensions.Time.Testing;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Playback;

namespace WheelTalk.Tests.Playback;

/// <summary>
/// Проигрыватель записанной поездки: ход по часам, пауза, перемотка, скорость. Проверяется на
/// виртуальном времени — настоящее ожидание сделало бы тест и медленным, и мигающим.
/// </summary>
public class RidePlayerTests
{
    /// <summary>Начало «записи» по часам — от него считаются настенные метки отсчётов.</summary>
    private static readonly DateTimeOffset Start = new(2026, 7, 28, 17, 24, 0, TimeSpan.FromHours(3));

    /// <summary>Пять отсчётов в секунду, как пишет настоящая запись; скорость = номеру отсчёта.</summary>
    private static IReadOnlyList<RideSample> Ride(int seconds = 10) =>
        Enumerable.Range(0, seconds * 5)
            .Select(i => new RideSample(TimeSpan.FromMilliseconds(i * 200),
                Start.AddMilliseconds(i * 200),
                new TelemetrySnapshot { SpeedRaw = i }))
            .ToList();

    [Fact]
    public void A_paused_player_stands_still()
    {
        var time = new FakeTimeProvider();
        using var player = new RidePlayer(Ride(), time);
        var seen = new List<int>();
        using var _ = player.Telemetry.Subscribe(s => seen.Add(s.SpeedRaw));

        time.Advance(TimeSpan.FromSeconds(3));

        Assert.Empty(seen);
        Assert.Equal(TimeSpan.Zero, player.Position);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void Playing_hands_out_samples_in_order_as_their_time_comes()
    {
        var time = new FakeTimeProvider();
        using var player = new RidePlayer(Ride(), time);
        var seen = new List<int>();
        using var _ = player.Telemetry.Subscribe(s => seen.Add(s.SpeedRaw));

        player.Play();
        time.Advance(TimeSpan.FromSeconds(1));

        // Секунда записи — отсчёты с 0 по 1000 мс включительно, то есть шесть, а не пять:
        // граница отрезка своя точка тоже отдаёт.
        Assert.Equal([0, 1, 2, 3, 4, 5], seen);
        Assert.True(player.IsPlaying);
    }

    [Fact]
    public void Double_speed_covers_twice_the_ride_in_the_same_wall_clock()
    {
        var time = new FakeTimeProvider();
        using var player = new RidePlayer(Ride(), time) { Speed = 2 };
        var seen = new List<int>();
        using var _ = player.Telemetry.Subscribe(s => seen.Add(s.SpeedRaw));

        player.Play();
        time.Advance(TimeSpan.FromSeconds(1));

        // Ни один отсчёт не пропущен — на ускоренном ходу их за тик несколько, и след поездки
        // строится по каждому.
        Assert.Equal(Enumerable.Range(0, 11), seen);
        Assert.Equal(TimeSpan.FromSeconds(2), player.Position);
    }

    [Fact]
    public void Seeking_shows_the_frame_it_landed_on_even_while_paused()
    {
        var time = new FakeTimeProvider();
        using var player = new RidePlayer(Ride(), time);
        var seen = new List<int>();
        using var _ = player.Telemetry.Subscribe(s => seen.Add(s.SpeedRaw));

        player.Seek(TimeSpan.FromSeconds(4));

        Assert.Equal([20], seen); // отсчёт ровно на 4-й секунде — последний, стоящий не позже места перемотки
        Assert.Equal(TimeSpan.FromSeconds(4), player.Position);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void Playing_on_after_a_seek_continues_from_there_without_replaying_the_past()
    {
        var time = new FakeTimeProvider();
        using var player = new RidePlayer(Ride(), time);
        player.Seek(TimeSpan.FromSeconds(4));

        var seen = new List<int>();
        using var _ = player.Telemetry.Subscribe(s => seen.Add(s.SpeedRaw));

        player.Play();
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal([21, 22, 23, 24, 25], seen);
    }

    [Fact]
    public void The_end_of_the_ride_stops_the_player_rather_than_running_past_it()
    {
        var time = new FakeTimeProvider();
        var ride = Ride(seconds: 2);
        using var player = new RidePlayer(ride, time);
        var seen = new List<int>();
        using var _ = player.Telemetry.Subscribe(s => seen.Add(s.SpeedRaw));

        player.Play();
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(ride.Count, seen.Count);
        Assert.False(player.IsPlaying);
        Assert.Equal(player.Duration, player.Position);
    }

    [Fact]
    public void Playing_from_the_end_starts_the_ride_over()
    {
        var time = new FakeTimeProvider();
        using var player = new RidePlayer(Ride(seconds: 2), time);
        player.Seek(player.Duration);

        var seen = new List<int>();
        using var _ = player.Telemetry.Subscribe(s => seen.Add(s.SpeedRaw));

        player.Play();
        time.Advance(TimeSpan.FromMilliseconds(400));

        Assert.Equal([0, 1, 2], seen);
    }

    [Fact]
    public void The_shown_frame_carries_the_wall_clock_of_when_it_was_recorded()
    {
        var time = new FakeTimeProvider();
        using var player = new RidePlayer(Ride(), time);

        player.Seek(TimeSpan.FromSeconds(4));

        // Четвёртая секунда записи — это 17:24:04 того дня, а не четвёртая секунда «вообще»:
        // ради этого ответа запись и открывают (docs/playback-plan.md §1).
        Assert.Equal(Start.AddSeconds(4), player.Current!.Stamp);
    }

    [Fact]
    public void A_seek_announces_the_jump_so_accumulated_marks_can_start_over()
    {
        var time = new FakeTimeProvider();
        using var player = new RidePlayer(Ride(), time);
        int jumps = 0;
        player.Jumped += () => jumps++;

        player.Play();
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, jumps); // обычный ход скачком не считается

        player.Seek(TimeSpan.FromSeconds(20));
        Assert.Equal(1, jumps);
    }

    [Fact]
    public void An_empty_ride_plays_without_throwing()
    {
        var time = new FakeTimeProvider();
        using var player = new RidePlayer([], time);

        player.Play();
        time.Advance(TimeSpan.FromSeconds(1));
        player.Seek(TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.Zero, player.Duration);
    }
}
