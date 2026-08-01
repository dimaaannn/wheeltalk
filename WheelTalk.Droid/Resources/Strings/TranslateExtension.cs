using System.Globalization;
using System.Resources;

namespace WheelTalk.Droid.Resources.Strings;

/// <summary>
/// Строка интерфейса по ключу. В MAUI-версии это ещё и markup-расширение для XAML
/// (<c>{loc:Translate Key}</c>) — здесь XAML-страниц не будет, поэтому от файла остался только
/// статический метод, ровно тот механизм, который позволяет менять язык без перезапуска
/// (см. docs/native-rewrite-inventory.md §7 «TranslateExtension — не просто MAUI markup extension»).
/// </summary>
public static class TranslateExtension
{
    private static readonly ResourceManager Resources =
        new("WheelTalk.Droid.Resources.Strings.AppStrings", typeof(TranslateExtension).Assembly);

    /// <summary>
    /// Ключ, которого нет, показывается как <c>!Ключ!</c>, а не пустотой: пропавшую подпись иначе
    /// нечем заметить.
    /// </summary>
    public static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) ?? $"!{key}!";
}
