using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Settings;

/// <summary>
/// Замки показа строк «сообщено колесом» (план 34, капкан К3). Дефект был спящий: ветка
/// <c>ReportedByWheel</c> печатала значение через <c>ParseBool</c> и потому умела ровно два слова —
/// пока все такие строки были переключателями, «Да/Нет» сходилось. С разбором страницы настроек
/// Veteran туда пошли числа (94, 200, 145), и каждое молча показалось бы как «Нет».
/// <para>
/// Правило показа живёт в android-проекте, тестам не референсном (<c>WheelTalk.Tests.csproj</c> —
/// только Core и Storage), и поднять его нечем: текст берётся из <c>AppStrings</c> через
/// <c>ResourceManager</c> сборки WheelTalk.Droid. Вынести выбор текста в Core можно было бы лишь
/// протащив туда переводчик и слова «Да»/«Нет» параметрами — три подпорки ради четырёх строк.
/// Поэтому замок читает боевой исходник текстом, приёмом <c>BackgroundWatchTests</c> (г), (е).
/// </para>
/// </summary>
public class ReportedByWheelDisplayTests
{
    /// <summary>
    /// (а) Ветка сообщённого не толкует значение сама и не знает про <c>ParseBool</c>: она зовёт
    /// общий <c>ReportedText</c>. Именно возврат <c>ParseBool</c> сюда и есть тот дефект — число
    /// превратилось бы в «Да/Нет».
    /// </summary>
    [Fact]
    public void The_reported_row_asks_SettingsFormat_instead_of_printing_a_yes_or_no()
    {
        string branch = RepoFiles.MethodBody(
            RepoFiles.Read("WheelTalk.Droid/Settings/SettingsCategoryActivity.cs"), "if (descriptor.ReportedByWheel)");

        Assert.Contains("SettingsFormat.ReportedText(descriptor, value)", branch);
        Assert.DoesNotContain("ParseBool", branch);
        Assert.DoesNotContain("AppStrings.Yes", branch);
    }

    /// <summary>
    /// (б) Текст выбирается по виду настройки — все три вида названы поимённо. Число печатается
    /// числом (<c>Display</c> добавляет единицу, если она есть), выбор — названием варианта, а не
    /// его кодом.
    /// </summary>
    [Fact]
    public void The_reported_text_is_chosen_by_the_setting_kind()
    {
        string body = RepoFiles.MethodBody(
            RepoFiles.Read("WheelTalk.Droid/Settings/SettingsFormat.cs"),
            "public static string ReportedText(SettingDescriptor descriptor, string value)");

        Assert.Contains("SettingKind.Toggle => ParseBool(value) ? AppStrings.Yes : AppStrings.No", body);
        Assert.Contains("SettingKind.Number => Display(descriptor, ParseNumber(value))", body);
        Assert.Contains("SettingKind.Choice => ChoiceLabel(descriptor, value)", body);
    }

    /// <summary>
    /// (в) «Как есть, ничего не выдумывать» (слово владельца 15.08.2026). Сообщённое число не
    /// прижимается к границам ручки — <c>Maximum</c> по умолчанию 100, а Veteran сообщает наклон
    /// числом 200, и клампом экран сказал бы 100. Не подменяется оно и словом «выкл.»: механизм
    /// <c>ZeroDisables</c> — правило правимых ручек, сообщённых он не касается.
    /// </summary>
    [Fact]
    public void The_reported_number_is_shown_raw_without_clamping_or_a_word_for_zero()
    {
        string body = RepoFiles.MethodBody(
            RepoFiles.Read("WheelTalk.Droid/Settings/SettingsFormat.cs"),
            "public static string ReportedText(SettingDescriptor descriptor, string value)");

        Assert.DoesNotContain("Clamp", body);
        Assert.DoesNotContain("ZeroDisables", body);
        Assert.DoesNotContain("SettingsValueOff", body);
    }
}
