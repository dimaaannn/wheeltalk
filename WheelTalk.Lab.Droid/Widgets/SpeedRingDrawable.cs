using Android.Graphics;
using WheelTalk.Dashboard.Droid;

namespace WheelTalk.Lab.Droid.Widgets;

/// <summary>
/// Кольцо вокруг цифры скорости. Цифра точнее любой фигуры, когда нужно значение, но её нельзя
/// прочесть краем глаза; кольцо добавляет к ней то, чего у цифры нет, — форму, которая меняется
/// и ловится периферией, не отнимая у цифры ни места, ни размера.
/// <para>
/// Тонкая риска впереди заливки — куда скорость придёт через пару секунд при нынешнем ускорении.
/// Тот же приём, что вектор тренда на ленте, и здесь он отвечает на вопрос «разгон или уже нет»
/// без второй цифры.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/SpeedRingDrawable.cs</c>: углы пересчитаны в
/// систему Android (см. <see cref="Angles"/>), абсолютные величины домножены на плотность экрана.
/// </para>
/// </summary>
public sealed class SpeedRingDrawable
{
    private const float StartAngle = 225;
    private const float Sweep = 270;

    private readonly Paint _stroke = new() { AntiAlias = true };
    private readonly RectF _box = new();

    public SpeedRingDrawable() => _stroke.SetStyle(Paint.Style.Stroke);

    public required DashboardOptions Options { get; init; }

    public double SpeedKmh { get; set; }

    /// <summary>Куда придёт через горизонт прогноза; null — не рисовать.</summary>
    public double? Trend { get; set; }

    public float Thickness { get; set; } = 10;

    public void Draw(Canvas canvas, RectF rect, float density)
    {
        var palette = Options.Palette;
        float thickness = Thickness * density;
        float radius = Math.Min(rect.Width(), rect.Height()) / 2 - thickness / 2 - 2 * density;
        if (radius <= 0 || Options.ReferenceSpeed <= 0) return;

        _box.Set(rect.CenterX() - radius, rect.CenterY() - radius, rect.CenterX() + radius, rect.CenterY() + radius);

        _stroke.StrokeWidth = thickness;
        _stroke.StrokeCap = Paint.Cap.Butt;

        _stroke.Color = Color.Argb(22, 255, 255, 255);
        canvas.DrawArc(_box, Angles.Start(StartAngle), Sweep, false, _stroke);

        float filled = (float)Math.Clamp(SpeedKmh / Options.ReferenceSpeed, 0, 1);
        if (filled > 0.001f)
        {
            _stroke.Color = palette.Ink;
            canvas.DrawArc(_box, Angles.Start(StartAngle), Sweep * filled, false, _stroke);
        }

        if (!Options.ShowTrend || Trend is not { } trend) return;

        float predicted = (float)Math.Clamp(trend / Options.ReferenceSpeed, 0, 1);
        if (Math.Abs(predicted - filled) < 0.01f) return;

        double angle = (StartAngle - Sweep * predicted) * Math.PI / 180;
        _stroke.Color = palette.Accent;
        _stroke.StrokeWidth = 3 * density;
        canvas.DrawLine(
            rect.CenterX() + (float)(Math.Cos(angle) * (radius - thickness)),
            rect.CenterY() - (float)(Math.Sin(angle) * (radius - thickness)),
            rect.CenterX() + (float)(Math.Cos(angle) * (radius + thickness)),
            rect.CenterY() - (float)(Math.Sin(angle) * (radius + thickness)),
            _stroke);
    }
}
