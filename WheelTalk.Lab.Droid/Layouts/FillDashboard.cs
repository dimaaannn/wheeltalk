using Android.Content;
using Android.Graphics;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Widgets;
using WheelTalk.Lab.Droid.Widgets;

namespace WheelTalk.Lab.Droid.Layouts;

/// <summary>
/// Вариант G — приборов нет, индикатор и есть экран. Проверяет сильное утверждение: цвет и
/// площадь сильнее цифр. Если оно верно, это самый дешёвый экран из возможных; если нет, станет
/// видно сразу — «сколько осталось до предела» здесь читается только на глаз.
/// <para>
/// Стоя экран был бы просто чёрным, и это тот случай, когда пусто — плохо: на стоянке как раз и
/// хочется видеть заряд, напряжение и одометр. Поэтому компоновка от контекста: стоит — цифры
/// есть, поехал — уходят.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Layouts/FillDashboard.cs</c>. Кегль 150 сохранён: ради
/// него вариант и существует — это 82 угловые минуты с руля, вчетверо выше рекомендуемого
/// ISO 15008, и помещается он только без десятых.
/// </para>
/// </summary>
public sealed class FillDashboard : DashboardView
{
    private const float ChargeWidth = 10;
    private const float Spacing = 6;
    private const float StandingFontSize = 30;
    private const float StandingBottomMargin = 40;

    private readonly FillDrawable _fill;
    private readonly ChargeBarDrawable _charge;
    private readonly SpeedDigitDrawable _speed;
    private readonly Paint _standing = new() { AntiAlias = true };

    public FillDashboard(Context context, DashboardOptions options) : base(context, options)
    {
        _fill = new FillDrawable { Options = options };
        _charge = new ChargeBarDrawable { Options = options, Horizontal = false };
        _speed = new SpeedDigitDrawable(100, showUnit: false) { Options = options, GrownFontSize = 150 };
        _standing.SetTypeface(Typeface.Monospace);
    }

    protected override void DrawPanel(Canvas canvas, RectF content)
    {
        float bar = ChargeWidth * Density;
        float gap = Spacing * Density;

        _charge.Battery = Reading.Battery;
        _charge.Draw(canvas, new RectF(content.Left, content.Top, content.Left + bar, content.Bottom), Density);

        var area = new RectF(content.Left + bar + gap, content.Top, content.Right, content.Bottom);

        _fill.Value = Reading.Pwm;
        _fill.Trend = Trend;
        _fill.Draw(canvas, area, Density);

        _speed.Draw(canvas, area, Reading, Density);

        if (!Reading.Standing) return;

        float font = StandingFontSize * Density;
        float row = font * 1.35f;
        float bottom = area.Bottom - StandingBottomMargin * Density;

        _standing.Color = Options.Palette.Ink;
        _standing.TextSize = font;

        Line(canvas, area, bottom - row * 3, row, $"{Reading.Battery} %");
        Line(canvas, area, bottom - row * 2, row, $"{Reading.VoltageV:F1} В");
        Line(canvas, area, bottom - row, row, $"{Reading.TripKm:F1} км");
    }

    private void Line(Canvas canvas, RectF area, float top, float height, string text) =>
        canvas.DrawString(_standing, text, area.Left, top, area.Width(), height, HAlign.Center, VAlign.Center);
}
