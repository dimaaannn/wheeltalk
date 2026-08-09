using System.Text;

namespace WheelTalk.Core.Settings;

/// <summary>
/// Чем настройки этого телефона отличаются от заводских — для отчёта диагностики (план 11 §4.2).
/// <para>
/// Список отличий, а не полный дамп: сорока строк со значениями по умолчанию никто читать не станет,
/// а вот отладочный порог тревоги, забытый на чужом телефоне, обязан быть виден с первого взгляда —
/// 28.07.2026 такие пороги вычищали руками перед заливкой, и на чужом устройстве вычистить их
/// некому.
/// </para>
/// <para>
/// Обход идёт через <see cref="SettingsBinder"/> по боевой области (планы 29 §29.3, 30): «изменено»
/// — это <see cref="SettingOrigin"/> не заводское, то есть ровно тот же ответ, который видит человек
/// на строке настройки. Второго определения «изменено» здесь не заводится.
/// </para>
/// </summary>
public static class ChangedSettings
{
    /// <summary>Что подставить вместо значения, которое в файл писать нельзя (<see cref="SettingDescriptor.Secret"/>).</summary>
    private const string Hidden = "задан";

    /// <summary>
    /// Отличия строками «ключ = значение (слой)». Пустой список — всё как с завода, и это ответ не
    /// хуже прочих.
    /// </summary>
    public static IReadOnlyList<string> Lines(SettingsBinder binder)
    {
        var lines = new List<string>();

        foreach (var descriptor in binder.Descriptors)
        {
            // Сообщённое колесом — не настройка человека, а слепок с железа; сеансовое живёт до
            // закрытия страницы; у действия и справки значения нет вовсе. Всё это в отличиях —
            // шум, за которым потеряется единственная важная строка.
            if (descriptor.ReportedByWheel || descriptor.Transient) continue;
            if (descriptor.Kind is SettingKind.Action or SettingKind.Note or SettingKind.Slider) continue;

            var resolved = binder.Read(descriptor, binder.LiveScope);
            if (resolved.Origin == SettingOrigin.Factory || resolved.Value is not { } value) continue;

            string shown = descriptor.Secret ? Hidden : value;
            lines.Add($"{descriptor.Key} = {shown} ({Layer(resolved.Origin)})");
        }

        return lines;
    }

    /// <summary>Готовый кусок отчёта — заголовок и строки; пусто, когда отличий нет.</summary>
    public static string Describe(SettingsBinder binder)
    {
        var lines = Lines(binder);
        if (lines.Count == 0) return "настройки: всё заводское";

        var text = new StringBuilder("настройки, отличные от заводских:");
        foreach (string line in lines) text.Append(Environment.NewLine).Append("  ").Append(line);
        return text.ToString();
    }

    private static string Layer(SettingOrigin origin) => origin switch
    {
        SettingOrigin.Wheel => "колесо",
        SettingOrigin.Global => "общее",
        _ => "завод",
    };
}
