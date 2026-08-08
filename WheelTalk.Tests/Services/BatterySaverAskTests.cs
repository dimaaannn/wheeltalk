using WheelTalk.Core.Services;

namespace WheelTalk.Tests.Services;

/// <summary>
/// Просьба об исключении из экономии заряда (bugfix 3 §3.1). Оригинал спрашивает на каждом запуске,
/// пока система не считает исключение выданным — на части прошивок это не проходит никогда, и
/// приложение выпрашивало бы разрешение вечно. Решение владельца: спросить три раза, дальше молчать.
/// </summary>
public class BatterySaverAskTests
{
    [Fact]
    public void An_already_granted_exception_is_never_asked_for_and_the_counter_stands_still()
    {
        Assert.Equal(new BatterySaverAskDecision(false, 0), Decide(isIgnoring: true, asksSoFar: 0));
        Assert.Equal(new BatterySaverAskDecision(false, 2), Decide(isIgnoring: true, asksSoFar: 2));
    }

    [Fact]
    public void A_disabled_toggle_is_never_asked_for()
    {
        Assert.Equal(new BatterySaverAskDecision(false, 0),
            BatterySaverAsk.Decide(warnEnabled: false, isIgnoringOptimizations: false, asksSoFar: 0));
    }

    /// <summary>Без исключения — ровно три попытки подряд, четвёртая уже тихая.</summary>
    [Fact]
    public void Without_the_exception_it_asks_exactly_three_times_then_falls_silent()
    {
        int asksSoFar = 0;

        for (int expectedCount = 1; expectedCount <= BatterySaverAsk.MaxAsks; expectedCount++)
        {
            var decision = Decide(isIgnoring: false, asksSoFar);
            Assert.True(decision.ShouldAsk);
            Assert.Equal(expectedCount, decision.NextAskCount);
            asksSoFar = decision.NextAskCount;
        }

        var fourth = Decide(isIgnoring: false, asksSoFar);
        Assert.False(fourth.ShouldAsk);
        Assert.Equal(BatterySaverAsk.MaxAsks, fourth.NextAskCount);
    }

    /// <summary>
    /// Иначе тумблер стал бы мёртвым переключателем после третьего раза: выключить можно, попросить
    /// снова нечем.
    /// </summary>
    [Fact]
    public void Turning_the_toggle_off_and_on_again_earns_three_more_asks()
    {
        var maxedOut = Decide(isIgnoring: false, asksSoFar: BatterySaverAsk.MaxAsks);
        Assert.False(maxedOut.ShouldAsk);

        var turnedOff = BatterySaverAsk.Decide(warnEnabled: false, isIgnoringOptimizations: false, maxedOut.NextAskCount);
        Assert.Equal(0, turnedOff.NextAskCount);

        var firstAfterReEnable = Decide(isIgnoring: false, turnedOff.NextAskCount);
        Assert.True(firstAfterReEnable.ShouldAsk);
        Assert.Equal(1, firstAfterReEnable.NextAskCount);
    }

    private static BatterySaverAskDecision Decide(bool isIgnoring, int asksSoFar) =>
        BatterySaverAsk.Decide(warnEnabled: true, isIgnoringOptimizations: isIgnoring, asksSoFar);
}
