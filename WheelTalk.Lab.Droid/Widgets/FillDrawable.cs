using Android.Graphics;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Lab.Droid.Widgets;

/// <summary>
/// Приборов нет — индикатор и есть экран: ШИМ заливает рабочую область снизу вверх. Главный
/// элемент здесь не цвет и не цифра, а резкая граница заливки: положение горизонтальной линии
/// периферия ловит лучше всего, а высота дублирует значение формой, а не только тоном.
/// <para>
/// Цифра ШИМ едет вместе с границей и постоянного места не имеет — это сознательно: её положение
/// говорит то же, что её значение.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/FillDrawable.cs</c>: пунктир линии прогноза
/// собран через <see cref="DashPathEffect"/> (у MAUI это было свойство канвы
/// <c>StrokeDashPattern</c>), абсолютные величины домножены на плотность экрана.
/// </para>
/// </summary>
public sealed class FillDrawable
{
    private readonly Paint _fill = new() { AntiAlias = true };
    private readonly Paint _stroke = new() { AntiAlias = true };
    private readonly Paint _dashed = new() { AntiAlias = true };
    private readonly Paint _bold = new() { AntiAlias = true };

    public FillDrawable()
    {
        _stroke.SetStyle(Paint.Style.Stroke);
        _dashed.SetStyle(Paint.Style.Stroke);
        _bold.SetTypeface(Typeface.DefaultBold);
    }

    public required DashboardOptions Options { get; init; }

    public double Value { get; set; }

    /// <summary>Куда граница придёт через пару секунд — тонкая линия выше заливки.</summary>
    public double? Trend { get; set; }

    public void Draw(Canvas canvas, RectF rect, float density)
    {
        var palette = Options.Palette;
        float height = (float)(Options.Fraction(Value) * rect.Height());
        float edge = rect.Bottom - height;

        if (height > 0)
        {
            _fill.Color = palette.ForPwm(Value, Options);
            canvas.DrawRect(rect.Left, edge, rect.Right, edge + height, _fill);

            _stroke.Color = palette.Ink;
            _stroke.StrokeWidth = 4 * density;
            canvas.DrawLine(rect.Left, edge, rect.Right, edge, _stroke);
        }

        if (Options.ShowTrend && Trend is { } trend)
        {
            float trendY = rect.Bottom - (float)(Options.Fraction(trend) * rect.Height());
            if (Math.Abs(trendY - edge) > 3 * density)
            {
                _dashed.Color = palette.Accent;
                _dashed.StrokeWidth = 2 * density;
                _dashed.SetPathEffect(new DashPathEffect([6 * density, 4 * density], 0));
                canvas.DrawLine(rect.Left, trendY, rect.Right, trendY, _dashed);
            }
        }

        if (Options.ShowBug && Options.PersonalLimit > 0)
        {
            float bugY = rect.Bottom - (float)(Options.Fraction(Options.PersonalLimit) * rect.Height());
            _stroke.Color = palette.Accent;
            _stroke.StrokeWidth = 3 * density;
            canvas.DrawLine(rect.Right - 40 * density, bugY, rect.Right, bugY, _stroke);
        }

        // Цифра всегда рядом с границей, но не за краем экрана: у самого верха она уходит внутрь
        // заливки, у самого низа — поднимается над ней. Запас сверху — в целую высоту строки:
        // при заливке до упора граница совпадает с краем, и цифра иначе наполовину срезается.
        float digitY = Math.Clamp(edge - 46 * density, rect.Top + 56 * density, rect.Bottom - 56 * density);
        _bold.Color = palette.Ink;
        _bold.TextSize = 44 * density;
        canvas.DrawString(_bold, $"{Value:F0} %", rect.Right - 260 * density, digitY,
            250 * density, 52 * density, HAlign.Right, VAlign.Center);
    }
}
