using Android.Content;
using Android.Graphics;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Layouts;
using WheelTalk.Dashboard.Droid.Widgets;
using WheelTalk.Dashboard.Droid.Widgets.Tape;
using WheelTalk.Lab.Droid.Widgets;

namespace WheelTalk.Lab.Droid.Layouts;

/// <summary>
/// Вариант D — две ленты по краям, как на авиационном PFD, и ни одной крупной цифры. Не доминирует
/// ничего: доминирует движение. Самый информативный из вариантов и самый требовательный к вниманию —
/// два источника непрерывного движения одновременно, и это главный вопрос к нему.
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Layouts/TapesDashboard.cs</c>. Приём с подписью под
/// значением сохранён: между лентами остаётся 168 dp, и «ПОЕЗДКА 0.4 км» в одну строку туда не
/// влезает — значение сверху, подпись под ним мелко, только так значения держатся на кегле 30.
/// </para>
/// </summary>
public sealed class TapesDashboard : DashboardView
{
    private const float SpeedTapeWidth = 84;
    private const float PwmTapeWidth = 108;
    private const float ChargeHeight = 40;
    private const float ValueFontSize = 30;
    private const float CaptionFontSize = 14;
    private const float Spacing = 10;

    private readonly TapeDrawable _speed;
    private readonly TapeDrawable _pwm;
    private readonly ChargeBarDrawable _charge;
    private readonly Paint _value = new() { AntiAlias = true };
    private readonly Paint _caption = new() { AntiAlias = true };

    public TapesDashboard(Context context, DashboardOptions options) : base(context, options)
    {
        _speed = Tapes.Speed(options, TapeSide.Left);
        _pwm = Tapes.Pwm(options);
        _charge = new ChargeBarDrawable { Options = options };
        _value.SetTypeface(Typeface.Monospace);
    }

    protected override void DrawPanel(Canvas canvas, RectF content)
    {
        float left = SpeedTapeWidth * Density;
        float right = PwmTapeWidth * Density;

        Tapes.ApplySpeed(_speed, Reading, Options);
        Tapes.ApplyPwm(_pwm, Reading, Options);

        _speed.Draw(canvas, new RectF(content.Left, content.Top, content.Left + left, content.Bottom), Density);
        _pwm.Draw(canvas, new RectF(content.Right - right, content.Top, content.Right, content.Bottom), Density);

        var middle = new RectF(content.Left + left, content.Top, content.Right - right, content.Bottom);

        float value = ValueFontSize * Density;
        float caption = CaptionFontSize * Density;
        float gap = Spacing * Density;
        float charge = ChargeHeight * Density;
        float row = value * 1.15f + caption * 1.4f;

        // Столбик посередине стоит по центру высоты — как VerticalStackLayout с Center в исходнике.
        float block = charge + (row + gap) * 3;
        float top = middle.CenterY() - block / 2;

        _charge.Battery = Reading.Battery;
        _charge.VoltageV = Reading.VoltageV;
        _charge.Draw(canvas, new RectF(middle.Left, top, middle.Right, top + charge), Density);
        top += charge + gap;

        Titled(canvas, middle, top, value, caption, $"{Reading.MaxPwm:F0} %", "макс ШИМ");
        top += row + gap;
        Titled(canvas, middle, top, value, caption, $"{Reading.TripKm:F1}", "поездка, км");
        top += row + gap;
        Titled(canvas, middle, top, value, caption, $"{Reading.TemperatureC} °C", "температура");
    }

    private void Titled(Canvas canvas, RectF middle, float top, float value, float caption,
        string text, string label)
    {
        var palette = Options.Palette;

        _value.Color = palette.Ink;
        _value.TextSize = value;
        canvas.DrawString(_value, text, middle.Left, top, middle.Width(), value * 1.15f, HAlign.Center, VAlign.Center);

        _caption.Color = WithAlpha(palette.Ink, 0.7f);
        _caption.TextSize = caption;
        canvas.DrawString(_caption, label, middle.Left, top + value * 1.15f, middle.Width(), caption * 1.4f,
            HAlign.Center, VAlign.Center);
    }

    private static Color WithAlpha(Color color, float alpha) =>
        Color.Argb((int)Math.Round(alpha * 255), color.R, color.G, color.B);
}
