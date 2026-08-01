using Android.Content;
using Android.Graphics;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Layouts;
using WheelTalk.Dashboard.Droid.Widgets;
using WheelTalk.Lab.Droid.Widgets;

namespace WheelTalk.Lab.Droid.Layouts;

/// <summary>
/// Вариант C — одна лента ШИМ по правому краю и крупная цифра скорости в центре. В исследовании
/// его нет: он собран из двух половин, чтобы проверить главное возражение против варианта A.
/// У A два источника непрерывного движения и самый мелкий кегль из всех; здесь движение одно,
/// а центр экрана — самое ценное место — отдан не второстепенному, а тому, ради чего смотрят.
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Layouts/SingleTapeDashboard.cs</c>: строки центральной
/// колонки (скорость по центру, строка контекста, полоса заряда снизу) считаются по высоте
/// холста — те же кегли и те же отступы, что были в MAUI-<c>Grid</c>.
/// </para>
/// </summary>
public sealed class SingleTapeDashboard : DashboardView
{
    private const float TapeWidth = 108;
    private const float ChargeHeight = 44;
    private const float ContextFontSize = 24;
    private const float Padding = 8;
    private const float Spacing = 10;

    private readonly TapeDrawable _pwm;
    private readonly ChargeBarDrawable _charge;
    private readonly SpeedDigitDrawable _speed;
    private readonly Paint _context = new() { AntiAlias = true };

    public SingleTapeDashboard(Context context, DashboardOptions options) : base(context, options)
    {
        _pwm = Tapes.Pwm(options);
        _charge = new ChargeBarDrawable { Options = options };
        // Лента забирает 108 dp, центру остаётся 252 — «24,5» помещается кеглем 80, «24» кеглем 120.
        _speed = new SpeedDigitDrawable(80) { Options = options, GrownFontSize = 120 };
        _context.SetTypeface(Typeface.Monospace);
    }

    protected override void DrawPanel(Canvas canvas, RectF content)
    {
        float tape = TapeWidth * Density;
        float pad = Padding * Density;
        float gap = Spacing * Density;
        float charge = ChargeHeight * Density;
        float contextFont = ContextFontSize * Density;

        Tapes.ApplyPwm(_pwm, Reading, Options);
        _pwm.Draw(canvas, new RectF(content.Right - tape, content.Top, content.Right, content.Bottom), Density);

        float left = content.Left + pad;
        float right = content.Right - tape - pad;
        float bottom = content.Bottom - pad;

        _charge.Battery = Reading.Battery;
        _charge.VoltageV = Reading.VoltageV;
        _charge.Draw(canvas, new RectF(left, bottom - charge, right, bottom), Density);

        float contextTop = bottom - charge - gap - contextFont * 1.3f;

        // Стоя показывается то, ради чего на стоящее колесо и смотрят; на ходу — то, что меняется.
        _context.Color = Options.Palette.Ink;
        _context.TextSize = contextFont;
        canvas.DrawString(_context, Reading.Standing
                ? $"{Reading.Battery} % · {Reading.VoltageV:F1} В"
                : $"макс {Reading.MaxPwm:F0} % · {Reading.TripKm:F1} км",
            left, contextTop, right - left, contextFont * 1.3f, HAlign.Center, VAlign.Center);

        _speed.Draw(canvas, new RectF(left, content.Top, right, contextTop - gap), Reading, Density);
    }
}
