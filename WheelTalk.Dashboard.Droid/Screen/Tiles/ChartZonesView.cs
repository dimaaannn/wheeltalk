using Android.Content;
using Android.Graphics;
using Android.Views;
using Com.Github.Mikephil.Charting.Charts;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Пороги поперёк графика — двумя чертами: жёлтой и красной. Видно, докуда дотянулся ход величины и
/// сколько осталось до предела.
/// <para>
/// <b>Черта, а не залитая зона.</b> Залитая полоса накладывалась на синюю заливку под линией, и оба
/// цвета сливались в грязь (проверено глазами 05.08.2026) — а над пустым местом, где данных нет, она
/// вдобавок висела ни на чём. Черта не спорит ни с чем и говорит ровно то же.
/// </para>
/// <para>
/// Так вышло проще прежнего: цвет — свойство шкалы, а не линии. Раньше он задавался каждой точке
/// отдельно, и библиотека рисовала линию тысячей сегментов — на плитке шириной в палец они
/// вырождались, и линия пропадала кусками. Теперь линия одноцветная и идёт одним проходом, а
/// «опасно» рисуется дважды за кадр двумя прямоугольниками.
/// </para>
/// <para>Касаний не ловит: это подсказка, а не орган управления.</para>
/// </summary>
internal sealed class ChartZonesView : View
{
    private readonly Paint _line = new() { AntiAlias = true };
    private readonly LineChart _chart;
    private readonly DashboardPalette _palette;

    private TileLimits? _limits;

    public ChartZonesView(Context context, LineChart chart, DashboardPalette palette) : base(context)
    {
        _chart = chart;
        _palette = palette;

        _line.SetStyle(Paint.Style.Stroke);
        _line.StrokeWidth = context.Dp(TilesLayout.ChartStrokeDp);
        _line.SetPathEffect(new DashPathEffect(
            [context.Dp(TilesLayout.LimitDashDp), context.Dp(TilesLayout.LimitDashDp)], 0));

        Clickable = false;
        Focusable = false;
    }

    /// <summary>Пороги плитки. <c>null</c> — зон нет: величине не от чего тревожиться.</summary>
    public TileLimits? Limits
    {
        set
        {
            _limits = value;
            Invalidate();
        }
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);

        if (_limits is not { } limits || _chart.Data is null) return;

        // Границы шкалы берём у самого графика: она подвижна — обрезка по крайним значениям меняет
        // её на каждом обновлении, и считать зону по своим числам значило бы разъехаться с линией.
        float low = _chart.AxisLeft!.AxisMinimum;
        float high = _chart.AxisLeft.AxisMaximum;
        var content = _chart.ContentRect;

        if (high <= low || content is null || content.Height() <= 0) return;

        Mark(canvas, content, low, high, limits.Warn, _palette.Caution);
        Mark(canvas, content, low, high, limits.Danger, _palette.Danger);
    }

    /// <summary>Черта на уровне порога. Порог вне видимой шкалы не рисуется: черта у кромки соврала бы.</summary>
    private void Mark(Canvas canvas, RectF content, float low, float high, double at, Color color)
    {
        if (at <= low || at >= high) return;

        float y = content.Bottom - (float)(at - low) / (high - low) * content.Height();

        _line.Color = color;
        canvas.DrawLine(content.Left, y, content.Right, y, _line);
    }
}
