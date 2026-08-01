using WheelTalk.Core.Alerts;

namespace WheelTalk.Tests.Alerts;

/// <summary>
/// Ритм тревоги по ШИМ. Пропорции сняты с оригинала (<c>Alarms.kt</c>: период 200 мс, длина тона
/// 20…200 мс) и проверяются здесь на то единственное, ради чего они такие: сплошной звук на
/// потолке получается сам, из тех же двух чисел, а не отдельным режимом.
/// <para>
/// Своя версия была сложнее и звучала хуже. Длина сигнала и тишина считались порознь, поэтому на
/// потолке между сигналами оставался зазор, сплошной режим включался отдельно, а на границе
/// дребезжал — и его приходилось удерживать гистерезисом. Ничего этого больше нет.
/// </para>
/// </summary>
public class AlertRhythmTests
{
    /// <summary>
    /// То, из-за чего всё переписано: на потолке сигнал занимает период целиком, тишине взяться
    /// неоткуда, и никакого «включить сплошной режим» для этого не требуется.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.2)]   // расчётный ШИМ у Gotway доходит до 110 % — интенсивность выходит за единицу
    [InlineData(2.0)]
    public void At_the_full_alarm_threshold_the_tone_fills_the_whole_period(double intensity)
    {
        Assert.Equal(AlertRhythm.Period, AlertRhythm.ToneLength(intensity));

        // Значит звучит в любой момент периода, включая самый его конец.
        Assert.True(AlertRhythm.IsSounding(AlertRhythm.Period - TimeSpan.FromMilliseconds(1), intensity));
    }

    /// <summary>А ниже — именно писк с тишиной, иначе «почти» и «уже» звучали бы одинаково.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void Below_it_the_tone_leaves_audible_silence(double intensity)
    {
        var tone = AlertRhythm.ToneLength(intensity);

        Assert.True(tone < AlertRhythm.Period);
        Assert.True(AlertRhythm.Period - tone >= TimeSpan.FromMilliseconds(15));
        Assert.False(AlertRhythm.IsSounding(AlertRhythm.Period - TimeSpan.FromMilliseconds(1), intensity));
    }

    /// <summary>Приближение к пределу слышно как удлинение сигнала в неизменной сетке.</summary>
    [Fact]
    public void The_closer_to_the_limit_the_longer_the_tone()
    {
        var previous = AlertRhythm.ToneLength(0);

        for (double intensity = 0.1; intensity <= 1.0; intensity += 0.1)
        {
            var tone = AlertRhythm.ToneLength(intensity);
            Assert.True(tone > previous, $"на {intensity:F1} сигнал не удлинился");
            previous = tone;
        }
    }

    [Fact]
    public void At_the_threshold_itself_it_is_a_short_beep_in_a_slow_grid()
    {
        Assert.Equal(AlertRhythm.ShortestTone, AlertRhythm.ToneLength(0));
        Assert.True(AlertRhythm.ShortestTone < AlertRhythm.Period / 5);
    }

    [Fact]
    public void The_period_is_over_when_it_is_over_and_not_before()
    {
        Assert.False(AlertRhythm.IsPeriodOver(AlertRhythm.Period - TimeSpan.FromMilliseconds(1)));
        Assert.True(AlertRhythm.IsPeriodOver(AlertRhythm.Period));
    }
}
