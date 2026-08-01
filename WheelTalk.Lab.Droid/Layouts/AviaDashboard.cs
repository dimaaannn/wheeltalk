using Android.Content;
using Android.Graphics;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Layouts;
using WheelTalk.Dashboard.Droid.Widgets;
using WheelTalk.Lab.Droid.Widgets;

namespace WheelTalk.Lab.Droid.Layouts;

/// <summary>
/// Вариант B — «Авиа». Ближе всех к исходному замыслу PFD, но с исправленным распределением
/// внимания: по краям то, у чего важна тенденция, в центре то, ради чего смотрят.
/// <list type="bullet">
/// <item>Слева — напряжение полосой, без делений и цифр: важно не значение, а просадка.</item>
/// <item>Справа — лента ШИМ с вектором тренда и биркой личного предела.</item>
/// <item>В центре — скорость цифрой, а вокруг неё кольцо: цифра даёт значение, кольцо — форму,
/// которая ловится периферией.</item>
/// </list>
/// <para>
/// Отличие от варианта A, где ленты по краям были обе: там движение шло с двух сторон и центр был
/// отдан второстепенному, здесь непрерывное движение только справа, а слева медленная величина.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Layouts/AviaDashboard.cs</c>: колонки MAUI-<c>Grid</c>
/// (28 dp · звезда · 108 dp, зазор 6, поля 6/6/0/6) стали арифметикой по ширине холста — те же
/// числа, только в пикселях.
/// </para>
/// </summary>
public sealed class AviaDashboard : DashboardView
{
    private const float StripWidth = 28;
    private const float TapeWidth = 108;
    private const float Spacing = 6;

    private readonly VoltageStripDrawable _voltage;
    private readonly SpeedRingDrawable _ring;
    private readonly TapeDrawable _pwm;
    private readonly SpeedDigitDrawable _speed;

    public AviaDashboard(Context context, DashboardOptions options) : base(context, options)
    {
        _voltage = new VoltageStripDrawable { Options = options };
        _ring = new SpeedRingDrawable { Options = options };
        _pwm = Tapes.Pwm(options);
        _speed = new SpeedDigitDrawable(72) { Options = options, GrownFontSize = 104 };
    }

    protected override void DrawPanel(Canvas canvas, RectF content)
    {
        float pad = Spacing * Density;
        float strip = StripWidth * Density;
        float tape = TapeWidth * Density;

        float left = content.Left + pad;
        float top = content.Top + pad;
        float bottom = content.Bottom - pad;

        _voltage.VoltageV = Reading.VoltageV;
        _voltage.Draw(canvas, new RectF(left, top, left + strip, bottom), Density);

        Tapes.ApplyPwm(_pwm, Reading, Options);
        _pwm.Draw(canvas, new RectF(content.Right - tape, content.Top, content.Right, content.Bottom), Density);

        var middle = new RectF(left + strip + pad, top, content.Right - tape - pad, bottom);

        _ring.SpeedKmh = Reading.SpeedKmh;
        _ring.Trend = Reading.SpeedKmh + Reading.SpeedRate * Options.TrendSeconds;
        _ring.Draw(canvas, middle, Density);

        _speed.Draw(canvas, middle, Reading, Density);
    }
}
