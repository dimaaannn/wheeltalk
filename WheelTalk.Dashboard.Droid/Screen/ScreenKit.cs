using Android.Content;
using Android.Content.Res;
using Android.Graphics;

namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Те же мелочи, что <c>UiKit</c> приложения, — ровно в объёме, который нужен шторке и полосе
/// тревоги. Библиотека приложение не видит, а тащить сюда весь UiKit незачем: остальные его
/// помощники обслуживают страницы, которых в библиотеке нет.
/// </summary>
public static class ScreenKit
{
    public static int Dp(this Context context, float dp) =>
        (int)Math.Round(dp * context.Resources!.DisplayMetrics!.Density);

    /// <summary>
    /// Кегль в пикселях. Отдельно от <see cref="Dp"/> намеренно: sp растёт вместе с системным
    /// шрифтом, dp — нет, и подменять одно другим значит считать бюджет текста по величине, которая
    /// к тексту отношения не имеет.
    /// </summary>
    public static float Sp(this Context context, float sp) =>
        sp * context.Resources!.DisplayMetrics!.ScaledDensity;

    public static bool IsDarkTheme(this Context context) =>
        (context.Resources!.Configuration!.UiMode & UiMode.NightMask) == UiMode.NightYes;

    // Фон страницы здесь больше не считается: он стал ролью палитры документных экранов
    // (WheelTalk.Droid/Ui/DocPalette, план 33) и живёт в ресурсах приложения, которых библиотека не
    // видит. Двух ответов на вопрос «какого цвета страница» не держим: страницы — приложения, а
    // библиотеке остались приборные поверхности, у которых тема одна и всегда тёмная.
}
