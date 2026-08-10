using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Что делает короткий тап по плитке (решение владельца 10.08.2026): вне режима правки — открывает
/// <b>меню действий</b>, и ничего не рушит сам. Прежде тап у каждого вида значил своё: у крайнего
/// значения стирал накопленный пик, у графика открывал просмотр, у прочих не делал ничего.
/// <para>
/// <b>Почему замок по исходнику.</b> Жест живёт в android-библиотеке, поднять её отсюда нельзя, а
/// стеречь тут надо не арифметику, а решение: сброс — только по явному слову из меню. Проверяется
/// оно там, где может сломаться, — в теле обработчика тапа (тот же приём, что у замков §29.2 и у
/// проверки формата раскладки).
/// </para>
/// </summary>
public class TileTapTests
{
    private static string Screen() =>
        RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/Tiles/TilesScreen.cs");

    /// <summary>
    /// Тап открывает меню — и только. Ни сброса, ни полноэкранного графика прямо из жеста: касание
    /// в кармане не должно стирать пик, копившийся всю поездку.
    /// </summary>
    [Fact]
    public void A_tap_outside_editing_opens_the_menu_and_touches_nothing()
    {
        string tap = RepoFiles.MethodBody(Screen(), "private void SingleTap(float x, float y)");

        Assert.Contains("ShowActions(", tap);
        Assert.DoesNotContain("Reset", tap);
        Assert.DoesNotContain("ChartViewer", tap);
    }

    /// <summary>
    /// А сброс и просмотр графика живут в меню — там, где у них есть надпись. Без этого «ничего не
    /// рушит» вышло бы проверкой того, что сбросить нельзя вовсе.
    /// </summary>
    [Fact]
    public void The_menu_is_where_reset_and_the_chart_live()
    {
        string actions = RepoFiles.MethodBody(Screen(), "private void ShowActions(int position, TileView? view)");

        Assert.Contains("view.ResetValue", actions);
        Assert.Contains("ChartViewer.Show", actions);
        Assert.Contains("AskCaption", actions);
    }

    /// <summary>
    /// Смена колеса сбрасывает крайние значения — и <b>только</b> их: у дистанции точка отсчёта
    /// переживает и смену колеса, и возвращение к прежнему (решение владельца 10.08.2026).
    /// </summary>
    [Fact]
    public void A_wheel_change_resets_extremes_but_never_a_trip()
    {
        string reset = RepoFiles.MethodBody(Screen(), "public void ResetExtremeTiles()");

        Assert.Contains("ExtremumTileView", reset);
        Assert.DoesNotContain("TripTileView", reset);
    }
}
