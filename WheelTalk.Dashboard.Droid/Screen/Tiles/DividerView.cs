using Android.Content;
using Android.Graphics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Разделитель: полоса во всю ширину, отбивающая одну кучку плиток от другой (решение владельца
/// 11.08.2026). Ни подложки, ни рамки, ни содержимого — вся его работа в том, чтобы между соседями
/// был <b>видимый зазор</b>.
/// <para>
/// <b>Зазор делает высота элемента, а линия его только называет.</b> Первым пробовали свойство
/// плитки — черту по её верхнему краю, — и владелец отверг: волосяная линия ничего не отделяет.
/// Поэтому здесь строка сетки своей, пониженной высоты (<see cref="TilesLayout.DividerRowDp"/>), а
/// линия по её середине — тонкая нарочно: разделяет пустота, а не чернила.
/// </para>
/// <para>
/// В режиме правки берётся пальцем и таскается как плитка: для сетки это такой же элемент списка, и
/// пунктирный контур пустого места ему достаётся от <see cref="TileView.BindEmpty"/> — иначе
/// невидимую полосу нечем было бы поймать.
/// </para>
/// </summary>
internal sealed class DividerView(Context context, DashboardOptions options) : TileView(context, options)
{
    private readonly Paint _line = new() { AntiAlias = true };

    /// <summary>Своего содержимого у разделителя нет — прятать нечего.</summary>
    protected override void ShowContent(bool visible)
    {
    }

    public void Bind(TileSize size)
    {
        BindEmpty(size);
        Invalidate();
    }

    protected override void DispatchDraw(Canvas canvas)
    {
        base.DispatchDraw(canvas);

        var ink = Palette.Dim;
        _line.Color = Color.Argb(TilesLayout.DividerAlpha, ink.R, ink.G, ink.B);
        _line.StrokeWidth = Context!.Dp(TilesLayout.DividerLineDp);

        float middle = Height / 2f;
        canvas.DrawLine(0, middle, Width, middle, _line);
    }
}
