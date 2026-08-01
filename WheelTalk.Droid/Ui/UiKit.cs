using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Dashboard.Droid.Screen;

namespace WheelTalk.Droid.Ui;

/// <summary>
/// Мелочи разметки, собранной кодом, общие для новых экранов (<see cref="TelemetryActivity"/>,
/// <see cref="ScanActivity"/>, <see cref="RecordingActivity"/>, <see cref="RidesActivity"/>,
/// <see cref="RideActivity"/>) — те же приёмы, что уже прижились в <c>MainActivity</c> (план 12 §4:
/// присваивание свойств только при изменении), не продублированные вручную в каждом файле.
/// Плотность, тема и фон страницы живут не здесь, а в <c>ScreenKit</c> библиотеки панели: она
/// нужна и ей самой, а два владельца одной и той же мелочи разошлись бы (план 14, Б3).
/// </summary>
internal static class UiKit
{
    /// <summary>Правка текста только при изменении — опись §3.3, план 11 §0.</summary>
    public static void SetText(this TextView view, string value)
    {
        if (view.Text != value) view.Text = value;
    }

    public static void SetShown(this View view, bool value)
    {
        var target = value ? ViewStates.Visible : ViewStates.Gone;
        if (view.Visibility != target) view.Visibility = target;
    }

    public static Button CreateButton(Context context, string text)
    {
        var button = new Button(context) { Text = text };
        button.SetTextSize(ComplexUnitType.Sp, 14);
        button.SetAllCaps(false);
        button.SetTextColor(Color.White);
        button.StateListAnimator = null;
        var background = new GradientDrawable();
        background.SetShape(ShapeType.Rectangle);
        background.SetCornerRadius(context.Dp(8));
        background.SetColor(context.IsDarkTheme() ? Color.ParseColor("#AC99EA") : Color.ParseColor("#512BD4"));
        button.Background = background;
        return button;
    }

    /// <summary>Тонкая горизонтальная черта-разделитель секций — как <c>BoxView</c> в MAUI-эталонах.</summary>
    public static View Divider(Context context)
    {
        var view = new View(context)
        {
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, context.Dp(1)),
        };
        view.SetBackgroundColor(Color.Gray);
        view.Alpha = 0.2f;
        return view;
    }

    public static Color PlainText(Context context) =>
        context.IsDarkTheme() ? Color.White : Color.Black;
}
