using Android.Content;
using Android.Graphics;
using Android.Views;
using WheelTalk.Core.Alerts;

namespace WheelTalk.Dashboard.Droid.Widgets;

/// <summary>
/// Полосы тревоги как самостоятельный элемент — слово владельца 05.08.2026: «полосы не принадлежат
/// экрану и не дублируются на каждом». Рисование (<see cref="AlertBarsDrawable"/>), ритм моргания и
/// правило толщины живут здесь одним экземпляром кода; экраны и окна лишь дают элементу место:
/// рамка главного экрана держит его поверх сменного содержимого (<c>MainScreenView.Bars</c>),
/// прочие экраны приложения — наложением (<c>AlertOverlayView</c>), стенд — поверх своего хоста.
/// <para>
/// <b>Задел под «поверх всех окон».</b> Элемент не знает, в чьём окне живёт: ему нужны контекст,
/// настройки и источник тревоги (<see cref="Alert"/>) — ни <c>Activity</c>, ни разметки, ни
/// инсетов. Когда владелец попросит тревогу поверх чужих приложений, этот же класс встанет в окно
/// <c>TYPE_APPLICATION_OVERLAY</c> через <c>WindowManager.AddView</c> — новый крепёж под настройкой,
/// а не переписывание (docs/roadmap.md: разрешение спрашивается только под эту опцию).
/// </para>
/// <para>
/// Сила тревоги <b>спрашивается на кадре, а не приходит событием</b>: она меняется непрерывно, и
/// полоса растёт за ней без отдельного сигнала. Пока тревога в голос, элемент перезаказывает кадры
/// сам; из тишины его будит хозяин обычным <see cref="View.Invalidate()"/> — по своему кадровому
/// циклу либо по событию, у кого что есть.
/// </para>
/// <para>
/// <b>Насквозь для пальца</b> — тем же порядком, что было у наложения: ни одного обработчика, оба
/// свойства сняты явно, и касание уходит экрану под полосами.
/// </para>
/// </summary>
public sealed class AlertBarsView : View
{
    private readonly AlertBarsDrawable _bars;
    private readonly DashboardOptions _options;

    public AlertBarsView(Context context, DashboardOptions options) : base(context)
    {
        _options = options;
        _bars = new AlertBarsDrawable { Options = options };
        Clickable = false;
        Focusable = false;
    }

    /// <summary>Откуда берётся тревога. Не задан — полос нет: элемент без источника молчит, а не падает.</summary>
    public Func<AlertState>? Alert { get; set; }

    /// <summary>
    /// Доля высоты окна на полосу <b>в полный голос</b> — четверть (решение владельца 05.08.2026).
    /// <para>
    /// Мера одна на все экраны: полосы — самостоятельный элемент, и одна тревога не должна выглядеть
    /// двумя разными. Прежняя панельная доля — от меньшей стороны — на вертикальном экране давала
    /// 4,4 % высоты даже при ста процентах ШИМ, то есть ниточку там, где нужен крик.
    /// </para>
    /// <para>
    /// <b>Четверть отдана крайнему случаю нарочно.</b> Полный голос — это ШИМ у самого предела, до
    /// которого в поездке почти не доходят; там уже неважно, что полоса закрыла шкалы лент. На
    /// пороге тревоги она впятеро тоньше (<c>AlertBarsDrawable.MinShare</c>) — двадцатая часть
    /// высоты, и приборы читаются.
    /// </para>
    /// </summary>
    public float HeightShare { get; set; } = FullShare;

    /// <summary>Четверть высоты на полосу в полный голос; на пороге тревоги — пятая часть от неё.</summary>
    public const float FullShare = 0.25f;

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);

        var state = Alert?.Invoke() ?? AlertState.Quiet;

        _bars.Intensity = state.PwmIntensity;
        _bars.SpeedExceeded = state.SpeedExceeded;

        // Ритм по часам, а не переключением раз в кадр: при плавающей частоте экрана он плавал бы
        // вместе с ней вместо заданных BlinkHz. Ноль в настройке значит «не моргать» (решение
        // владельца 05.08.2026) — полоса горит ровно, тем же нулём в приложении выключаются пороги.
        double period = _options.BlinkHz > 0 ? 1000 / _options.BlinkHz : 0;
        _bars.Lit = period <= 0 || Environment.TickCount64 % period < period / 2;

        _bars.Draw(canvas, new RectF(0, 0, Width, Height), Height * HeightShare);

        // Кадровый цикл заводится тревогой и гаснет вместе с ней: мигание и рост силы не ждут
        // хозяина. Мягкая тревога не мигает и цикла не держит — её снятие принесёт пинок хозяина.
        if (state.PwmAlarming) PostInvalidateOnAnimation();
    }
}
