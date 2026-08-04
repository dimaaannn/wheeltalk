using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Metrics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Плитка вида <see cref="TileKind.Value"/>: подпись, текущее число и единица. Величина приходит
/// описанием (<see cref="MetricDescriptor"/>), поэтому плитка одна на все двадцать шесть величин —
/// новая не добавляет сюда ни строки.
/// <para>
/// <b>Молчащая величина рисует прочерк, а не ноль</b> (план 23 §3.1): <c>null</c> из
/// <see cref="MetricDescriptor.Read"/> значит «этого колесо не сообщает», и ноль на его месте был бы
/// показанием, которого не было.
/// </para>
/// </summary>
internal sealed class MetricTileView : LinearLayout
{
    private const string NoValue = "—";

    private readonly TextView _label;
    private readonly TextView _value;
    private readonly TextView _unit;

    /// <summary>Уголок-ручка режима правки: рисуется по размеру плитки, поэтому строится в <see cref="OnSizeChanged"/>.</summary>
    private readonly Paint _handlePaint = new() { AntiAlias = true };
    private readonly Android.Graphics.Path _handle = new();

    /// <summary>Контур пустого места — виден только в правке: иначе пустоту нечем поймать пальцем.</summary>
    private readonly Paint _outlinePaint = new() { AntiAlias = true };
    private readonly RectF _outline = new();
    private readonly Drawable _filled;
    private readonly float _radius;

    private MetricDescriptor? _metric;
    private string _format = "F0";
    private string _shown = "";
    private bool _editing;
    private bool _empty;

    /// <summary>
    /// HOTRELOAD: своих чисел у плитки нет — все до одного берутся из <see cref="TilesLayout"/>.
    /// Подгонять вид глазами открывают тот файл, а прочитаны они здесь, в конструкторе: оттого
    /// правка и требует пересборки экрана, а не только применения перезагрузки.
    /// </summary>
    public MetricTileView(Context context, DashboardPalette palette) : base(context)
    {
        Orientation = Android.Widget.Orientation.Vertical;
        int pad = context.Dp(TilesLayout.PaddingDp);
        SetPadding(pad, pad, pad, pad);

        _radius = context.Dp(TilesLayout.CornerRadiusDp);

        var background = new GradientDrawable();
        background.SetShape(ShapeType.Rectangle);
        background.SetCornerRadius(_radius);
        // Фон плитки — та же приглушённая краска палитры, взятая почти прозрачной: так плитки видны
        // на фоне панели при любой палитре, и второго набора цветов заводить не пришлось.
        background.SetColor(Color.Argb(TilesLayout.BackgroundAlpha, palette.Dim.R, palette.Dim.G, palette.Dim.B));
        _filled = background;
        Background = background;

        _outlinePaint.SetStyle(Paint.Style.Stroke);
        _outlinePaint.StrokeWidth = context.Dp(TilesLayout.OutlineDp);
        _outlinePaint.Color = Color.Argb(TilesLayout.HandleAlpha, palette.Dim.R, palette.Dim.G, palette.Dim.B);

        _label = new TextView(context);
        _label.SetTextSize(ComplexUnitType.Sp, TilesLayout.LabelSp);
        _label.SetTextColor(palette.Dim);
        _label.SetMaxLines(1);
        _label.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        AddView(_label);

        var row = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);

        _value = new TextView(context) { Text = NoValue, Gravity = GravityFlags.Center };
        _value.SetTextColor(palette.Ink);
        _value.SetMaxLines(1);
        // Кегль подбирает платформа (API 26+): число занимает плитку целиком — в узкой однострочной
        // и в широкой двухстрочной оно одно и то же, но разного размера. Своего расчёта здесь быть
        // не должно: встроенный меряет тем же кодом, которым потом и рисует.
        //
        // Ширина и высота у него не WrapContent намеренно — при них автоподбору не от чего
        // отталкиваться, и он не работает вовсе (документация Android, «Autosizing TextView»).
        _value.SetAutoSizeTextTypeUniformWithConfiguration(
            TilesLayout.ValueMinSp, TilesLayout.ValueMaxSp, TilesLayout.ValueStepSp, (int)ComplexUnitType.Sp);
        row.AddView(_value, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));

        _handlePaint.Color = Color.Argb(TilesLayout.HandleAlpha, palette.Ink.R, palette.Ink.G, palette.Ink.B);

        _unit = new TextView(context);
        _unit.SetTextSize(ComplexUnitType.Sp, TilesLayout.UnitSp);
        _unit.SetTextColor(palette.Dim);
        _unit.SetPadding(context.Dp(TilesLayout.UnitGapDp), 0, 0, 0);
        row.AddView(_unit);

        // Остаток плитки под числом: подпись сверху, число — во всём, что осталось, и по центру
        // этого остатка. Отсюда и «стоит по центру плитки», и то, во что упирается автоподбор.
        AddView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f)
        {
            TopMargin = context.Dp(TilesLayout.ValueTopMarginDp),
        });
    }

    /// <summary>
    /// Режим правки: плитка помечается уголком. Метка, а не ручка — размер задают в меню плитки, а
    /// уголок отвечает на единственный вопрос: правится сейчас экран или показывается.
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
    /// Чью величину показывать. Слова приходят готовыми, а не ключами: библиотека ресурсов
    /// приложения не видит — тот же порядок, что у подписей шторки и плашки связи.
    /// </summary>
    public void Bind(MetricDescriptor metric, string label, string unit, TileSize size)
    {
        _metric = metric;
        _empty = false;
        _format = "F" + metric.Decimals;
        _label.Text = label;
        _unit.Text = unit;
        _unit.Visibility = unit.Length == 0 ? ViewStates.Gone : ViewStates.Visible;
        _value.Visibility = ViewStates.Visible;
        Background = _filled;

        SetRows(size.Rows);

        _shown = "";
        Render(null);
    }

    /// <summary>
    /// Пустое место: ни подписи, ни числа, ни подложки — только клетки, которые оно занимает. В
    /// режиме правки за него берутся пальцем, поэтому там оно обведено контуром.
    /// </summary>
    public void BindEmpty(TileSize size)
    {
        _metric = null;
        _empty = true;
        _label.Text = "";
        _unit.Visibility = ViewStates.Gone;
        _value.Visibility = ViewStates.Gone;
        Background = null;

        SetRows(size.Rows);
    }

    /// <summary>
    /// Точная высота вместо высоты по содержимому: строка сетки — одна мера на всех
    /// (<see cref="TilesLayout.RowHeightDp"/>), и двухстрочная плитка встаёт ровно на место двух
    /// однострочных вместе с просветом между ними.
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

    /// <summary>
    /// Очередной снимок. Зовётся на каждом кадре, поэтому текст переставляется только при
    /// изменении: <c>TextView.SetText</c> тянет за собой перекладку строки, а число меняется впятеро
    /// реже, чем идут кадры.
    /// </summary>
    public void Render(TelemetrySnapshot? snapshot)
    {
        if (_metric is not { } metric) return;

        double? value = snapshot is null ? null : metric.Read(snapshot);
        string text = value is { } number ? number.ToString(_format) : NoValue;

        if (_shown == text) return;

        _shown = text;
        _value.Text = text;
    }
}
