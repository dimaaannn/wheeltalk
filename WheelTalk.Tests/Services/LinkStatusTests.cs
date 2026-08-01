using WheelTalk.Core.Services;

namespace WheelTalk.Tests.Services;

/// <summary>
/// Фазы связи главного экрана. Проверяются здесь потому, что на телефоне ошибка в них выглядит
/// как «плашка врёт» — и обнаруживается на выезде, где ей верят: жёлтая «данных нет» и зелёная
/// «подключено» отличаются одним сравнением, а стоят они райдеру доверия к экрану.
/// <para>
/// Главное, ради чего логика вообще вынесена из Activity (план 14, Б2): связь и свежесть данных —
/// разные вещи. Линк держится, пока колесо молчит, и оба этих случая должны разводиться.
/// </para>
/// </summary>
public class LinkStatusTests
{
    /// <summary>
    /// То, из-за чего фаза <see cref="WheelLink.NoData"/> существует: линк цел, а отсчётов нет.
    /// Показывать в этот момент «подключено» — значит утверждать, что цифры на экране живые.
    /// </summary>
    [Fact]
    public void A_live_link_with_no_frames_is_not_the_same_as_a_working_one()
    {
        Assert.Equal(WheelLink.NoData, Evaluate(ConnectionState.Connected, staleFor: 5));
        Assert.Equal(WheelLink.Connected, Evaluate(ConnectionState.Connected, staleFor: 0.2));
    }

    /// <summary>
    /// Порог строгий: ровно на полутора секундах кадр ещё свеж. Колесо шлёт отсчёты каждые
    /// ~200 мс, так что порог — это семь пропущенных пакетов подряд, а не дрогнувший интервал.
    /// </summary>
    [Theory]
    [InlineData(1.49, WheelLink.Connected)]
    [InlineData(1.5, WheelLink.Connected)]
    [InlineData(1.51, WheelLink.NoData)]
    public void The_freshness_threshold_lets_the_boundary_frame_count_as_fresh(double staleFor, WheelLink expected)
    {
        Assert.Equal(expected, Evaluate(ConnectionState.Connected, staleFor));
        Assert.Equal(expected == WheelLink.NoData, LinkStatus.IsStale(staleFor));
    }

    /// <summary>
    /// Возраст кадра решает только при живом линке. Пока связи нет, старость последнего отсчёта
    /// ничего не добавляет: он и так последний, и плашка говорит про связь, а не про него.
    /// </summary>
    [Theory]
    [InlineData(ConnectionState.Connecting, WheelLink.Connecting)]
    [InlineData(ConnectionState.Reconnecting, WheelLink.Reconnecting)]
    public void While_the_link_is_being_chased_the_frame_age_changes_nothing(ConnectionState state, WheelLink expected)
    {
        Assert.Equal(expected, Evaluate(state, staleFor: 0));
        Assert.Equal(expected, Evaluate(state, staleFor: 600));
    }

    /// <summary>
    /// Отключено — это либо покой, либо беда, и разводятся они не догадкой, а тем, знаем ли мы
    /// причину. Красная плашка без причины была бы пугалкой, серая при отказе — враньём. Каждая
    /// типизированная причина (план 19 Б4), кроме <see cref="LinkProblem.None"/>, даёт Failed.
    /// </summary>
    [Theory]
    [InlineData(LinkProblem.None, WheelLink.Idle)]
    [InlineData(LinkProblem.NoPermissions, WheelLink.Failed)]
    [InlineData(LinkProblem.BluetoothOff, WheelLink.Failed)]
    [InlineData(LinkProblem.NoWheelSelected, WheelLink.Failed)]
    [InlineData(LinkProblem.WheelRefused, WheelLink.Failed)]
    public void Disconnected_splits_into_calm_and_trouble_by_whether_the_reason_is_known(
        LinkProblem problem, WheelLink expected)
    {
        Assert.Equal(expected, Evaluate(ConnectionState.Disconnected, staleFor: 0, problem));
    }

    /// <summary>
    /// Обрыв на ходу: кадры шли, связь пропала, погоня началась — фаза меняется в тот же миг, не
    /// дожидаясь, пока состарится последний кадр.
    /// </summary>
    [Fact]
    public void Losing_the_link_mid_ride_switches_phase_before_the_last_frame_goes_stale()
    {
        Assert.Equal(WheelLink.Connected, Evaluate(ConnectionState.Connected, staleFor: 0.2));
        Assert.Equal(WheelLink.Reconnecting, Evaluate(ConnectionState.Reconnecting, staleFor: 0.2));
    }

    private static WheelLink Evaluate(ConnectionState state, double staleFor, LinkProblem problem = LinkProblem.None) =>
        LinkStatus.Evaluate(state, staleFor, problem);
}
