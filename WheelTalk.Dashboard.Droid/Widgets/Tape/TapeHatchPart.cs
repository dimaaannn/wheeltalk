using Android.Graphics;

namespace WheelTalk.Dashboard.Droid.Widgets.Tape;

/// <summary>
/// Косая штриховка выше предела — «barber pole» авиационных лент. Она разводит два состояния,
/// которые цифрой не различить: «сейчас будет» (до штриховки дотянулась стрелка тренда) и «уже»
/// (в неё вошёл сам указатель).
/// <para>
/// Рисуется короткими диагоналями по клипу, а не пунктирной линией под углом: пунктир на полосе в
/// шестнадцать точек читается как грязь, а не как штриховка.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/Tape/TapeHatchPart.cs</c>: логика и порог не
/// менялись, в <see cref="Draw"/> добавлено умножение на плотность экрана —
/// <see cref="Spacing"/> и толщина линии были буквальными dp у MAUI-канвы.
/// Уже перенесено 1:1 в <c>WheelTalk.Native/Drawing/TapeRenderer.Hatch</c> — этот файл сверен с
/// обоими источниками.
/// </para>
/// </summary>
public sealed class TapeHatchPart
{
    private const float Spacing = 10;

    private readonly Paint _fill = new() { AntiAlias = true };
    private readonly Paint _stroke = new() { AntiAlias = true, StrokeWidth = 1 };

    public TapeHatchPart() => _stroke.SetStyle(Paint.Style.Stroke);

    /// <summary>С какого значения начинается штриховка. null — не рисовать.</summary>
    public double? From { get; set; }

    /// <summary>Где кончается. null — до верха видимой части.</summary>
    public double? To { get; set; }

    public void Draw(Canvas canvas, in TapeGeometry geometry, DashboardPalette palette, float density)
    {
        if (From is not { } from) return;

        float bottom = geometry.ToY(from);
        float top = Math.Max(geometry.Rect.Top, geometry.ToY(To ?? geometry.TopValue));
        if (bottom <= top) return;

        float left = geometry.BandLeft;
        float width = geometry.BandWidth;

        canvas.Save();
        canvas.ClipRect(left, top, left + width, bottom);

        _fill.Color = Color.White;
        canvas.DrawRect(left, top, left + width, bottom, _fill);

        _stroke.Color = palette.Danger;
        _stroke.StrokeWidth = 5 * density;
        for (float y = top - width; y < bottom + width; y += Spacing * density)
        {
            canvas.DrawLine(left, y + width, left + width, y, _stroke);
        }

        canvas.Restore();
    }
}
