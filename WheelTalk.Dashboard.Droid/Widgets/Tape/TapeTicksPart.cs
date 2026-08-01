using Android.Graphics;
using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Dashboard.Droid.Widgets.Tape;

/// <summary>
/// Деления и подписи. Растут от полосы внутрь экрана: полоса стоит у края, цифры смотрят в центр,
/// и на левой ленте всё то же самое в зеркале.
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/Tape/TapeTicksPart.cs</c>: логика цикла и порог
/// подписи не менялись, добавлено умножение на плотность экрана. <see cref="LabelFormat"/> —
/// закрытый пробел бенча: в <c>WheelTalk.Native/Drawing/TapeRenderer.Ticks</c> формат подписи был
/// жёстко <c>"F0"</c> прямо в вызове <c>DrawText</c>; здесь он снова настраиваемое поле, как в
/// MAUI-исходнике, значение по умолчанию то же.
/// </para>
/// </summary>
public sealed class TapeTicksPart
{
    /// <summary>Длины делений и кегль подписи — доли ширины ленты, но не мельче читаемого.</summary>
    private const float MinorOfWidth = 0.09f;

    private const float MajorOfWidth = 0.18f;
    private const float FontOfWidth = 0.26f;

    private readonly Paint _stroke = new() { AntiAlias = true, StrokeWidth = 1 };
    private readonly Paint _text = new() { AntiAlias = true };

    public TapeTicksPart() => _stroke.SetStyle(Paint.Style.Stroke);

    public double Step { get; set; } = 5;
    public double LabelStep { get; set; } = 10;

    /// <summary>Концы шкалы: за ними разметки нет. null — лента бесконечна в эту сторону.</summary>
    public double? From { get; set; }
    public double? To { get; set; }

    /// <summary>
    /// Ниже этого значения деления есть, а подписей нет. Отрицательный ШИМ существует — это
    /// рекуперация, — но числом он на экране движения не нужен: шкала уходит вниз, показывая, что
    /// она не кончилась, и на этом её работа там заканчивается.
    /// </summary>
    public double? LabelFrom { get; set; }

    public string LabelFormat { get; set; } = "F0";

    /// <summary>Потолок кегля подписи; на узкой ленте он опускается сам.</summary>
    public float MaxFontSize { get; set; } = 26;

    /// <summary>
    /// Полосы затухания у кромок, пиксели: у самого края деления и подписи прозрачны полностью и
    /// проявляются по мере удаления от него. Разметка обрезанная кромкой читается как обломок, а не
    /// как продолжение шкалы, — особенно сверху, где под панель уходит статус-бар и половинка цифры
    /// оказывается рядом с часами.
    /// <para>
    /// Затухают только деления и подписи. Заливка ленты идёт до кромки как шла: она и есть та
    /// величина, ради которой ленту смотрят, и обрывать её незачем.
    /// </para>
    /// <para>Ноль — не затухать. Так у стендов, где панель не под системными барами.</para>
    /// </summary>
    public float FadeTop { get; set; }

    public float FadeBottom { get; set; }

    public void Draw(Canvas canvas, in TapeGeometry geometry, DashboardPalette palette, float density)
    {
        if (Step <= 0) return;

        double top = To is { } limit ? Math.Min(geometry.TopValue, limit) : geometry.TopValue;
        double bottom = From is { } floor ? Math.Max(geometry.BottomValue, floor) : geometry.BottomValue;

        float rectWidth = geometry.Rect.Width();
        float minor = rectWidth * MinorOfWidth;
        float major = rectWidth * MajorOfWidth;
        float font = Math.Min(MaxFontSize * density, rectWidth * FontOfWidth);

        _stroke.Color = palette.Dim;
        _stroke.StrokeWidth = 2 * density;
        _text.Color = palette.Ink;
        _text.TextSize = font;

        int inward = geometry.Inward;
        float tickBase = geometry.TickBase;

        int strokeAlpha = _stroke.Color.A;
        int textAlpha = _text.Color.A;

        for (double value = Math.Ceiling(bottom / Step) * Step; value <= top; value += Step)
        {
            float y = geometry.ToY(value);
            bool labelled = Math.Abs(Math.IEEERemainder(value, LabelStep)) < Step / 4;

            float visible = Visibility(y, geometry.Rect);
            if (visible <= 0) continue;

            _stroke.Alpha = (int)Math.Round(strokeAlpha * visible);
            canvas.DrawLine(tickBase, y, tickBase + (labelled ? major : minor) * inward, y, _stroke);
            if (!labelled || value < LabelFrom) continue;

            _text.Alpha = (int)Math.Round(textAlpha * visible);

            // Подпись занимает всё место от делений до внутреннего края ленты и жмётся к делениям:
            // так соседние подписи не разъезжаются по горизонтали при смене числа знаков.
            float edge = tickBase + (major + 4 * density) * inward;
            float left = inward > 0 ? edge : geometry.Rect.Left;
            float width = inward > 0 ? geometry.Rect.Right - edge : edge - geometry.Rect.Left;
            if (width <= 0) continue;

            canvas.DrawString(_text, value.ToString(LabelFormat), left, y - font * 0.7f, width, font * 1.4f,
                inward > 0 ? HAlign.Left : HAlign.Right, VAlign.Center);
        }
    }

    /// <summary>
    /// Насколько видима разметка на этой высоте: 0 у самой кромки, 1 за полосой затухания. Берётся
    /// меньшее из двух — иначе на короткой ленте, где полосы сверху и снизу перекрываются, середина
    /// оказалась бы ярче краёв с обеих сторон сразу.
    /// </summary>
    private float Visibility(float y, RectF rect)
    {
        float above = FadeTop > 0 ? (y - rect.Top) / FadeTop : 1;
        float below = FadeBottom > 0 ? (rect.Bottom - y) / FadeBottom : 1;
        return Math.Clamp(Math.Min(above, below), 0, 1);
    }
}
