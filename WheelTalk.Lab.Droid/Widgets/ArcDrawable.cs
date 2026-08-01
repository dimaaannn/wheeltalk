using Android.Graphics;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Lab.Droid.Widgets;

/// <summary>
/// Дуга ШИМ на 270° — та же фигура, к которой райдеры привыкли в WheelLog, но с перевёрнутой
/// иерархией: на дуге здесь ШИМ (важно изменение), а цифра внутри — скорость (важно значение).
/// <para>
/// Дуга разбита на сегменты, а не залита непрерывно, и это не украшение: счёт «сколько горит»
/// считывается быстрее, чем положение указателя, и дискретность гасит дрожание младшего разряда,
/// не сглаживая скачок — а именно скачок ШИМ и важен.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/ArcDrawable.cs</c>. Две правки, обе платформенные:
/// углы пересчитаны из системы MAUI (против часовой стрелки от востока) в систему
/// <see cref="Canvas.DrawArc(RectF, float, float, bool, Paint)"/> (по часовой) — см.
/// <see cref="Angles"/>, — и абсолютные величины домножены на плотность экрана. Пороги, доли и
/// логика раскраски сегментов перенесены без изменений.
/// </para>
/// </summary>
public sealed class ArcDrawable
{
    private const float StartAngle = 225;
    private const float Sweep = 270;
    private const float SegmentGap = 1.5f;

    private readonly Paint _stroke = new() { AntiAlias = true };
    private readonly Paint _text = new() { AntiAlias = true };
    private readonly RectF _box = new();

    public ArcDrawable() => _stroke.SetStyle(Paint.Style.Stroke);

    public required DashboardOptions Options { get; init; }

    public double Value { get; set; }

    /// <summary>Пик за последние секунды — тонкая риска, показывает, где значение только что было.</summary>
    public double? Peak { get; set; }

    public double? Bug { get; set; }

    /// <summary>Толщину задаёт раскладка: в варианте E она растёт внутрь при подходе к пределу.</summary>
    public float Thickness { get; set; } = 26;

    public void Draw(Canvas canvas, RectF rect, float density)
    {
        var palette = Options.Palette;
        float thickness = Thickness * density;
        float radius = Math.Min(rect.Width(), rect.Height()) / 2 - thickness / 2 - 2 * density;
        if (radius <= 0) return;

        float centerX = rect.CenterX();
        float centerY = rect.Top + Math.Min(rect.Width(), rect.Height()) / 2;
        _box.Set(centerX - radius, centerY - radius, centerX + radius, centerY + radius);

        int segments = Math.Max(1, (int)Math.Round((Options.ScaleMax - Options.ScaleMin) / Options.SegmentPercent));
        float step = Sweep / segments;
        double filled = Options.Fraction(Value) * segments;

        _stroke.StrokeWidth = thickness;
        _stroke.StrokeCap = Paint.Cap.Butt;

        for (int i = 0; i < segments; i++)
        {
            double segmentValue = Options.ScaleMin + (i + 0.5) * Options.SegmentPercent;
            bool lit = i < filled;

            // Незажжённые сегменты выше предела не гасятся в фон, а белеют: верх шкалы должен
            // выглядеть горячим и тогда, когда указатель до него ещё не дошёл — это и есть
            // авиационный «barber pole», перенесённый на дугу.
            _stroke.Color = (lit, Options.ShowBarberPole && segmentValue >= Options.BarberPolePwm) switch
            {
                (true, _) => palette.ForPwm(segmentValue, Options),
                (false, true) => Color.Argb(60, 255, 255, 255),
                (false, false) => Color.Argb(22, 255, 255, 255),
            };

            canvas.DrawArc(_box, Angles.Start(StartAngle - i * step), step - SegmentGap, false, _stroke);
        }

        DrawMark(canvas, radius, centerX, centerY, thickness, Peak, palette.Ink, 3 * density);
        if (Options.ShowBug && Bug is { } bug && bug > 0)
        {
            DrawMark(canvas, radius, centerX, centerY, thickness, bug, palette.Accent, 5 * density);
        }

        _text.Color = palette.Dim;
        _text.TextSize = 16 * density;
        float label = 24 * density;
        canvas.DrawString(_text, Options.ScaleMin.ToString("F0"),
            _box.Left - 6 * density, _box.Bottom - label, 40 * density, label, HAlign.Center, VAlign.Center);
        canvas.DrawString(_text, Options.ScaleMax.ToString("F0"),
            _box.Right - 34 * density, _box.Bottom - label, 40 * density, label, HAlign.Center, VAlign.Center);
    }

    /// <summary>Риска поперёк дуги — тем же радиусом, что и она сама, чтобы не спорить с заливкой.</summary>
    private void DrawMark(Canvas canvas, float radius, float centerX, float centerY, float thickness,
        double? value, Color color, float width)
    {
        if (value is not { } mark) return;

        double angle = (StartAngle - Options.Fraction(mark) * Sweep) * Math.PI / 180;
        float inner = radius - thickness / 2;
        float outer = radius + thickness / 2;

        _stroke.Color = color;
        _stroke.StrokeWidth = width;
        canvas.DrawLine(
            centerX + (float)(Math.Cos(angle) * inner),
            centerY - (float)(Math.Sin(angle) * inner),
            centerX + (float)(Math.Cos(angle) * outer),
            centerY - (float)(Math.Sin(angle) * outer),
            _stroke);
    }
}

/// <summary>
/// Углы дуг в двух системах. MAUI-канва считает их как в математике — против часовой стрелки от
/// востока, — а <see cref="Canvas.DrawArc(RectF, float, float, bool, Paint)"/> по часовой, потому
/// что ось Y на экране смотрит вниз. Перенос сводится к смене знака у начала и к тому, что размах
/// у Android положительный там, где MAUI шёл «clockwise: true».
/// </summary>
internal static class Angles
{
    public static float Start(float mauiAngle) => -mauiAngle;
}
