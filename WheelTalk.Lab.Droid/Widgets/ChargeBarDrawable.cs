using Android.Graphics;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Lab.Droid.Widgets;

/// <summary>
/// Заряд полосой: сколько ещё ехать — величина третьей важности, и цифра ей нужна не всегда.
/// Горизонтальная полоса подписана процентом и напряжением, вертикальная — ничем: она стоит
/// вдоль края экрана и работает как термометр.
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/ChargeBarDrawable.cs</c>: абсолютные величины
/// домножены на плотность экрана, логика и пороги без изменений.
/// </para>
/// </summary>
public sealed class ChargeBarDrawable
{
    private readonly Paint _fill = new() { AntiAlias = true };
    private readonly Paint _text = new() { AntiAlias = true };

    public required DashboardOptions Options { get; init; }

    public int Battery { get; set; }
    public double VoltageV { get; set; }
    public bool Horizontal { get; set; } = true;

    public void Draw(Canvas canvas, RectF rect, float density)
    {
        var palette = Options.Palette;
        float fraction = Math.Clamp(Battery / 100f, 0, 1);

        _fill.Color = Color.Argb(26, 255, 255, 255);
        canvas.DrawRect(rect, _fill);

        // Красным заряд становится только под конец: до тех пор это фон, а не сигнал, и спорить
        // за внимание с ШИМ он не должен.
        _fill.Color = Battery <= 15 ? palette.Danger : palette.Calm;

        if (Horizontal)
        {
            canvas.DrawRect(rect.Left, rect.Top, rect.Left + rect.Width() * fraction, rect.Bottom, _fill);

            // Обе подписи рисуются в одну коробку во всю ширину, только с разным выравниванием:
            // делить её пополам оказалось нельзя — трёхзначный заряд и трёхзначное напряжение в
            // половину не помещались и теряли по знаку с краю.
            _text.Color = palette.Ink;
            _text.TextSize = 20 * density;
            float left = rect.Left + 10 * density;
            float width = rect.Width() - 20 * density;
            canvas.DrawString(_text, $"{Battery} %", left, rect.Top, width, rect.Height(), HAlign.Left, VAlign.Center);
            canvas.DrawString(_text, $"{VoltageV:F1} В", left, rect.Top, width, rect.Height(), HAlign.Right, VAlign.Center);
            return;
        }

        float height = rect.Height() * fraction;
        canvas.DrawRect(rect.Left, rect.Bottom - height, rect.Right, rect.Bottom, _fill);
    }
}
