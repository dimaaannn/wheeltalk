using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Diagnostics;

/// <summary>
/// Замок пути «поделиться диагностикой» (план 11 §4.4): между нажатием кнопки и системным диалогом
/// обязан стоять <b>экран состава</b>. Обход этого экрана — это отправка неизвестно чего: в журнале
/// MAC колеса, пути файлов и модель телефона.
/// <para>
/// Читается по исходникам: android-проект тестам не виден, а держать надо именно боевой путь. Мест
/// всего два, и правило простое — кнопка открывает экран, диалог живёт на экране.
/// </para>
/// </summary>
public class DiagnosticsShareRouteTests
{
    private const string Button = "WheelTalk.Droid/Diagnostics/DiagnosticsShare.cs";

    private const string Screen = "WheelTalk.Droid/Diagnostics/DiagnosticsShareActivity.cs";

    /// <summary>Кнопка ведёт на экран и никого не минует: ни отправки, ни упаковки у неё больше нет.</summary>
    [Fact]
    public void The_button_opens_the_screen_instead_of_the_system_dialog()
    {
        string source = RepoFiles.Read(Button);

        Assert.Contains("typeof(DiagnosticsShareActivity)", source);
        Assert.DoesNotContain("Intent.ActionSend", source);
        Assert.DoesNotContain("CreateChooser", source);
    }

    /// <summary>
    /// Отправка живёт на экране и только в нём — и только там, где человек нажал «Отправить»: сбор
    /// состава (<c>Prepare</c>) идёт на открытии, упаковка — на нажатии.
    /// </summary>
    [Fact]
    public void The_screen_shows_what_goes_out_before_it_offers_to_send_it()
    {
        string source = RepoFiles.Read(Screen);

        Assert.Contains("DiagnosticsBundle.Prepare()", source);
        Assert.Contains("DiagnosticsBundlePlan.Weigh", source);

        string share = RepoFiles.MethodBody(source, "private void Share()");
        Assert.Contains("DiagnosticsBundle.Pack(_parts)", share);
        Assert.Contains("Intent.ActionSend", share);

        // Упаковка — только в «Отправить»: собранный где-то ещё архив ушёл бы мимо показанного
        // состава, а значит мимо того, что человек видел.
        Assert.Equal(1, Occurrences(source, "DiagnosticsBundle.Pack("));
        Assert.Equal(1, Occurrences(source, "Intent.ActionSend"));
    }

    private static int Occurrences(string source, string needle)
    {
        int count = 0;
        for (int at = source.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = source.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
