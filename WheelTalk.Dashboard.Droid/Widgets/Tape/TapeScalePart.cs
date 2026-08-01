using Android.Graphics;

namespace WheelTalk.Dashboard.Droid.Widgets.Tape;

/// <summary>Цветной диапазон на самой шкале: ответ «много ли это» даёт положение внутри полосы.</summary>
public sealed record TapeBand(double From, double To, Color Color);

/// <summary>
/// Полоса шкалы, размеченная цветом. Главный приём, взятый у авиационного PFD: не «загорается
/// лампочка при превышении», а сама шкала размечена, и положение указателя внутри цветного
/// диапазона и есть ответ. Цифра «84» не говорит, много это или мало; полоса говорит.
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/Tape/TapeScalePart.cs</c> без изменений в
/// логике — заливки не используют абсолютных dp-констант, поэтому плотность экрана здесь не
/// нужна вовсе.
/// </para>
/// </summary>
public sealed class TapeScalePart
{
    private readonly Paint _fill = new() { AntiAlias = true };

    public IReadOnlyList<TapeBand> Bands { get; set; } = [];

    public void Draw(Canvas canvas, in TapeGeometry geometry)
    {
        foreach (var band in Bands)
        {
            float top = geometry.ToY(band.To);
            float bottom = geometry.ToY(band.From);
            if (bottom < geometry.Rect.Top || top > geometry.Rect.Bottom) continue;

            _fill.Color = band.Color;
            canvas.DrawRect(geometry.BandLeft, top, geometry.BandLeft + geometry.BandWidth, bottom, _fill);
        }
    }
}
