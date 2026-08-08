using Android.Graphics;

namespace WheelTalk.Dashboard.Droid.Widgets;

/// <summary>
/// Галочка у нижнего края — подсказка про шторку быстрых команд и второй вход в неё (тап вместо
/// свайпа).
/// <para>
/// Своим типом, а не куском панели: подсказка принадлежит **шторке**, а шторка — рамке обоих
/// экранов, и экрану плиток нужна ровно та же (план 25 §0.2). Вторая копия геометрии разошлась бы
/// с первой при первой же правке отступа, и на одном из экранов галочка перестала бы нажиматься —
/// молча, потому что рисоваться она продолжала бы там же.
/// </para>
/// </summary>
public sealed class SheetHintDrawable
{
    /// <summary>Ширина, высота и отступ галочки от нижнего края — в точках экрана.</summary>
    private const float Width = 26f;

    private const float Height = 7f;
    private const float Bottom = 12f;

    /// <summary>Непрозрачность: подсказку видно, но за сигнал её не примешь.</summary>
    private const float Alpha = 0.35f;

    /// <summary>
    /// Цель касания, точки экрана: чуть больше самого знака (26×7), но много меньше зоны свайпа
    /// шторки (нижние 128 dp, <c>SwipeUpFromEdgeListener</c>) — прицельная точка, а не вторая
    /// полоса поверх первой (владелец, план 23).
    /// </summary>
    private const float TouchWidth = 48f;

    private const float TouchHeight = 32f;

    private readonly Paint _stroke = new() { AntiAlias = true };

    public SheetHintDrawable() => _stroke.SetStyle(Paint.Style.Stroke);

    /// <summary>Рисовать ли подсказку. Пока шторка открыта, подсказывать нечего.</summary>
    public bool Visible { get; set; }

    /// <summary>
    /// Попало ли касание в галочку. Проверка живёт рядом с рисованием, потому что координаты знака
    /// здешние: копия снаружи разошлась бы с ними при первой же правке отступа.
    /// </summary>
    public bool Hits(RectF rect, float density, float x, float y)
    {
        if (!Visible) return false;

        float centreX = rect.CenterX();
        float centreY = rect.Bottom - (Bottom + Height / 2) * density;
        float halfWidth = TouchWidth / 2 * density;
        float halfHeight = TouchHeight / 2 * density;
        return x >= centreX - halfWidth && x <= centreX + halfWidth
            && y >= centreY - halfHeight && y <= centreY + halfHeight;
    }

    /// <param name="ink">Цвет письма палитры: сам знак цвета не выбирает — он часть экрана, на
    /// котором стоит, а экраны у нас двух видов и палитра у них одна.</param>
    public void Draw(Canvas canvas, RectF rect, float density, Color ink)
    {
        if (!Visible) return;

        // Галочка смотрит вверх — туда, откуда придёт шторка.
        float width = Width * density;
        float height = Height * density;
        float centre = rect.CenterX();
        float bottom = rect.Bottom - Bottom * density;

        _stroke.Color = Color.Argb((int)Math.Round(Alpha * 255), ink.R, ink.G, ink.B);
        _stroke.StrokeWidth = 2 * density;
        _stroke.StrokeCap = Paint.Cap.Round;

        var path = new Android.Graphics.Path();
        path.MoveTo(centre - width / 2, bottom);
        path.LineTo(centre, bottom - height);
        path.LineTo(centre + width / 2, bottom);
        canvas.DrawPath(path, _stroke);
    }
}
