using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Core.Contracts;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Рамка плитки: подложка, подпись сверху, метки режима правки и высота. Общее у всех видов, чем бы
/// они ни рисовали, — величина остаётся параметром рисовальщика, а не поводом для нового класса
/// (план 23 §7).
/// <para>
/// Пустое место (<see cref="TileKind.Empty"/>) рисуется этой же рамкой без содержимого: заводить
/// под «ничего» отдельный вид значило бы держать класс ради выключенных полей.
/// </para>
/// <para>
/// <b>HOTRELOAD:</b> своих чисел у рамки нет — все берутся из <see cref="TilesLayout"/> и прочитаны
/// в конструкторе. Оттого правка вида и требует пересборки экрана, а не только применения
/// перезагрузки.
/// </para>
/// </summary>
internal abstract class TileView : LinearLayout
{
    /// <summary>Уголок-метка режима правки: рисуется по размеру плитки, поэтому строится в <see cref="OnSizeChanged"/>.</summary>
    private readonly Paint _handlePaint = new() { AntiAlias = true };
    private readonly Android.Graphics.Path _handle = new();

    /// <summary>Контур пустого места — виден только в правке: иначе пустоту нечем поймать пальцем.</summary>
    private readonly Paint _outlinePaint = new() { AntiAlias = true };
    private readonly RectF _outline = new();
    private readonly GradientDrawable _filled;
    private readonly float _radius;

    /// <summary>Какого цвета рамка тревоги сейчас — чтобы не перекрашивать её тем же самым.</summary>
    private Color _fill;

    private bool _editing;
    private bool _empty;

    protected TileView(Context context, DashboardOptions options) : base(context)
    {
        Options = options;
        Orientation = Android.Widget.Orientation.Vertical;

        var palette = options.Palette;

        int pad = context.Dp(TilesLayout.PaddingDp);
        SetPadding(pad, pad, pad, pad);

        _radius = context.Dp(TilesLayout.CornerRadiusDp);

        _filled = new GradientDrawable();
        _filled.SetShape(ShapeType.Rectangle);
        _filled.SetCornerRadius(_radius);
        // Фон плитки — приглушённая краска палитры, взятая почти прозрачной: так плитки видны при
        // любой палитре, и второго набора цветов заводить не пришлось. Он не меняется никогда;
        // тревога показывается рамкой поверх него (см. ShowHeat).
        _filled.SetColor(Color.Argb(TilesLayout.BackgroundAlpha, palette.Dim.R, palette.Dim.G, palette.Dim.B));
        ShowHeat(0);
        Background = _filled;

        _handlePaint.Color = Color.Argb(TilesLayout.HandleAlpha, palette.Ink.R, palette.Ink.G, palette.Ink.B);

        _outlinePaint.SetStyle(Paint.Style.Stroke);
        _outlinePaint.StrokeWidth = context.Dp(TilesLayout.OutlineDp);
        _outlinePaint.Color = Color.Argb(TilesLayout.HandleAlpha, palette.Dim.R, palette.Dim.G, palette.Dim.B);

        Label = new TextView(context);
        Label.SetTextSize(ComplexUnitType.Sp, TilesLayout.LabelSp);
        Label.SetTextColor(palette.Dim);
        Label.SetMaxLines(1);
        Label.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        AddView(Label);
    }

    /// <summary>
    /// Настройки панели целиком, а не одна её палитра: подложка красится по порогам тревоги, а
    /// пороги живут здесь же (<see cref="DashboardOptions.Thresholds"/>).
    /// </summary>
    protected DashboardOptions Options { get; }

    protected DashboardPalette Palette => Options.Palette;

    /// <summary>Подпись величины сверху — одна на все виды плиток.</summary>
    protected TextView Label { get; }

    /// <summary>
    /// Показать, насколько величина подошла к тревоге (<see cref="MetricHeat"/>), — <b>рамкой, а не
    /// заливкой всей плитки</b> (решение владельца 05.08.2026). Залитая подложка ложилась на график
    /// и спорила с его линией и заливкой; рамка идёт по краю внутрь и не закрывает содержимого. На
    /// текст она наезжать может — так лучше, чем красить его самого.
    /// <para>
    /// Красится по-прежнему не число: показание должно читаться одинаково при любом значении.
    /// </para>
    /// <para>
    /// Тем же цветом второй раз не красим: <c>SetStroke</c> тянет за собой перерисовку, а зовут это
    /// с каждым новым показанием.
    /// </para>
    /// </summary>
    protected void ShowHeat(double heat)
    {
        var tint = MetricHeat.Tint(heat, Palette);
        var stroke = heat <= 0
            ? Color.Transparent
            : Color.Argb(MetricHeat.Alpha(heat), tint.R, tint.G, tint.B);

        if (_fill == stroke) return;

        _fill = stroke;
        _filled.SetStroke(Context!.Dp(TilesLayout.HeatStrokeDp), stroke);
    }

