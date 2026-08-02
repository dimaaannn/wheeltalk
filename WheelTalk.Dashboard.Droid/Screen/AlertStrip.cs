using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Полоса тревоги колеса над панелью: строка на всю ширину, всплывает раз в поездку, если
/// всплывает вообще. Цвет — часть сообщения, поэтому <see cref="Show"/> принимает его явно:
/// <see cref="Danger"/> — тревога самого колеса, <see cref="Notice"/> — служебное
/// («ещё раз — выход»). Слова, как везде в библиотеке, даёт вызывающий.
/// </summary>
public sealed class AlertStrip : TextView
{
    public static readonly Color Danger = Color.ParseColor("#B00020");
    public static readonly Color Notice = Color.ParseColor("#696969");

    private Color _color = Danger;
    private readonly int _basePaddingTopPx;
    private int _topInsetPx;

    public AlertStrip(Context context) : base(context)
    {
        Gravity = GravityFlags.Center;
        SetTextColor(Color.White);
        SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        SetTextSize(ComplexUnitType.Sp, 14);
        _basePaddingTopPx = context.Dp(2);
        SetPadding(context.Dp(6), _basePaddingTopPx, context.Dp(6), context.Dp(2));
        SetBackgroundColor(_color);
        Visibility = ViewStates.Gone;
    }

    /// <summary>
    /// Высота системного статус-бара в пикселях: полоса стоит у той же верхней кромки, что и
    /// панель, и компенсирует бар тем же значением (<see cref="DashboardView.TopInset"/>) —
    /// иначе часы ложатся на её текст (план 22 §1).
    /// </summary>
    public int TopInset
    {
        get => _topInsetPx;
        set
        {
            if (_topInsetPx == value) return;
            _topInsetPx = value;
            SetPadding(PaddingLeft, _basePaddingTopPx + value, PaddingRight, PaddingBottom);
        }
    }

    /// <summary>Правка свойств только при изменении (план 11 §0): вызывается на каждом отсчёте телеметрии.</summary>
    public void Show(string text, Color color)
    {
        if (Text != text) Text = text;
        if (_color != color)
        {
            _color = color;
            SetBackgroundColor(color);
        }
        if (Visibility != ViewStates.Visible) Visibility = ViewStates.Visible;
    }

    public void Hide()
    {
        if (Visibility != ViewStates.Gone) Visibility = ViewStates.Gone;
    }
}
