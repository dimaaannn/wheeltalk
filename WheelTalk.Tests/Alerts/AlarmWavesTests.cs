using WheelTalk.Core.Alerts;

namespace WheelTalk.Tests.Alerts;

/// <summary>
/// Волна тревоги. Проверяется тем, что слышно только на выезде, а ломается молча: NaN в отсчёте,
/// вылет за шкалу, уехавшая громкость.
/// <para>
/// Громкость здесь — не придирка. Варианты отбирались на телефоне выровненными по
/// <see cref="AlarmWaves.TargetRms"/>, и на этом же уровне звучала тревога до выбора звука. Волна,
/// уехавшая по уровню, звучит не тем, что слушал владелец, — а узнаётся это уже на колесе.
/// </para>
/// </summary>
public class AlarmWavesTests
{
    private const int SampleRate = 44100;
    private const int Seconds = 2;

    public static TheoryData<AlarmWave> Waves => [AlarmWave.TwoToneStack, AlarmWave.Stack];

    [Theory]
    [MemberData(nameof(Waves))]
    public void Every_sample_is_a_number_within_the_scale(AlarmWave wave)
    {
        for (double intensity = 0; intensity <= 1; intensity += 0.1)
        {
            foreach (double sample in Samples(wave, intensity))
            {
                Assert.True(double.IsFinite(sample), $"не число на интенсивности {intensity:F1}");
                Assert.InRange(sample, -1, 1);
            }
        }
    }

    /// <summary>На том уровне, на котором варианты слушали, — иначе выбирали одно, а едут с другим.</summary>
    [Theory]
    [MemberData(nameof(Waves))]
    public void At_full_intensity_it_sounds_at_the_level_the_choice_was_made_on(AlarmWave wave)
    {
        var (rms, peak) = Measure(Samples(wave, 1));

        Assert.Equal(AlarmWaves.TargetRms, rms, tolerance: 0.01);
        Assert.InRange(peak, 0.3, AlarmWaves.PeakCeiling);
    }

    /// <summary>
    /// Приближение к пределу слышно как рисунок: у порога сигнал перемежается тишиной, на потолке
    /// её не остаётся. Без этого тревога звучала бы одинаково на 80 и на 99 процентах.
    /// </summary>
    [Theory]
    [MemberData(nameof(Waves))]
    public void The_pattern_thickens_towards_the_limit(AlarmWave wave)
    {
        double atThreshold = Sounding(wave, intensity: 0);
        double atLimit = Sounding(wave, intensity: 1);

        Assert.InRange(atThreshold, 0.05, 0.5);
        Assert.True(atLimit > atThreshold + 0.3, $"рисунок не уплотнился: {atThreshold:P0} → {atLimit:P0}");
    }

    /// <summary>Оба варианта живут в рабочей полосе плана 26 — постоянной составляющей там взяться неоткуда.</summary>
    [Theory]
    [MemberData(nameof(Waves))]
    public void It_carries_no_constant_offset(AlarmWave wave)
    {
        double sum = 0;
        int count = 0;
        foreach (double sample in Samples(wave, 1))
        {
            sum += sample;
            count++;
        }

        Assert.Equal(0, sum / count, tolerance: 0.01);
    }

    private static IEnumerable<double> Samples(AlarmWave wave, double intensity)
    {
        for (int n = 0; n < SampleRate * Seconds; n++)
        {
            yield return AlarmWaves.Sample(wave, n / (double)SampleRate, intensity);
        }
    }

    /// <summary>Доля звучащих отсчётов — то же «звучит или молчит», по которому меряется громкость.</summary>
    private static double Sounding(AlarmWave wave, double intensity) =>
        Samples(wave, intensity).Count(sample => Math.Abs(sample) >= 0.001) / (double)(SampleRate * Seconds);

    /// <summary>Уровень и пик по звучащим отсчётам — тем же правилом, каким волна выравнивалась.</summary>
    private static (double Rms, double Peak) Measure(IEnumerable<double> samples)
    {
        double squares = 0;
        double peak = 0;
        int sounding = 0;

        foreach (double sample in samples)
        {
            double level = Math.Abs(sample);
            if (level < 0.001) continue;

            squares += sample * sample;
            peak = Math.Max(peak, level);
            sounding++;
        }

        return (Math.Sqrt(squares / sounding), peak);
    }
}
