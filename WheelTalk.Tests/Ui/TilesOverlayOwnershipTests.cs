using System.Text.RegularExpressions;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Ui;

/// <summary>
/// Замок «у окна поверх плиток есть хозяин». Диалог — полноэкранный просмотр графика
/// (<c>ChartViewer</c>) и меню плитки (<c>TileEditor</c>) — висит на окне <b>активности</b>, а не на
/// ветви вью экрана. Открытый и брошенный, он переживает свою активность: она уничтожается вместе с
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

    /// <summary>Окно, которое некому отдать, некому и закрыть: <c>void Show</c> — уже утечка.</summary>
    [Fact]
    public void Both_windows_hand_themselves_to_the_owner()
    {
        Assert.Matches(@"public static Dialog Show\(", RepoFiles.Read(ChartViewer));
        Assert.Matches(@"public static Dialog Show\(", RepoFiles.Read(TileEditor));
    }

    /// <summary>Открыл — держи: брошенный вызов <c>Show</c> и есть та самая утечка.</summary>
    [Fact]
    public void The_tiles_screen_keeps_every_window_it_opens()
    {
        var opened = Regex.Matches(RepoFiles.Read(TilesScreen), @"[^\r\n]*(?:ChartViewer|TileEditor)\.Show\(");

        Assert.Equal(2, opened.Count);

        foreach (Match call in opened)
        {
            Assert.Matches(@"_overlay = (?:ChartViewer|TileEditor)\.Show\($", call.Value.TrimStart());
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
