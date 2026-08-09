#if DEBUG
using Android.Content;
using Android.Graphics;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Droid.Main;

/// <summary>
/// Проверочный вариант панели: тот же <see cref="DashboardView"/> — фон, вуаль устаревших данных,
/// плашка связи, точка записи, — но из приборов только крупная скорость, без лент. Виден невооружённым
/// глазом, и этого от него довольно: он существует, чтобы выбор варианта (план 17 §3) было чем
/// проверить, а не чтобы им ездили.
/// <para>
/// <b>Только Debug.</b> В Release вариант панели ровно один (решение владельца 09.08.2026), и этого
/// класса в сборке нет вовсе — вместе с ним пропадает и строка выбора в настройках.
/// </para>
/// </summary>
internal sealed class SpeedOnlyDashboard : DashboardView
{
    private readonly SpeedBlockDrawable _centre;

    public SpeedOnlyDashboard(Context context, DashboardOptions options) : base(context, options) =>
        _centre = new SpeedBlockDrawable { Options = options };

    protected override void DrawPanel(Canvas canvas, RectF content)
    {
        _centre.Reading = Reading;
        _centre.Draw(canvas, content);
    }
}
#endif
