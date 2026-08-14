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
/// <b>Насквозь для пальца — кроме самой строки.</b> Под наложением живые кнопки, списки и ползунки,
/// и полосы краёв не отнимают у райдера ни одного касания: сам контейнер и <see cref="AlertBarsView"/>
/// не <see cref="View.Clickable"/> и не <see cref="View.Focusable"/>, <c>FrameLayout</c> ведёт
/// разбор вниз, к экрану под наложением. Единственное исключение с 15.08.2026 — <b>строка слов</b>:
/// она ловит касание по себе ради смахивания («убрать любое сообщение смахиванием» — слово
/// владельца), и это осознанный обмен — узкая полоса на время тревоги против возможности её убрать.
/// </para>
/// </summary>
public sealed class AlertOverlayView : FrameLayout
{
    private readonly AlertStrip _strip;
    private readonly AlertBarsView _bars;

    /// <summary>
    /// Смахнутые слова: этот текст строка не показывает, пока тревога не кончится или не сменит
    /// слова. Полосы краёв смахивание не трогает — тревога остаётся видимой, убраны только слова.
    /// </summary>
    private string _hushed = "";

    public AlertOverlayView(Context context, DashboardOptions options, Func<AlertState> alert) : base(context)
    {
        Clickable = false;
        Focusable = false;

        _bars = new AlertBarsView(context, options) { Alert = alert };
        _strip = new AlertStrip(context);
        _strip.Dismissed += shown => _hushed = shown;

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
        // Другие слова — глушение отслужило; те же — строка остаётся убранной, тревога видна полосами.
        if (text != _hushed)
        {
            _hushed = "";
            _strip.Show(text, AlertStrip.Danger);
        }

        // Пинок из тишины: слова появляются тем же событием банера, что и тревога, а дальше мигание
        // и рост силы элемент ведёт сам, своим кадровым циклом.
        _bars.Invalidate();
        if (Visibility != ViewStates.Visible) Visibility = ViewStates.Visible;
    }

    public void Hide()
    {
        if (Visibility != ViewStates.Gone) Visibility = ViewStates.Gone;
        // Тревога кончилась — глушение снимается: следующая обязана показаться и словами тоже.
        _hushed = "";
    }
}
