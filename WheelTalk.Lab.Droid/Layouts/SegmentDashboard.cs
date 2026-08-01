using Android.Content;
using Android.Graphics;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Widgets;
using WheelTalk.Lab.Droid.Widgets;

namespace WheelTalk.Lab.Droid.Layouts;

/// <summary>
/// Вариант F — линейка сегментов вдоль края и крупная цифра скорости. Приём взят у гоночных дэшей,
/// где обороты дублируются линейкой огней: гонщик не читает тахометр, он видит, сколько огней
/// горит. Проверяем ровно это — считается ли «сколько горит» быстрее, чем оценивается положение
/// указателя на непрерывной шкале.
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Layouts/SegmentDashboard.cs</c> с теми же кеглями:
/// трёхзначный ШИМ — не аномалия (MTen3 на раскруте отдал 110), и подпись под скоростью держится
/// на 36, а не на 44, чтобы «ШИМ 105 %» не переносилось.
/// </para>
/// </summary>
public sealed class SegmentDashboard : DashboardView
{
    private const float StripWidth = 44;
    private const float ChargeHeight = 44;
    private const float PwmFontSize = 36;
    private const float Padding = 8;
    private const float Spacing = 10;

    private readonly SegmentStripDrawable _strip;
    private readonly ChargeBarDrawable _charge;
    private readonly SpeedDigitDrawable _speed;
    private readonly Paint _pwm = new() { AntiAlias = true };

    public SegmentDashboard(Context context, DashboardOptions options) : base(context, options)
    {
        _strip = new SegmentStripDrawable { Options = options };
        _charge = new ChargeBarDrawable { Options = options };
        _speed = new SpeedDigitDrawable(90) { Options = options, GrownFontSize = 140 };
        _pwm.SetTypeface(Typeface.Create(Typeface.Monospace, TypefaceStyle.Bold));
    }

    protected override void DrawPanel(Canvas canvas, RectF content)
    {
        float pad = Padding * Density;
        float gap = Spacing * Density;
        float strip = StripWidth * Density;
        float charge = ChargeHeight * Density;
        float pwmFont = PwmFontSize * Density;

        _strip.Value = Reading.Pwm;
        _strip.Trend = Trend;
        _strip.Bug = Options.PersonalLimit;
        _strip.Draw(canvas, new RectF(content.Left + 6 * Density, content.Top + 6 * Density,
            content.Left + 6 * Density + strip, content.Bottom), Density);

        float left = content.Left + 6 * Density + strip + gap;
        float right = content.Right - pad;
        float bottom = content.Bottom - pad;

        _charge.Battery = Reading.Battery;
        _charge.VoltageV = Reading.VoltageV;
        _charge.Draw(canvas, new RectF(left, bottom - charge, right, bottom), Density);

        float pwmTop = bottom - charge - gap - pwmFont * 1.3f;
        _pwm.Color = Options.Palette.ForPwm(Reading.Pwm, Options);
        _pwm.TextSize = pwmFont;
        canvas.DrawString(_pwm, $"ШИМ {Reading.Pwm:F0} %", left, pwmTop, right - left, pwmFont * 1.3f,
            HAlign.Center, VAlign.Center);

        _speed.Draw(canvas, new RectF(left, content.Top, right, pwmTop - gap), Reading, Density);
    }
}
