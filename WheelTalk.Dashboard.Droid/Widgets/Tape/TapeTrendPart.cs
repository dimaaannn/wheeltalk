using Android.Graphics;
using GraphicsPath = Android.Graphics.Path;

namespace WheelTalk.Dashboard.Droid.Widgets.Tape;

/// <summary>
/// Стрелка вдоль полосы. На ленте ШИМ это вектор тренда: от текущего значения до того, каким оно
/// станет через несколько секунд при нынешней производной, — райдеру нужен не ШИМ, а время до
/// предела. На ленте напряжения та же стрелка показывает просадку: она идёт от опорного холостого
/// напряжения к текущему, и её длина и есть глубина просадки здесь и сейчас.
/// <para>
/// Один класс на оба случая нарочно: приём один и тот же — «отсюда сюда», и разводить его на две
/// реализации значило бы получить две по-разному выглядящие стрелки.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/Tape/TapeTrendPart.cs</c>: логика и все пороги
/// (4, 8, 6) те же, домножены на плотность экрана. Уже перенесено 1:1 в
/// <c>WheelTalk.Native/Drawing/TapeRenderer.Arrow</c> — этот файл сверен с обоими источниками.
/// </para>
/// </summary>
public sealed class TapeTrendPart
{
    private const float ArrowHead = 8;

    private readonly Paint _stroke = new() { AntiAlias = true, StrokeWidth = 1 };
    private readonly Paint _fill = new() { AntiAlias = true };
    private readonly GraphicsPath _path = new();

    public TapeTrendPart() => _stroke.SetStyle(Paint.Style.Stroke);

    /// <summary>Откуда идёт стрелка. null — от текущего значения в окне.</summary>
    public double? From { get; set; }

    /// <summary>Куда идёт. null — не рисовать.</summary>
    public double? To { get; set; }

    public Color Color { get; set; } = global::Android.Graphics.Color.Yellow;

    public void Draw(Canvas canvas, in TapeGeometry geometry, float density)
    {
        if (To is not { } to) return;

        float startY = geometry.ToY(From ?? geometry.Value);
        float endY = geometry.ToY(to);
        if (Math.Abs(endY - startY) < 4 * density) return;

        float x = geometry.BandCenter;
        _stroke.Color = Color;
        _stroke.StrokeWidth = 4 * density;
        canvas.DrawLine(x, startY, x, endY, _stroke);

        float head = endY < startY ? ArrowHead * density : -ArrowHead * density;
        _path.Reset();
        _path.MoveTo(x, endY);
        _path.LineTo(x - 6 * density, endY + head);
        _path.LineTo(x + 6 * density, endY + head);
        _path.Close();

        _fill.Color = Color;
        canvas.DrawPath(_path, _fill);
    }
}
