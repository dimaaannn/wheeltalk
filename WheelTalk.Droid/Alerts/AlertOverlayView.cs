using Android.Content;
using Android.Views;
using Android.Widget;
using WheelTalk.Core.Alerts;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Droid.Alerts;

/// <summary>
/// Тревога поверх обычного экрана: полосы сверху и снизу — тем же самостоятельным элементом, что на
/// главном экране (<see cref="AlertBarsView"/>), — и строка со словами над ними.
/// <para>
/// <b>Насквозь для пальца.</b> Под наложением живые кнопки, списки и ползунки, и тревога не смеет
/// отнимать у райдера ни одного касания. Держится это не настройкой флагов, а тем, что здесь нет ни
/// одного обработчика: <c>View</c>, которая не <see cref="View.Clickable"/> и не
/// <see cref="View.Focusable"/>, возвращает из <c>onTouchEvent</c> ложь, а <c>FrameLayout</c>
/// продолжает разбор по своим детям вниз, к экрану под наложением. Оба свойства выставлены явно —
/// умолчание тут слишком дорого стоит, чтобы полагаться на память.
/// </para>
/// </summary>
public sealed class AlertOverlayView : FrameLayout
{
    private readonly AlertStrip _strip;
    private readonly AlertBarsView _bars;

    public AlertOverlayView(Context context, DashboardOptions options, Func<AlertState> alert) : base(context)
    {
        Clickable = false;
        Focusable = false;

        _bars = new AlertBarsView(context, options) { Alert = alert };
        _strip = new AlertStrip(context);

        // Полосы — нижним слоем, слова — поверх: полоса тревоги в полный голос выше строки, и
        // текст, накрытый мигающим прямоугольником, читался бы урывками.
        AddView(_bars, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        AddView(_strip, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = GravityFlags.Top,
        });
    }

    /// <summary>Высота статус-бара: строка встаёт ниже него, иначе часы ложатся на её текст.</summary>
    public int TopInset
    {
        set => _strip.TopInset = value;
    }

    public void Show(string text)
    {
        _strip.Show(text, AlertStrip.Danger);
        // Пинок из тишины: слова появляются тем же событием банера, что и тревога, а дальше мигание
        // и рост силы элемент ведёт сам, своим кадровым циклом.
        _bars.Invalidate();
        if (Visibility != ViewStates.Visible) Visibility = ViewStates.Visible;
    }

    public void Hide()
    {
        if (Visibility != ViewStates.Gone) Visibility = ViewStates.Gone;
    }
}
