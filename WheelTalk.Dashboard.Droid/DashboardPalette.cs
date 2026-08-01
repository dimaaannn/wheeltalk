using Android.Graphics;

namespace WheelTalk.Dashboard.Droid;

/// <summary>
/// Цвета панели. Их две штуки нарочно: одна — оригинальная (WheelLog красит ШИМ от бледно-жёлтого
/// к красному), вторая — из палитры Ванга (синий/оранжевый/киноварь), которая различима при
/// дейтеранопии, а это порядка 8 % мужчин, то есть почти столько же, сколько райдеров.
/// <para>
/// Что из этого станет умолчанием приложения — решение владельца, и по правилам репозитория
/// расхождение с оригиналом придётся записать. Пока обе живут рядом и переключаются в стенде:
/// сравнивать их словами бессмысленно.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/DashboardPalette.cs</c>: единственная правка —
/// <c>Microsoft.Maui.Graphics.Color.FromArgb(string)</c> заменён на
/// <c>Android.Graphics.Color.ParseColor(string)</c>, который принимает те же строки «#RRGGBB» и
/// «#AARRGGBB». Обе палитры (Ванг и WheelLog) и все восемь HEX-кодов в каждой перенесены без
/// изменений.
/// </para>
/// </summary>
public sealed record DashboardPalette(
    string Name,
    Color Background,
    Color Ink,
    Color Dim,
    Color Calm,
    Color Caution,
    Color Danger,
    Color Accent,
    Color Good)
{
    /// <summary>Палитра Ванга: различима при любой форме цветовой слепоты.</summary>
    public static readonly DashboardPalette Wong = new(
        Name: "Ванг",
        Background: Color.ParseColor("#101010"),
        Ink: Color.ParseColor("#F2F2F2"),
        Dim: Color.ParseColor("#8A8A8A"),
        Calm: Color.ParseColor("#0072B2"),
        Caution: Color.ParseColor("#E69F00"),
        Danger: Color.ParseColor("#D55E00"),
        Accent: Color.ParseColor("#F0E442"),
        // Синевато-зелёный из той же палитры Ванга: отличим от жёлтого при дейтеранопии, тогда как
        // «чистый» зелёный с ним сливается — а на ленте напряжения эти двое стоят рядом.
        Good: Color.ParseColor("#009E73"));

    /// <summary>Как в оригинале: спокойное — полупрозрачный белый, тревожное — чистый красный.</summary>
    public static readonly DashboardPalette WheelLog = new(
        Name: "WheelLog",
        Background: Color.ParseColor("#101010"),
        Ink: Color.ParseColor("#F2F2F2"),
        Dim: Color.ParseColor("#8A8A8A"),
        Calm: Color.ParseColor("#59FFFFFF"),
        Caution: Color.ParseColor("#FFD24A"),
        Danger: Color.ParseColor("#FF0000"),
        Accent: Color.ParseColor("#FFFFFF"),
        Good: Color.ParseColor("#2E7D32"));

    public static readonly IReadOnlyList<DashboardPalette> All = [Wong, WheelLog];

    /// <summary>
    /// Цвет по значению ШИМ. Ступенями, а не интерполяцией: смысл цвета — ответить «много ли это»,
    /// а плавный переход как раз это и размывает.
    /// </summary>
    public Color ForPwm(double pwm, DashboardOptions options) => pwm switch
    {
        _ when pwm >= options.Thresholds.DangerPwm => Danger,
        _ when pwm >= options.Thresholds.WarnPwm => Caution,
        _ => Calm,
    };
}
