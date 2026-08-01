using Android.App;
using Android.Views;
using AndroidX.Core.View;

namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Системные отступы для нативных экранов (adaptive-layout.md §4): полоса состояния приложения
/// начинается ниже статус-бара/выреза, нижний контент — выше жестовой навигации. Ничего
/// интерактивного под системными зонами, а фон уходит под них, а не белыми полосами.
/// <para>
/// <c>SetDecorFitsSystemWindows(false)</c> отдаёт разметке всю площадь окна (иначе система сама
/// подрезает контент под барами и вставлять отступы было бы не от чего); слушатель ниже добавляет
/// системный инсет к уже заданному паддингу корня, а не подменяет его — так каждый экран продолжает
/// планировать свои поля по контенту.
/// </para>
/// </summary>
public static class EdgeToEdge
{
    /// <param name="giveTopAway">
    /// Кому отдать верхний инсет вместо паддинга. Задан — верх в паддинг не идёт вовсе, и экран сам
    /// решает, что делать с полосой под статус-баром. Так живёт главный экран: это прибор, а не
    /// страница, и полоса под часами ему дороже, чем аккуратность полей — фон панели уходит под бар,
    /// а приборы и плашка связи начинаются ниже (adaptive-layout.md §4, прогон 5).
    /// </param>
    public static void Apply(Activity activity, View root, Action<int>? giveTopAway = null)
    {
        WindowCompat.SetDecorFitsSystemWindows(activity.Window!, false);

        // Значки статус-бара — светлые всегда: панель тёмная независимо от темы устройства, и
        // тёмные значки на ней пропадают.
        if (WindowCompat.GetInsetsController(activity.Window!, root) is { } bars)
        {
            bars.AppearanceLightStatusBars = false;
        }

        int left = root.PaddingLeft, top = root.PaddingTop, right = root.PaddingRight, bottom = root.PaddingBottom;
        ViewCompat.SetOnApplyWindowInsetsListener(root, new Listener(left, top, right, bottom, giveTopAway));
    }

    private sealed class Listener(int left, int top, int right, int bottom, Action<int>? giveTopAway)
        : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(View? v, WindowInsetsCompat? insets)
        {
            if (v is null || insets is null) return insets;

            var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout())!;
            v.SetPadding(left + bars.Left, giveTopAway is null ? top + bars.Top : top, right + bars.Right, bottom + bars.Bottom);
            giveTopAway?.Invoke(bars.Top);
            return insets;
        }
    }
}
