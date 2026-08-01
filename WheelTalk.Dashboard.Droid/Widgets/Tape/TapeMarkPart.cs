using Android.Graphics;
using GraphicsPath = Android.Graphics.Path;

namespace WheelTalk.Dashboard.Droid.Widgets.Tape;

/// <summary>
/// След, оставленный поездкой: максимум ШИМ на одной шкале, самая глубокая просадка на другой.
/// Рисуется как «&gt;−»: тонкая риска поперёк ленты плюс треугольник со стороны подписей, остриём к
/// шкале. Одна риска теряется среди делений, а треугольник заметен — и стоит он там, где его не
/// закрывает окно текущего значения.
/// <para>
/// На левой ленте всё зеркально, «−&lt;»: треугольник у внутреннего края, остриём наружу.
/// Форма у следа своя, потому что рядом живёт стрелка тренда: два разных смысла должны различаться
/// не только цветом — на ходу и в полуметре от глаз два цветных пятна сливаются в одно.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/Tape/TapeMarkPart.cs</c>: логика и пороги
/// (<see cref="Arrow"/>, <see cref="NearWindow"/>, толщина линии 3) те же, домножены на плотность
/// экрана. Уже перенесено 1:1 в <c>WheelTalk.Native/Drawing/TapeRenderer.Trace</c> (вызывается там
/// дважды — для следа и для пика) — этот файл сверен с обоими источниками.
/// </para>
/// </summary>
public sealed class TapeMarkPart
{
    private const float Arrow = 14;

    /// <summary>
    /// Насколько близко к окну след перестаёт рисоваться. След — величина ретроспективная («до
    /// скольких догонял»), и пока значение стоит на своём максимуме, показывать нечего: на разгоне
    /// они совпадают, и метка всё равно оказалась бы под окном.
    /// </summary>
    private const float NearWindow = 34;

    private readonly Paint _stroke = new() { AntiAlias = true, StrokeWidth = 1 };
    private readonly Paint _fill = new() { AntiAlias = true };
    private readonly GraphicsPath _path = new();

    public TapeMarkPart() => _stroke.SetStyle(Paint.Style.Stroke);

    public double? Value { get; set; }

    public Color Color { get; set; } = global::Android.Graphics.Color.Yellow;

    public void Draw(Canvas canvas, in TapeGeometry geometry, float density)
    {
        if (Value is not { } mark) return;

        var rect = geometry.Rect;
        float y = geometry.ToY(mark);
        if (y < rect.Top || y > rect.Bottom) return;
        if (Math.Abs(y - geometry.WindowY) < NearWindow * density) return;

        _stroke.Color = Color;
        _stroke.StrokeWidth = 3 * density;
        canvas.DrawLine(rect.Left, y, rect.Right, y, _stroke);

        // Треугольник живёт со стороны подписей — там, где не проходит ни окно значения, ни
        // цветная полоса, — и смотрит остриём на шкалу.
        float baseX = geometry.Side == TapeSide.Right ? rect.Left : rect.Right;
        float tipX = baseX - Arrow * density * geometry.Inward;

        _path.Reset();
        _path.MoveTo(tipX, y);
        _path.LineTo(baseX, y - Arrow * density / 2);
        _path.LineTo(baseX, y + Arrow * density / 2);
        _path.Close();

        _fill.Color = Color;
        canvas.DrawPath(_path, _fill);
    }
}