    /// <summary>
    /// Режим правки: плитка помечается уголком, пустое место — контуром. Метка, а не ручка: размер и
    /// вид задают в меню плитки, а уголок отвечает на единственный вопрос — правится сейчас экран
    /// или показывается.
    /// </summary>
    public bool Editing
    {
        set
        {
            if (_editing == value) return;

            _editing = value;
            Invalidate();
        }
    }

    /// <summary>Очередной снимок. Зовётся на каждом кадре, поэтому дешёвый: вид без чисел молчит.</summary>
    public virtual void Render(TelemetrySnapshot? snapshot)
    {
    }

    /// <summary>
    /// Пустое место: ни подписи, ни содержимого, ни подложки — только клетки, которые оно занимает.
    /// В режиме правки за него берутся пальцем, поэтому там оно обведено контуром.
    /// </summary>
    public void BindEmpty(TileSize size)
    {
        _empty = true;
        Label.Text = "";
        Background = null;
        ShowContent(false);
        SetRows(size.Rows);
    }

    /// <summary>
    /// Общее начало всякой непустой привязки: подложка на месте, содержимое показано.
    /// <para>
    /// Подпись может быть выключена (<paramref name="showLabel"/>): на мелкой плитке её строка
    /// забирает место у числа, а величина часто узнаётся по нему самому — по разрядам и единице.
    /// </para>
    /// </summary>
    protected void BindFrame(string label, TileSize size, bool showLabel)
    {
        _empty = false;
        Label.Text = label;
        Label.Visibility = showLabel ? ViewStates.Visible : ViewStates.Gone;
        Background = _filled;
        ShowContent(true);
        SetRows(size.Rows);
    }

    /// <summary>Показать или спрятать то, что вид добавил под подписью.</summary>
    protected abstract void ShowContent(bool visible);

    protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
    {
        base.OnSizeChanged(width, height, oldWidth, oldHeight);

        int side = Context!.Dp(TilesLayout.HandleSizeDp);

        _handle.Reset();
        _handle.MoveTo(width, height - side);
        _handle.LineTo(width, height);
        _handle.LineTo(width - side, height);
        _handle.Close();

        // Контур рисуется по середине линии, поэтому отступает от края на её половину — иначе
        // внешняя половина обрезалась бы краем плитки.
        float inset = _outlinePaint.StrokeWidth / 2;
        _outline.Set(inset, inset, width - inset, height - inset);
    }

    /// <summary>
    /// Метки поверх содержимого, а не под ним: <c>OnDraw</c> у группы зовётся до детей, и число
    /// легло бы сверху. Вне режима правки не рисуется ничего — обе метки нужны тому, кто правит:
    /// уголок говорит «идёт правка», контур показывает, где стоит пустое место.
    /// </summary>
    protected override void DispatchDraw(Canvas canvas)
    {
        base.DispatchDraw(canvas);

        if (!_editing) return;

        if (_empty) canvas.DrawRoundRect(_outline, _radius, _radius, _outlinePaint);
        else canvas.DrawPath(_handle, _handlePaint);
    }

    /// <summary>
    /// Точная высота вместо высоты по содержимому: строка сетки — одна мера на всех
    /// (<see cref="TilesLayout.RowHeightDp"/>), и высокая плитка встаёт ровно на место столбика
    /// низких вместе с просветами между ними.
    /// <para>
    /// Нужна <c>GridLayoutManager</c>, который знает только ширину: высоту при нём ставит сама
    /// плитка. Свой укладчик меряет её прямоугольником и эту высоту не читает — лишней она не
    /// становится, пока укладчика два.
    /// </para>
    /// </summary>
    private void SetRows(int rows)
    {
        int height = Context!.Dp(TilesLayout.RowHeightDp * rows + TilesLayout.GapDp * 2 * (rows - 1));

        if (LayoutParameters is not { } layout || layout.Height == height) return;

        layout.Height = height;
        LayoutParameters = layout;
    }
}
