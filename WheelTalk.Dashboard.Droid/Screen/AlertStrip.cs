using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Полоса тревоги колеса поверх панели: строка на всю ширину, всплывает раз в поездку, если
/// всплывает вообще. Цвет — часть сообщения, поэтому <see cref="Show"/> принимает его явно:
/// <see cref="Danger"/> — тревога самого колеса, <see cref="Notice"/> — служебное
/// («ещё раз — выход»). Слова, как везде в библиотеке, даёт вызывающий.
/// </summary>
public sealed class AlertStrip : TextView
{
    public static readonly Color Danger = Color.ParseColor("#B00020");
    public static readonly Color Notice = Color.ParseColor("#696969");

    private Color _color = Danger;
    private int _topInsetPx;

    public AlertStrip(Context context) : base(context)
    {
        Gravity = GravityFlags.Center;
        SetTextColor(Color.White);
        SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        SetTextSize(ComplexUnitType.Sp, 14);
        SetPadding(context.Dp(6), context.Dp(2), context.Dp(6), context.Dp(2));
        SetBackgroundColor(_color);
        Visibility = ViewStates.Gone;
    }

    /// <summary>
    /// Высота системного статус-бара в пикселях: полоса стоит ниже него, а не под ним (иначе часы
    /// ложатся на её текст, план 22 §1). Ставит его хозяин рамки и только он: полоса — накладка на
    /// верх сцены, и разметка её ниже часов больше не двигает. Отступ — <em>margin</em>, а не
    /// паддинг: паддинг красится
    /// фоном полосы и раздувал её на весь инсет (на эмуляторе — в четверть экрана), margin остаётся
    /// прозрачным, и высота цветной полосы растёт от текста вниз, а не от верхней кромки.
    /// </summary>
    public int TopInset
    {
        get => _topInsetPx;
        set
        {
            if (_topInsetPx == value) return;
            _topInsetPx = value;
            if (LayoutParameters is ViewGroup.MarginLayoutParams margin)
            {
                margin.TopMargin = value;
                LayoutParameters = margin;
            }
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
        TranslationX = 0;
        Alpha = 1f;
    }

    /// <summary>
    /// Сообщение смахнули в сторону (решение владельца 15.08.2026 — «убрать любое сообщение
    /// смахиванием»). Наружу уходит смахнутый текст: полоса лишь прячет себя, а <b>что заглушить и
    /// надолго ли</b> — решает хозяин, у него для этого есть состояние, которого у полосы нет.
    /// </summary>
    public event Action<string>? Dismissed;

    private float _downX;
    private bool _tracking;

    /// <summary>
    /// Смахивание — своим счётом, без GestureDetector: жест один, и три ветки короче обвязки.
    /// Полоса ловит касания только по себе и только пока видима — экран под ней ничего не теряет
    /// (нюанс наложения на чужие экраны: полосы краёв там остаются насквозь, см.
    /// <c>AlertOverlayView</c>).
    /// </summary>
    public override bool OnTouchEvent(MotionEvent? e)
    {
        switch (e?.Action)
        {
            case MotionEventActions.Down:
                _downX = e.RawX;
                _tracking = true;
                return true;

            case MotionEventActions.Move when _tracking:
                TranslationX = e.RawX - _downX;
                // Полоса бледнеет по ходу жеста — палец видит, что тянет её прочь, а не скроллит.
                Alpha = 1f - Math.Min(0.6f, Math.Abs(TranslationX) / Math.Max(Width, 1f));
                return true;

            case MotionEventActions.Up when _tracking:
                _tracking = false;
                bool far = Math.Abs(TranslationX) > Width / 3f;
                string shown = Text ?? "";
                TranslationX = 0;
                Alpha = 1f;
                if (far)
                {
                    Hide();
                    Dismissed?.Invoke(shown);
                }
                return true;

            case MotionEventActions.Cancel:
                _tracking = false;
                TranslationX = 0;
                Alpha = 1f;
                return true;
        }

        return base.OnTouchEvent(e);
    }
}
