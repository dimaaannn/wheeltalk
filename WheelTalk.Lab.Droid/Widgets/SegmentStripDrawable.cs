using Android.Graphics;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Lab.Droid.Widgets;

/// <summary>
/// Линейка сегментов — это shift lights гоночных дэшей, поставленные вертикально. Гонщик не
/// читает тахометр, он видит, сколько огней горит; тот же счёт работает и для ШИМ. Дискретность
/// здесь свойство, а не упрощение: «семь из десяти» считывается быстрее положения указателя и не
/// дёргается от шума в младшем разряде.
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/SegmentStripDrawable.cs</c>: зазор и толщина
/// бирки домножены на плотность экрана, <c>Color.WithAlpha</c> заменён на
/// <see cref="Color.Argb(int, int, int, int)"/> — та же полусила, что у MAUI-исходника.
/// </para>
/// </summary>
public sealed class SegmentStripDrawable
{
    private const float Gap = 3;

    private readonly Paint _fill = new() { AntiAlias = true };
    private readonly Paint _stroke = new() { AntiAlias = true };

    public SegmentStripDrawable() => _stroke.SetStyle(Paint.Style.Stroke);

    public required DashboardOptions Options { get; init; }

    public double Value { get; set; }

    /// <summary>Куда придёт через пару секунд: сегменты прогноза светятся вполсилы.</summary>
    public double? Trend { get; set; }

    public double? Bug { get; set; }

    public void Draw(Canvas canvas, RectF rect, float density)
    {
        var palette = Options.Palette;
        float gap = Gap * density;
        int segments = Math.Max(1, (int)Math.Round((Options.ScaleMax - Options.ScaleMin) / Options.SegmentPercent));
        float height = (rect.Height() - gap * (segments - 1)) / segments;
        if (height <= 0) return;

        double lit = Options.Fraction(Value) * segments;
        double predicted = Options.ShowTrend && Trend is { } trend ? Options.Fraction(trend) * segments : 0;

        for (int i = 0; i < segments; i++)
        {
            double segmentValue = Options.ScaleMin + (i + 0.5) * Options.SegmentPercent;
            float top = rect.Bottom - (i + 1) * height - i * gap;

            _fill.Color = (i < lit, i < predicted) switch
            {
                (true, _) => palette.ForPwm(segmentValue, Options),
                (false, true) => WithAlpha(palette.ForPwm(segmentValue, Options), 0.35f),
                _ when Options.ShowBarberPole && segmentValue >= Options.BarberPolePwm => Color.Argb(55, 255, 255, 255),
                _ => Color.Argb(20, 255, 255, 255),
            };
            canvas.DrawRect(rect.Left, top, rect.Right, top + height, _fill);
        }

        if (!Options.ShowBug || Bug is not { } bug || bug <= 0) return;

        float bugY = rect.Bottom - (float)(Options.Fraction(bug) * rect.Height());
        _stroke.Color = palette.Accent;
        _stroke.StrokeWidth = 3 * density;
        canvas.DrawLine(rect.Left - 6 * density, bugY, rect.Right + 6 * density, bugY, _stroke);
    }

    private static Color WithAlpha(Color color, float alpha) =>
        Color.Argb((int)Math.Round(alpha * 255), color.R, color.G, color.B);
}
