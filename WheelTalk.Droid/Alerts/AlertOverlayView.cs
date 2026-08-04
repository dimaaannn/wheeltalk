using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using WheelTalk.Core.Alerts;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Droid.Alerts;

/// <summary>
/// Тревога поверх обычного экрана: те же полосы сверху и снизу, что на панели
/// (<see cref="AlertBarsDrawable"/>), и строка со словами над ними.
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
    private readonly BarsView _bars;

    public AlertOverlayView(Context context, DashboardOptions options, Func<AlertState> alert) : base(context)
    {
        Clickable = false;
        Focusable = false;

        _bars = new BarsView(context, options, alert);
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
        _bars.Invalidate();
        if (Visibility != ViewStates.Visible) Visibility = ViewStates.Visible;
    }

    public void Hide()
    {
        if (Visibility != ViewStates.Gone) Visibility = ViewStates.Gone;
    }

    /// <summary>
    /// Полосы. Собственного состояния нет вовсе — сила тревоги спрашивается на каждом кадре у общего
    /// источника, а рисует их тот же <see cref="AlertBarsDrawable"/>, что и панель: те же цвета, то
    /// же правило «сила множит толщину», тот же порядок «по ШИМ громче, чем по скорости». Своя здесь
    /// только доля экрана (<see cref="HeightShare"/>) — единственное, чем эти полосы отличаются от
    /// панельных, и отличаются они по делу.
    /// </summary>
    private sealed class BarsView(Context context, DashboardOptions options, Func<AlertState> alert) : View(context)
    {
        /// <summary>
        /// Доля <b>высоты</b> экрана на полосу в полный голос — решение владельца 05.08.2026.
        /// Панель считает свою от меньшей стороны и получает 4,4 % высоты: там под полосами приборы,
        /// и расти им некуда. Здесь под ними списки и кнопки, которые тревога и так перекрывать не
        /// должна лишь пальцем, — места хватает, и полоса становится видна как полоса, а не как
        /// ниточка у кромки.
        /// </summary>
        private const float HeightShare = 0.15f;

        private readonly AlertBarsDrawable _bars = new() { Options = options };

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);

            var state = alert();

            _bars.Intensity = state.PwmIntensity;
            _bars.SpeedExceeded = state.SpeedExceeded;

            // Ритм тот же, что у панели, и считается по часам, а не переключением раз в кадр: при
            // плавающей частоте экрана он плавал бы вместе с ней вместо заданных BlinkHz.
            //
            // Ноль в настройке значит «не моргать» (решение владельца 05.08.2026): полоса горит
            // ровно. Это не заглушка, а выбор человека — тем же нулём в этом приложении выключаются
            // пороги тревог.
            double period = options.BlinkHz > 0 ? 1000 / options.BlinkHz : 0;
            _bars.Lit = period <= 0 || Environment.TickCount64 % period < period / 2;

            _bars.Draw(canvas, new RectF(0, 0, Width, Height), Height * HeightShare);

            // Кадровый цикл заводится тревогой и гаснет вместе с ней: сила тревоги меняется
            // непрерывно, и полоса растёт за ней без отдельного события.
            if (state.PwmAlarming) PostInvalidateOnAnimation();
        }
    }
}
