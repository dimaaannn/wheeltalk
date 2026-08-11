using System.Text.RegularExpressions;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Alerts;

/// <summary>
/// Замки тревоги поверх ЧУЖИХ приложений (<c>SystemAlertOverlay</c>, решение владельца 11.08.2026).
/// Читаются по исходникам: и каталог настроек, и само окно живут в android-проекте, тестам не
/// видном — приём тот же, что в <c>ZeroDisablesTests</c> и <c>WindowOwnershipTests</c>.
/// </summary>
public class SystemAlertOverlayTests
{
    /// <summary>(а) Настройка — общая для телефона, а не для колеса, и по умолчанию выключена.</summary>
    [Fact]
    public void Setting_is_global_only_and_off_by_default()
    {
        string catalogue = RepoFiles.Read("WheelTalk.Droid/Settings/Catalogue/AlertsPage.cs");
        string descriptor = DescriptorBlock(catalogue, "AlertSignals:OverlayOtherApps");

        Assert.Contains("GlobalOnly = true", descriptor);

        string optionsSource = RepoFiles.Read("WheelTalk.Droid/Configuration/AlertSignalOptions.cs");
        string propertyLine = optionsSource
            .Split('\n')
            .Single(line => line.Contains("bool OverlayOtherApps"));

        // Без инициализатора «= true» — свойство остаётся при default(bool), то есть выключено.
        Assert.DoesNotContain("= true", propertyLine);
    }

    /// <summary>(б) Показ гейтится и системным разрешением, и тем, что наших экранов нет впереди.</summary>
    [Fact]
    public void Shows_only_with_permission_and_no_own_screen_in_front()
    {
        string source = RepoFiles.Read("WheelTalk.Droid/Alerts/SystemAlertOverlay.cs");
        string body = RepoFiles.MethodBody(source, "private void Evaluate()");

        Assert.Contains("CanDrawOverlays", body);
        Assert.Contains("HostVisible", body);
    }

    /// <summary>(в) Манифест просит разрешение — без строки экран запроса не открыть вовсе.</summary>
    [Fact]
    public void Manifest_declares_the_overlay_permission()
    {
        string manifest = RepoFiles.Read("WheelTalk.Droid/Properties/AndroidManifest.xml");

        Assert.Contains("android.permission.SYSTEM_ALERT_WINDOW", manifest);
    }

    /// <summary>
    /// (г) Окно не бросается: единственный вызов <c>AddView</c> и единственный <c>RemoveView</c>, и
    /// оба пути, которыми окно может перестать быть нужным — конец тревоги/условий (<c>Evaluate</c>)
    /// и конец жизни синглтона (<c>Dispose</c>) — доходят до второго.
    /// </summary>
    [Fact]
    public void AddView_is_always_matched_by_a_RemoveView_path()
    {
        string source = RepoFiles.Read("WheelTalk.Droid/Alerts/SystemAlertOverlay.cs");

        Assert.Single(Regex.Matches(source, @"_windowManager\.AddView\("));
        Assert.Single(Regex.Matches(source, @"_windowManager\.RemoveView\("));

        Assert.Contains("Remove();", RepoFiles.MethodBody(source, "private void Evaluate()"));
        Assert.Contains("Remove();", RepoFiles.MethodBody(source, "public void Dispose()"));
    }

    /// <summary>Тело записи <c>new() { ... }</c> по её ключу — тот же приём, что в <c>ZeroDisablesTests</c>.</summary>
    private static string DescriptorBlock(string source, string key)
    {
        int i = 0;
        while (true)
        {
            int at = source.IndexOf("new()", i, StringComparison.Ordinal);
            if (at < 0) throw new InvalidOperationException($"Записи с ключом «{key}» в исходнике нет.");

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

            if (body.Contains($"Key = \"{key}\"", StringComparison.Ordinal)) return body;
        }
    }
}
