using WheelTalk.Core.Services;

namespace WheelTalk.Tests.Services;

/// <summary>
/// Порог «спросить о причине» (план 11 §3.2) — чистая функция, и проверяется она целиком: вопрос
/// задаётся ровно один раз за погоню. Не раньше — один случайный отказ при живом колесе бывает
/// каждый день; и не каждый следующий раз — двести проверок в час это ровно та работа, которой
/// порог и избегает.
/// </summary>
public class ChaseTroubleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_couple_of_failures_is_an_ordinary_day(int failures)
    {
        Assert.False(ChaseTrouble.ShouldAskWhy(failures));
    }

    [Fact]
    public void At_the_threshold_the_question_is_asked()
    {
        Assert.True(ChaseTrouble.ShouldAskWhy(ChaseTrouble.Threshold));
    }

    [Fact]
    public void Past_the_threshold_it_is_not_asked_again()
    {
        Assert.False(ChaseTrouble.ShouldAskWhy(ChaseTrouble.Threshold + 1));
        Assert.False(ChaseTrouble.ShouldAskWhy(ChaseTrouble.Threshold + 200));
    }
}
