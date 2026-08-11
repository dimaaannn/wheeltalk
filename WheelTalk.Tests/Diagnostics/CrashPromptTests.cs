using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Diagnostics;

/// <summary>
/// Замки диалога «отправить журнал после сбоя» (решение владельца 12.08.2026). И каталог настроек, и
/// сам диалог живут в android-проекте, тестам не видном — приём тот же, что в
/// <c>SystemAlertOverlayTests</c>: читаем боевые исходники текстом.
/// </summary>
public class CrashPromptTests
{
    /// <summary>(а) Настройка — общая для телефона, включена по умолчанию, живёт в «Разборе поломок».</summary>
    [Fact]
    public void Setting_is_global_only_on_by_default_and_lives_in_the_diagnostics_section()
    {
        string optionsSource = RepoFiles.Read("WheelTalk.Droid/Configuration/DiagnosticsOptions.cs");
        string propertyLine = optionsSource
            .Split('\n')
            .Single(line => line.Contains("bool PromptShareAfterCrash"));

        Assert.Contains("= true", propertyLine);

        string catalogue = RepoFiles.Read("WheelTalk.Droid/Settings/Catalogue/AppPage.cs");
        string descriptor = DescriptorBlock(catalogue, "PromptAfterCrashKey");

        Assert.Contains("GlobalOnly = true", descriptor);
        Assert.Contains("SectionKey = \"SectionDiagnostics\"", descriptor);
    }

    /// <summary>(б) Показ гейтится обоими условиями сразу, одной строкой — не двумя проверками врозь.</summary>
    [Fact]
    public void Prompt_condition_checks_both_flags_in_one_line()
    {
        string source = RepoFiles.Read("WheelTalk.Droid/Main/MainActivity.cs");
        string body = RepoFiles.MethodBody(source, "private void OfferCrashShareIfNeeded()");

        int ifAt = body.IndexOf("if (", StringComparison.Ordinal);
        Assert.True(ifAt >= 0, "В теле метода нет условия «if».");
        string conditionLine = body[ifAt..].Split('\n')[0];

        Assert.Contains("CrashReport.PreviousRunCrashed", conditionLine);
        Assert.Contains("_diagnostics.PromptShareAfterCrash", conditionLine);
        Assert.Contains("return;", conditionLine);
    }

    /// <summary>(г) Обе кнопки — «Отправить» и «Не сейчас» — пишут галочку, а не только одна из них.</summary>
    [Fact]
    public void Both_buttons_persist_the_checkbox_before_acting()
    {
        string source = RepoFiles.Read("WheelTalk.Droid/Main/MainActivity.cs");
        string body = RepoFiles.MethodBody(source, "private void OfferCrashShareIfNeeded()");

        int positiveAt = body.IndexOf("SetPositiveButton", StringComparison.Ordinal);
        int negativeAt = body.IndexOf("SetNegativeButton", StringComparison.Ordinal);
        Assert.True(positiveAt >= 0 && negativeAt > positiveAt, "Обеих кнопок в теле метода нет.");

        string positiveBranch = body[positiveAt..negativeAt];
        string negativeBranch = body[negativeAt..];

        Assert.Contains("SaveIfChecked()", positiveBranch);
        Assert.Contains("SaveIfChecked()", negativeBranch);
    }

    /// <summary>(д) «Отправить» ведёт в экран состава, а не в голый ACTION_SEND мимо него.</summary>
    [Fact]
    public void Send_button_goes_through_DiagnosticsShare_not_a_raw_intent()
    {
        string source = RepoFiles.Read("WheelTalk.Droid/Main/MainActivity.cs");
        string body = RepoFiles.MethodBody(source, "private void OfferCrashShareIfNeeded()");

        Assert.Contains("DiagnosticsShare.Send();", body);
        Assert.DoesNotContain("ActionSend", body);
        Assert.DoesNotContain("CreateChooser", body);
    }

    /// <summary>Тело записи <c>new() { ... }</c> по её ключу — тот же приём, что в <c>SystemAlertOverlayTests</c>.</summary>
    private static string DescriptorBlock(string source, string keyExpression)
    {
        int i = 0;
        while (true)
        {
            int at = source.IndexOf("new()", i, StringComparison.Ordinal);
            if (at < 0) throw new InvalidOperationException($"Записи с ключом «{keyExpression}» в исходнике нет.");

            int open = source.IndexOf('{', at);
            int depth = 0;
            int end = -1;
            for (int j = open; j < source.Length; j++)
            {
                if (source[j] == '{') depth++;
                else if (source[j] == '}' && --depth == 0) { end = j; break; }
            }

            if (end < 0) throw new InvalidOperationException($"У «new()» на позиции {at} не сошлись скобки.");

            string body = source[open..end];
            i = end + 1;

            if (body.Contains($"Key = {keyExpression}", StringComparison.Ordinal)) return body;
        }
    }
}
