using WheelTalk.Core.Services;

namespace WheelTalk.Tests.Services;

/// <summary>
/// Тап по плашке связи (bugfix 2 §2.1). Раньше решала не та проверка — по сырому
/// <c>ConnectionState.Connected</c>, который совпадает и с <see cref="WheelLink.NoData"/>, — и тап в
/// «нет данных» умирал первой строкой. Здесь тесты держат решение на посчитанной фазе.
/// </summary>
public class LinkBadgeTapTests
{
    [Fact]
    public void Fresh_connection_ignores_the_tap()
    {
        Assert.Equal(LinkBadgeTapAction.None, Decide(WheelLink.Connected));
    }

    /// <summary>Суть бага: NoData — это тоже Connected по ConnectionState, но тап обязан вести в поиск.</summary>
    [Fact]
    public void No_data_sends_to_scan_instead_of_doing_nothing()
    {
        Assert.Equal(LinkBadgeTapAction.GoToScan, Decide(WheelLink.NoData));
    }

    [Fact]
    public void No_data_while_awaiting_a_password_sends_to_settings_instead_of_scan()
    {
        Assert.Equal(LinkBadgeTapAction.GoToSettings, Decide(WheelLink.NoData, awaitingPassword: true));
    }

    /// <summary>Реплей не пишет пароль никуда — окно ввода тут не про пароль колеса вовсе.</summary>
    [Fact]
    public void Awaiting_password_during_a_replay_still_sends_to_scan()
    {
        Assert.Equal(LinkBadgeTapAction.GoToScan,
            Decide(WheelLink.NoData, awaitingPassword: true, isReplay: true));
    }

    [Theory]
    [InlineData(WheelLink.Connecting)]
    [InlineData(WheelLink.Reconnecting)]
    [InlineData(WheelLink.Failed)]
    [InlineData(WheelLink.Idle)]
    public void Every_other_phase_sends_to_scan(WheelLink link)
    {
        Assert.Equal(LinkBadgeTapAction.GoToScan, Decide(link));
    }

    /// <summary>Реплей без сессии — это «Запись готова»: тап пускает воспроизведение, а не сканирует эфир.</summary>
    [Theory]
    [InlineData(WheelLink.Idle)]
    [InlineData(WheelLink.Failed)]
    public void Replay_with_no_session_toggles_playback_instead_of_scanning(WheelLink link)
    {
        Assert.Equal(LinkBadgeTapAction.ToggleReplay, Decide(link, isReplay: true));
    }

    private static LinkBadgeTapAction Decide(WheelLink link, bool awaitingPassword = false, bool isReplay = false) =>
        LinkBadgeTap.Decide(link, awaitingPassword, isReplay);
}
