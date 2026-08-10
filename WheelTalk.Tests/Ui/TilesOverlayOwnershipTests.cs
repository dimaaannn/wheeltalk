using System.Text.RegularExpressions;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Ui;

/// <summary>
/// Замок «у окна поверх плиток есть хозяин». Диалог — полноэкранный просмотр графика
/// (<c>ChartViewer</c>), меню правки (<c>TileEditor</c>), меню действий и вопрос о подписи
/// (<c>TileActions</c>) — висит на окне <b>активности</b>, а не на ветви вью экрана. Открытый и брошенный, он переживает свою активность: она уничтожается вместе с
/// его окном, и в системный журнал уходит <c>WindowLeaked</c>, а диалог держит живыми ветвь вью,
/// график и его данные.
/// <para>
/// Повод у замка свой, дамп владельца 10.08.2026: тап по плитке-графику, через минуту активность
/// уничтожена (<c>IsFinishing=True</c>) — и единственный стек в хвосте дампа это
/// <c>WindowLeaked … Dialog.show … TilesScreen_TileTouch.onSingleTapUp</c>. Стек не различает, какой
/// из двух диалогов открыли, — значит хозяин нужен обоим.
/// </para>
/// <para>
/// Проверяется по исходникам: <c>android</c>-проекты тестам не видны, поднять экран в тесте нечем, а
/// правило простое и читается глазами — кто открыл окно, тот его и закрывает.
/// </para>
/// </summary>
public class TilesOverlayOwnershipTests
{
    private const string TilesScreen = "WheelTalk.Dashboard.Droid/Screen/Tiles/TilesScreen.cs";

    private const string ChartViewer = "WheelTalk.Dashboard.Droid/Screen/Tiles/ChartViewer.cs";

    private const string TileEditor = "WheelTalk.Dashboard.Droid/Screen/Tiles/TileEditor.cs";

    private const string TileActions = "WheelTalk.Dashboard.Droid/Screen/Tiles/TileActions.cs";

    /// <summary>
    /// Окно, которое некому отдать, некому и закрыть: <c>void Show</c> — уже утечка. Правило общее
    /// на все окна экрана, и меню действий с вопросом о подписи (10.08.2026) вошли в него наравне с
    /// просмотром и меню правки.
    /// </summary>
    [Fact]
    public void Every_window_hands_itself_to_the_owner()
    {
        Assert.Matches(@"public static Dialog Show\(", RepoFiles.Read(ChartViewer));
        Assert.Matches(@"public static Dialog Show\(", RepoFiles.Read(TileEditor));
        Assert.Matches(@"public static Dialog Show\(", RepoFiles.Read(TileActions));
        Assert.Matches(@"public static Dialog AskCaption\(", RepoFiles.Read(TileActions));
    }

    /// <summary>
    /// Открыл — держи: брошенный вызов и есть та самая утечка. Считаются <b>все</b> окна экрана;
    /// сегодня их четыре — меню действий, вопрос о подписи, просмотр графика и меню правки, — и
    /// каждое присвоено <c>_overlay</c>. Прибавилось окно, а число нет — значит оно брошено.
    /// </summary>
    [Fact]
    public void The_tiles_screen_keeps_every_window_it_opens()
    {
        var opened = Regex.Matches(
            RepoFiles.Read(TilesScreen),
            @"[^\r\n]*(?:ChartViewer|TileEditor|TileActions)\.(?:Show|AskCaption)\(");

        Assert.Equal(4, opened.Count);

        foreach (Match call in opened)
        {
            Assert.Matches(
                @"_overlay = (?:ChartViewer|TileEditor|TileActions)\.(?:Show|AskCaption)\($",
                call.Value.TrimStart());
        }
    }

    /// <summary>
    /// Экран уходит из окна — окно поверх него закрывается. Цепочка короткая и держится целиком:
    /// корень зовёт <c>onDetached</c>, хозяин отдаёт корню <c>CloseOverlay</c>, а тот закрывает
    /// диалог. Порвано любое звено — окно снова утечёт.
    /// </summary>
    [Fact]
    public void The_tiles_screen_closes_its_window_when_it_leaves_the_screen()
    {
        string source = RepoFiles.Read(TilesScreen);

        Assert.Matches(@"protected override void OnDetachedFromWindow\(\)", source);
        Assert.Contains("onDetached()", RepoFiles.MethodBody(source, "protected override void OnDetachedFromWindow()"));
        Assert.Matches(@"CloseOverlay\)", source);
        Assert.Contains("Dismiss()", RepoFiles.MethodBody(source, "private void CloseOverlay()"));
    }

    /// <summary>
    /// Заполнение просмотра отменяемо. История читается из базы и возвращается уже после закрытия —
    /// а рисовать тогда некуда: за графиком стоят peer-объекты уничтоженной активности, и сама
    /// задача держит их живыми. <c>CancellationToken.None</c> здесь означает «остановить нечем».
    /// </summary>
    [Fact]
    public void The_viewer_stops_filling_a_window_that_is_already_closed()
    {
        string source = RepoFiles.Read(ChartViewer);

        Assert.DoesNotContain("CancellationToken.None", source);
        Assert.Matches(@"ReadAsync\([^)]*alive\)", source);
        Assert.Matches(@"DismissEvent \+= .*Cancel\(\)", source);
    }
}
