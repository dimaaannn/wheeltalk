using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Tiles;

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

    /// <summary>
    /// Рамка жара — своя, а не штрих подложки: её низ обязан уметь <b>не рисоваться</b>, когда
    /// нижней стороной служит шкала (решение владельца 10.08.2026). <c>GradientDrawable</c> рисует
    /// контур только целиком, поэтому рамка перешла сюда.
    /// </summary>
    private readonly Paint _framePaint = new() { AntiAlias = true };
    private readonly Android.Graphics.Path _frame = new();
    private readonly RectF _frameBox = new();

    /// <summary>Угол под дугу — поле, а не местная переменная: путь строится в отрисовке, а мусорить в ней нельзя.</summary>
    private readonly RectF _corner = new();

    /// <summary>С каким низом собран путь сейчас, чтобы не собирать его заново на каждый кадр.</summary>
    private bool _frameOpen;
    private bool _frameReady;

    /// <summary>Метки начала и конца шкалы — на концах прямого участка низа.</summary>
    private readonly Paint _tickPaint = new() { AntiAlias = true };

    /// <summary>Прямой участок низа, между закруглениями: по нему идёт шкала, углы она не трогает.</summary>
    private float _scaleFrom;
    private float _scaleTo;
    private float _scaleY;

    private double _heat;

    /// <summary>Круг «убрать» и ручка перетаскивания — видны только в правке, обе нажимаемы.</summary>
    private readonly Paint _editPaint = new() { AntiAlias = true };
    private readonly RectF _remove = new();

    private bool _editing;
    private bool _empty;

    /// <summary>Подпись размера («1/2 × 2») — только в правке и только там, где для неё есть место.</summary>
    private string _sizeLabel = "";

    /// <summary>Показывать ли полоску жара — выбор человека на этой плитке (умолчание «да»).</summary>
    private bool _showHeatBar = true;

    /// <summary>
    /// Подпись квадратной плитки: она не строка разметки, а метка в углу, и рисуется здесь же.
    /// Пусто — плитка не квадратная либо подпись выключена.
    /// </summary>
    private string _cornerLabel = "";

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

        _framePaint.SetStyle(Paint.Style.Stroke);
        _framePaint.StrokeWidth = context.Dp(TilesLayout.HeatStrokeDp);
        _framePaint.StrokeCap = Paint.Cap.Butt;

        _tickPaint.SetStyle(Paint.Style.Fill);
        _tickPaint.Color = Color.Argb(TilesLayout.HeatTickAlpha, palette.Ink.R, palette.Ink.G, palette.Ink.B);

        _editPaint.Color = Color.Argb(TilesLayout.HandleAlpha, palette.Ink.R, palette.Ink.G, palette.Ink.B);

        _outlinePaint.SetStyle(Paint.Style.Stroke);
        _outlinePaint.StrokeWidth = context.Dp(TilesLayout.OutlineDp);
        _outlinePaint.Color = Color.Argb(TilesLayout.HandleAlpha, palette.Dim.R, palette.Dim.G, palette.Dim.B);

        Label = new TextView(context);
        Label.SetTextSize(ComplexUnitType.Dip, TilesLayout.LabelSp);
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
        if (Math.Abs(_heat - heat) > 0.001)
        {
            _heat = heat;
            Invalidate();
        }

        var tint = MetricHeat.Tint(heat, Palette);
        var stroke = heat <= 0
            ? Color.Transparent
            : Color.Argb(MetricHeat.Alpha(heat), tint.R, tint.G, tint.B);

        if (_fill == stroke) return;

        _fill = stroke;
        _framePaint.Color = stroke;
        Invalidate();
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
    /// Есть ли у этого вида что сбрасывать. Меню плитки одно на все виды (решение владельца
    /// 10.08.2026), и надпись в нём общая — «Сбросить»; отвечает на неё каждый вид по-своему, а у
    /// кого ответа нет, у того пункт погашен.
    /// </summary>
    public virtual bool CanReset => false;

    /// <summary>
    /// Начать заново — то, что у этого вида значит сброс: у крайнего значения пик, у дистанции
    /// точка отсчёта. Зовётся <b>только из меню плитки</b>: голый тап сбрасывал пик случайным
    /// касанием, и владелец это поведение снял.
    /// </summary>
    public virtual void ResetValue()
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
    protected void BindFrame(string label, TileSize size, bool showLabel, bool heatBar = true)
    {
        _showHeatBar = heatBar;
        _empty = false;
        Label.Text = label;
        Label.Visibility = showLabel ? ViewStates.Visible : ViewStates.Gone;
        Background = _filled;
        ShowContent(true);
        _sizeLabel = SizeLabel(size);
        SetRows(size.Rows);
    }

    /// <summary>Показать или спрятать то, что вид добавил под подписью.</summary>
    protected abstract void ShowContent(bool visible);

    /// <summary>
    /// Форма плитки (план плиток §2). Обе формы — те же два ребёнка, только разложенные иначе:
    /// «столбик» ставит подпись над числом, «строка» — слева от него, в одну линию. Заводить под
    /// вторую форму вторую разметку значило бы держать две правды об одной плитке.
    /// <para>
    /// Подпись «строки» крупнее и ограничена долей ширины (<see cref="TilesLayout.RowLabelShare"/>):
    /// дальше она обрезается многоточием, а не съедает кегль числа.
    /// </para>
    /// </summary>
    protected void ApplyForm(TileForm form, TileSize size)
    {
        // Квадрат: подпись уходит меткой в угол, а разметку целиком забирает число — ради этого
        // квадратную плитку и заводят (решение владельца 10.08.2026). Строкой она бы съела у числа
        // высоту, а числу в квадрате места и так не хватает: упирается оно в ширину.
        if (form == TileForm.Square)
        {
            _cornerLabel = Label.Visibility == ViewStates.Visible ? Label.Text ?? "" : "";
            Label.Visibility = ViewStates.Gone;
            Orientation = Android.Widget.Orientation.Vertical;
            SetGravity(GravityFlags.Top);
            Invalidate();
            return;
        }

        _cornerLabel = "";
        bool row = form == TileForm.Row;
        Orientation = row ? Android.Widget.Orientation.Horizontal : Android.Widget.Orientation.Vertical;
        SetGravity(row ? GravityFlags.CenterVertical : GravityFlags.Top);

        Label.SetTextSize(ComplexUnitType.Dip, row ? TilesLayout.RowLabelSp : TilesLayout.LabelSp);

        if (Label.LayoutParameters is LayoutParams label)
        {
            label.Width = row ? ViewGroup.LayoutParams.WrapContent : ViewGroup.LayoutParams.MatchParent;
            label.Height = ViewGroup.LayoutParams.WrapContent;
            label.Weight = 0;
            label.Gravity = row ? GravityFlags.CenterVertical : GravityFlags.Top;
            Label.LayoutParameters = label;
        }

        int box = Context!.Dp((size.Columns * TilesLayout.RowHeightDp) / 2);
        Label.SetMaxWidth(row
            ? (int)(Width > 0 ? Width * TilesLayout.RowLabelShare : box * TilesLayout.RowLabelShare)
            : int.MaxValue);
    }

    /// <summary>
    /// Величина молчит — плитка гаснет целиком (план плиток §5): бодрый белый прочерк наравне с
    /// живыми числами читался как показание, которого нет.
    /// </summary>
    protected void ShowMuted(bool muted)
    {
        float wanted = muted ? TilesLayout.MutedAlpha : 1f;
        if (Math.Abs(Alpha - wanted) > 0.001f) Alpha = wanted;
    }

    /// <summary>Короткая подпись размера для режима правки: «1/4», «1/2 × 2».</summary>
    protected static string SizeLabel(TileSize size)
    {
        string share = size.Columns >= TilesLayout.Columns ? "1/1"
            : size.Columns * 2 >= TilesLayout.Columns ? "1/2"
            : "1/4";

        return size.Rows > 1 ? $"{share} × {size.Rows}" : share;
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

        // Рамка идёт по краю внутрь, как шла у подложки: линия центрируется на границе, поэтому
        // прямоугольник отступает на половину её толщины.
        float half = _framePaint.StrokeWidth / 2;
        _frameBox.Set(half, half, width - half, height - half);

        // Шкала жара — нижняя сторона рамки, и только её прямой участок: закругления она не
        // трогает, иначе «начало» и «конец» шкалы приходились бы на дугу и длина заливки врала бы.
        _scaleY = _frameBox.Bottom;
        _scaleFrom = _frameBox.Left + _radius;
        _scaleTo = _frameBox.Right - _radius;
        _frameReady = false;

        float pad = Context.Dp(TilesLayout.PaddingDp);
        float circle = Context.Dp(TilesLayout.RemoveSizeDp);
        _remove.Set(width - pad - circle, pad, width - pad, pad + circle);

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

        if (!_empty) DrawFrame(canvas);
        if (_cornerLabel.Length > 0) DrawCornerLabel(canvas);

        if (!_editing) return;

        if (_empty)
        {
            canvas.DrawRoundRect(_outline, _radius, _radius, _outlinePaint);
            return;
        }

        DrawEditMarks(canvas);
    }

    /// <summary>
    /// Рамка жара, и шкала — её нижняя сторона (решение владельца 10.08.2026). Полоска над низом
    /// плитки читалась как рамка соседа снизу; став самой рамкой, она перестала быть вторым
    /// элементом у того же края.
    /// <para>
    /// Когда шкала включена, <b>прямой участок низа не рисуется вовсе</b>: иначе длина заливки
    /// теряется в сплошной линии и «насколько близко» снова отвечает один цвет. Углы при этом
    /// целы — рамка остаётся рамкой, а не разорванной скобкой.
    /// </para>
    /// <para>
    /// При жаре ноль рамки нет вовсе (цвет прозрачный), а метки шкалы стоят: шкала объявлена — на
    /// ней просто нечего заливать.
    /// </para>
    /// </summary>
    private void DrawFrame(Canvas canvas)
    {
        if (_heat > 0)
        {
            // Путь собирается при смене размера плитки и включении шкалы, а не в каждой отрисовке:
            // рисуемся мы шестьдесят раз в секунду на восемнадцати плитках, и сборка пути с дугами
            // — это пять обращений к платформе на каждую (баг 10.08.2026, ANR со 109 % CPU).
            if (!_frameReady || _frameOpen != _showHeatBar) BuildFrame(_showHeatBar);
            canvas.DrawPath(_frame, _framePaint);
        }

        if (!_showHeatBar) return;

        DrawScaleTicks(canvas);

        if (_heat <= 0) return;

        // Заливка растёт от левой метки к правой — тем же цветом и той же толщиной, что и рамка:
        // при полном жаре она смыкается с ней в единое целое.
        float end = _scaleFrom + ((_scaleTo - _scaleFrom) * (float)Math.Clamp(_heat, 0, 1));
        canvas.DrawLine(_scaleFrom, _scaleY, end, _scaleY, _framePaint);
    }

    /// <summary>
    /// Путь рамки. Замкнутый — обычная рамка; разомкнутый — та же рамка без прямого участка низа:
    /// от левого нижнего закругления против часовой стрелки до правого нижнего.
    /// </summary>
    private void BuildFrame(bool openBottom)
    {
        _frame.Reset();
        _frameOpen = openBottom;
        _frameReady = true;

        if (!openBottom)
        {
            _frame.AddRoundRect(_frameBox, _radius, _radius, Android.Graphics.Path.Direction.Cw!);
            return;
        }

        float d = _radius * 2;

        _frame.MoveTo(_scaleFrom, _frameBox.Bottom);
        _corner.Set(_frameBox.Left, _frameBox.Bottom - d, _frameBox.Left + d, _frameBox.Bottom);
        _frame.ArcTo(_corner, 90, 90);

        _corner.Set(_frameBox.Left, _frameBox.Top, _frameBox.Left + d, _frameBox.Top + d);
        _frame.ArcTo(_corner, 180, 90);

        _frame.LineTo(_scaleTo, _frameBox.Top);
        _corner.Set(_frameBox.Right - d, _frameBox.Top, _frameBox.Right, _frameBox.Top + d);
        _frame.ArcTo(_corner, 270, 90);

        _frame.LineTo(_frameBox.Right, _frameBox.Bottom - _radius);
        _corner.Set(_frameBox.Right - d, _frameBox.Bottom - d, _frameBox.Right, _frameBox.Bottom);
        _frame.ArcTo(_corner, 0, 90);
    }

    /// <summary>
    /// Подпись квадратной плитки — меткой в верхнем углу, поверх поля. Своей строки не берёт: в
    /// квадрате число и есть главный житель.
    /// </summary>
    private void DrawCornerLabel(Canvas canvas)
    {
        _tickPaint.Color = Color.Argb(255, Palette.Dim.R, Palette.Dim.G, Palette.Dim.B);
        _tickPaint.TextSize = Context!.Dp(TilesLayout.SquareLabelSp);
        canvas.DrawText(_cornerLabel, Context.Dp(TilesLayout.PaddingDp),
            Context.Dp(TilesLayout.PaddingDp) + _tickPaint.TextSize, _tickPaint);

        // Кисть меток общая — вернуть ей свой цвет обязана та же рука, что заняла.
        _tickPaint.Color = Color.Argb(TilesLayout.HeatTickAlpha, Palette.Ink.R, Palette.Ink.G, Palette.Ink.B);
    }

    /// <summary>
    /// Метки начала и конца шкалы. Стоят всегда, пока шкала включена, — и при нулевом жаре тоже:
    /// без них при частичной заливке не видно, где шкала кончается, а без шкалы вовсе не видно, что
    /// она есть.
    /// </summary>
    private void DrawScaleTicks(Canvas canvas)
    {
        float w = Context!.Dp(TilesLayout.HeatTickWidthDp);
        float h = Context.Dp(TilesLayout.HeatTickHeightDp);

        canvas.DrawRect(_scaleFrom - (w / 2), _scaleY - (h / 2), _scaleFrom + (w / 2), _scaleY + (h / 2), _tickPaint);
        canvas.DrawRect(_scaleTo - (w / 2), _scaleY - (h / 2), _scaleTo + (w / 2), _scaleY + (h / 2), _tickPaint);
    }

    /// <summary>
    /// Метки правки: круг «убрать» у верхнего края и ручка перетаскивания у нижнего. Уголок 14 dp
    /// заменён ими потому, что уголок отвечал на один вопрос — «идёт правка», — а взяться пальцем
    /// было не за что (план плиток §6). Место под обе уже вычтено из бюджета кегля, поэтому число
    /// под ними не печатается.
    /// </summary>
    private void DrawEditMarks(Canvas canvas)
    {
        float radius = _remove.Height() / 2;
        _editPaint.SetStyle(Paint.Style.Fill);
        canvas.DrawRoundRect(_remove, radius, radius, _editPaint);

        _editPaint.SetStyle(Paint.Style.Stroke);
        _editPaint.StrokeWidth = Context!.Dp(2);
        float cross = radius * 0.45f;
        canvas.DrawLine(_remove.CenterX() - cross, _remove.CenterY() - cross,
            _remove.CenterX() + cross, _remove.CenterY() + cross, _editPaint);
        canvas.DrawLine(_remove.CenterX() + cross, _remove.CenterY() - cross,
            _remove.CenterX() - cross, _remove.CenterY() + cross, _editPaint);

        // Ручка — три черты у нижнего правого угла: за неё берутся, чтобы двигать плитку.
        float right = Width - Context.Dp(TilesLayout.PaddingDp);
        float bottom = Height - Context.Dp(TilesLayout.PaddingDp) - Context.Dp(TilesLayout.HeatStrokeDp);
        float wide = Context.Dp(TilesLayout.HandleSizeDp);
        for (int i = 0; i < 3; i++)
        {
            float y = bottom - (i * Context.Dp(4));
            canvas.DrawLine(right - wide, y, right, y, _editPaint);
        }

        if (_sizeLabel.Length == 0 || Width < Context.Dp(TilesLayout.RowHeightDp)) return;

        _editPaint.SetStyle(Paint.Style.Fill);
        _editPaint.TextSize = Context.Dp(TilesLayout.EditSizeSp);
        canvas.DrawText(_sizeLabel, Context.Dp(TilesLayout.PaddingDp), bottom, _editPaint);
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
    /// <para>
    /// <b>Здесь просветы прибавляются, а у подбора кегля — вычитаются, и это не разнобой.</b> При
    /// <c>GridLayoutManager</c> просветы лежат полями <b>снаружи</b> плитки, и высота её — с ними;
    /// свой укладчик врезает плитку <b>внутрь</b> клетки, отступая на просвет сверху и снизу.
    /// Бюджет кегля (<c>TileMetrics.Height</c>) считается по своему укладчику — по тому, что на
    /// экране на самом деле. Сравнявшего эти две формулы ждёт срез низа числа (10.08.2026).
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
