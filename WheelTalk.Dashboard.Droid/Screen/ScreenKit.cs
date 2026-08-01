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

    public static bool IsDarkTheme(this Context context) =>
        (context.Resources!.Configuration!.UiMode & UiMode.NightMask) == UiMode.NightYes;

    public static Color PageBackground(this Context context) =>
        context.IsDarkTheme() ? Color.ParseColor("#1f1f1f") : Color.White;
}
