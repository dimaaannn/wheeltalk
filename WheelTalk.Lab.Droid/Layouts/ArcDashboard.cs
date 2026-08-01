using Android.Content;
using Android.Graphics;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Widgets;
using WheelTalk.Lab.Droid.Widgets;

namespace WheelTalk.Lab.Droid.Layouts;

/// <summary>
/// Вариант E — одна дуга ШИМ на весь верх экрана и крупная скорость внутри неё. Иерархия
/// перевёрнута против WheelLog: фигура показывает ШИМ, потому что у него важно изменение, цифра
/// показывает скорость, потому что у неё важно значение.
/// <para>
/// Здесь же живёт идея «ШИМ вытесняет скорость», и живёт она не натяжкой: дуга и цифра делят один
/// круг, поэтому рост одного — прямое уменьшение другого, и происходит это там, куда и так смотрят.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Layouts/ArcDashboard.cs</c> без изменений в числах:
/// те же толщины дуги, те же кегли и тот же расчёт вытеснения.
/// </para>
/// </summary>
public sealed class ArcDashboard : DashboardView
{
    private const float ThinArc = 26;
    private const float ThickArc = 60;
    private const double LargeSpeed = 96;
    private const double CrowdedSpeed = 64;
    private const float ChargeHeight = 44;
    private const float TilesHeight = 58;
    private const float PwmFontSize = 34;
    private const float TileValueFontSize = 30;
    private const float TileCaptionFontSize = 14;
    private const float Padding = 8;
    private const float Spacing = 8;

    // Единица измерения живёт в подписи, а не в значении: на плитку шириной 90 dp «33 °C» тридцатым
    // кеглем не влезает, и соседние плитки начинают наезжать друг на друга.
    private static readonly (string Caption, Func<DashboardReading, string> Read)[] TileSet =
    [
        ("°C", r => $"{r.TemperatureC}"),
        ("км", r => $"{r.TripKm:F1}"),
        ("макс ШИМ", r => $"{r.MaxPwm:F0}"),
        ("макс км/ч", r => $"{r.TopSpeedKmh:F0}"),
    ];

    private readonly ArcDrawable _arc;
    private readonly ChargeBarDrawable _charge;
    private readonly SpeedDigitDrawable _speed;
    private readonly Paint _pwm = new() { AntiAlias = true };
    private readonly Paint _tileValue = new() { AntiAlias = true };
    private readonly Paint _tileCaption = new() { AntiAlias = true };

    public ArcDashboard(Context context, DashboardOptions options) : base(context, options)
    {
        _arc = new ArcDrawable { Options = options };
        _charge = new ChargeBarDrawable { Options = options };
        _speed = new SpeedDigitDrawable(LargeSpeed) { Options = options, GrownFontSize = 112 };
        _pwm.SetTypeface(Typeface.Monospace);
        _tileValue.SetTypeface(Typeface.Monospace);
    }

    protected override void DrawPanel(Canvas canvas, RectF content)
    {
        var palette = Options.Palette;
        double crowding = Crowding();

        float pad = Padding * Density;
        float gap = Spacing * Density;
        float charge = ChargeHeight * Density;
        float tiles = TilesHeight * Density;

        float left = content.Left + pad;
        float right = content.Right - pad;
        float tilesTop = content.Bottom - tiles;
        float chargeTop = tilesTop - gap - charge;
        var top = new RectF(left, content.Top, right, chargeTop - gap);

        _arc.Value = Reading.Pwm;
        _arc.Peak = Reading.RecentPwmPeak > Reading.Pwm ? Reading.RecentPwmPeak : null;
        _arc.Bug = Options.PersonalLimit;
        _arc.Thickness = (float)(ThinArc + (ThickArc - ThinArc) * crowding);
        _arc.Draw(canvas, top, Density);

        // Скорость и подпись ШИМ стоят парой внутри дуги, как VerticalStackLayout в MAUI-исходнике:
        // цифра чуть выше центра кольца, подпись сразу под ней. Центр кольца — не центр отведённой
        // области: дуга прижата к верху, поэтому пара считается от него, а не от середины строки.
        float pwmFont = PwmFontSize * Density;
        float ring = Math.Min(top.Width(), top.Height());
        var inside = new RectF(top.Left, top.Top, top.Right, top.Top + ring);
        float split = inside.CenterY() + pwmFont * 0.4f;

        _speed.ForcedFontSize = LargeSpeed - (LargeSpeed - CrowdedSpeed) * crowding;
        _speed.Draw(canvas, new RectF(inside.Left, inside.Top, inside.Right, split), Reading, Density);

        _pwm.Color = palette.ForPwm(Reading.Pwm, Options);
        _pwm.TextSize = pwmFont;
        canvas.DrawString(_pwm, $"ШИМ {Reading.Pwm:F0}", inside.Left, split,
            inside.Width(), pwmFont * 1.3f, HAlign.Center, VAlign.Center);

        _charge.Battery = Reading.Battery;
        _charge.VoltageV = Reading.VoltageV;
        _charge.Draw(canvas, new RectF(left, chargeTop, right, chargeTop + charge), Density);

        float value = TileValueFontSize * Density;
        float caption = TileCaptionFontSize * Density;
        float column = (right - left) / TileSet.Length;

        for (int i = 0; i < TileSet.Length; i++)
        {
            float cell = left + column * i;

            _tileValue.Color = palette.Ink;
            _tileValue.TextSize = value;
            canvas.DrawString(_tileValue, TileSet[i].Read(Reading), cell, tilesTop, column, value * 1.15f,
                HAlign.Center, VAlign.Center);

            _tileCaption.Color = WithAlpha(palette.Ink, 0.7f);
            _tileCaption.TextSize = caption;
            canvas.DrawString(_tileCaption, TileSet[i].Caption, cell, tilesTop + value * 1.15f, column, caption * 1.4f,
                HAlign.Center, VAlign.Center);
        }
    }

    /// <summary>
    /// 0 до критического порога, 1 у конца шкалы. Предел роста здесь не украшение: кегль 64 — это
    /// всё ещё 35 угловых минут с руля, то есть выше рекомендуемого ISO 15008, а дальше цифра
    /// перестала бы читаться ровно тогда, когда она нужна.
    /// </summary>
    private double Crowding()
    {
        if (!Options.PwmCrowdsOutSpeed) return 0;

        double span = Options.ScaleMax - Options.Thresholds.DangerPwm;
        return span <= 0 ? 0 : Math.Clamp((Reading.Pwm - Options.Thresholds.DangerPwm) / span, 0, 1);
    }

    private static Color WithAlpha(Color color, float alpha) =>
        Color.Argb((int)Math.Round(alpha * 255), color.R, color.G, color.B);
}
