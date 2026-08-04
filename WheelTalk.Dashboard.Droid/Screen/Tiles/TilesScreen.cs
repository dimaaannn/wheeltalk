using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Metrics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Второй основной экран (план 23 §3): сетка плиток с числами живого колеса. Смотрят его не на
/// ходу — на стоянке, после поездки, при зарядке, — поэтому здесь нет ни лент, ни моргания: числа
/// стоят и читаются.
/// <para>
/// Тот же <see cref="IMainScreen"/>, что и панель: принимает посчитанное состояние кадра и ни к
/// сессии, ни к рекордеру, ни к контейнеру служб не ходит. Живой снимок приходит кадром
/// (<see cref="MainScreenFrame.Snapshot"/>).
/// </para>
/// <para>
/// Укладывает плитки <see cref="TileGridLayoutManager"/> — клеточная сетка с поиском места потоком.
/// Почему не <c>GridLayoutManager</c>, одобренный планом, написано там же: ряд «половина плюс
/// четыре четвертных» линейной сеткой не выкладывается.
/// </para>
/// <para>
/// <b>Правка раскладки</b> (шаг 6): долгий тап включает режим, в нём плитку тащат пальцем, а тап по
/// плитке открывает её меню — величина, размер, «убрать». Новую заводит кнопка «плюс». Правка живёт
/// в <see cref="TileLayoutDraft"/>, до настройки она пока не доходит.
/// </para>
/// </summary>
public sealed class TilesScreen : IMainScreen
{
    private readonly Context _context;
    private readonly Func<string, string> _translate;
    private readonly IMetricHistory? _history;
    private readonly DashboardOptions _options;
    private readonly DashboardPalette _palette;
    private readonly FrameLayout _root;
    private readonly RecyclerView _list;
    private readonly View _buttons;
    private readonly TileAdapter _adapter;
    private readonly int _padding;

    private int _topInset = -1;
    private bool _editing;
    private long _polledAt;

    /// <summary>Раскладка на входе в режим правки: к ней возвращает «отменить» и кнопка «назад».</summary>
    private IReadOnlyList<MetricTile> _beforeEditing = [];

    /// <param name="translate">
    /// Ключ ресурса → слово. Слова — забота вызывающего, как у подписей шторки: у приложения они
    /// переводимые, у стенда свои, а библиотека ресурсов приложения не видит.
    /// </param>
    /// <param name="history">
    /// Откуда плитки-графики берут историю. <c>null</c> — истории нет вовсе (запись выключена, база
    /// не открыта): графики останутся пустыми, остальные плитки работают как работали.
    /// </param>
    public TilesScreen(Context context, DashboardOptions options, Func<string, string> translate,
        IMetricHistory? history = null)
    {
        _context = context;
        _translate = translate;
        _history = history;
        _options = options;
        _palette = options.Palette;
        _padding = context.Dp(6);
        _adapter = new TileAdapter(context, options, translate, TileLayoutDraft.Tiles);

        _list = new RecyclerView(context);
        _list.SetLayoutManager(LayoutManager(context));
        _list.SetAdapter(_adapter);
        _list.SetBackgroundColor(options.Palette.Background);
        // Отступ сверху отдан инсету статус-бара, и содержимое под ним должно прокручиваться, а не
        // обрезаться по краю паддинга.
        _list.SetClipToPadding(false);
        _list.AddOnItemTouchListener(new TileTouch(context, this));

        new ItemTouchHelper(new DragCallback(this)).AttachToRecyclerView(_list);

        _buttons = EditButtons(context, options.Palette);

        _root = new FrameLayout(context);
        _root.AddView(_list, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _root.AddView(_buttons, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Bottom));
    }

    public View View => _root;

    /// <summary>Плитки намерений не подают: правка раскладки не выходит за пределы экрана.</summary>
    public Action<MainScreenIntent>? OnIntent { get; set; }

    public void Show(MainScreenFrame frame)
    {
        ApplyInset((int)frame.TopInset);
        _adapter.Render(frame.Snapshot);
        PollCharts();
    }

    /// <summary>
    /// Перечитать историю показанных графиков. Зовётся с кадром, а работает раз в секунду-две
    /// (<see cref="TilesLayout.ChartPollMs"/>): своего таймера заводить незачем — кадры и так идут,
    /// а лишний источник частоты дал бы биение с ними.
    /// <para>
    /// Обходятся <b>показанные</b> плитки, а не вся раскладка: то, что уехало за край, читать из
    /// базы не нужно, а вернётся оно уже с точками.
    /// </para>
    /// </summary>
    private void PollCharts()
    {
        if (_history is null) return;

        long now = Environment.TickCount64;
        if (now - _polledAt < TilesLayout.ChartPollMs) return;

        _polledAt = now;

        for (int index = 0; index < _list.ChildCount; index++)
        {
            if (_list.GetChildAt(index) is not ChartTileView chart) continue;

            int position = _list.GetChildAdapterPosition(chart);
            if (position < 0 || _adapter.TileAt(position) is not { Chart: { } options } tile) continue;

            _ = FillAsync(chart, tile.MetricId, options.Window);
        }
    }

    private async Task FillAsync(ChartTileView chart, string metricId, TimeSpan window)
    {
        var to = DateTimeOffset.Now;
        var from = to - window;
        var points = await _history!.ReadAsync(metricId, from, to, chart.Points, CancellationToken.None);

        // Читали вне потока отрисовки — возвращаемся в него: точки ставит тот, кто рисует.
        chart.Post(() => chart.SetPoints(points, from, to));
    }

    public void Tap(float windowX, float windowY)
    {
    }

    /// <summary>
    /// «Назад» в режиме правки значит «не сохранять» — то же, что кнопка «отменить». Иначе кнопка
    /// закрывала бы приложение вместе с незакрытой правкой.
    /// </summary>
    public bool Back()
    {
        if (!_editing) return false;

        StopEditing(save: false);
        return true;
    }

    /// <summary>
    /// Верхний инсет забирает сам экран, как и панель: фон уходит под статус-бар, а числа
    /// начинаются ниже него. Переставляется только при изменении — правка паддинга тянет перекладку
    /// всей сетки, а кадров шестьдесят в секунду.
    /// </summary>
    private void ApplyInset(int top)
    {
        if (_topInset == top) return;

        _topInset = top;
        _list.SetPadding(_padding, top + _padding, _padding, _padding);
    }

    /// <summary>
    /// Долгий тап по плитке включает режим правки — привычный для сеток жест, которому не надо
    /// учить (план 23 §3.2). В самом режиме долгий тап уже занят: им берут плитку и тащат, и это
    /// делает <see cref="ItemTouchHelper"/> своим счётом.
    /// </summary>
    private void LongPress(float x, float y)
    {
        if (_editing || _list.FindChildViewUnder(x, y) is null) return;

        _beforeEditing = _adapter.Snapshot();
        SetEditing(true);
    }

    /// <summary>
    /// В режиме правки короткий тап по плитке открывает её меню. Мимо плиток он не значит ничего:
    /// правку заканчивают кнопкой, а не промахом — иначе непонятно, легла она или потерялась. Вне
    /// режима короткий тап не занят вовсе: его ждёт плитка-график, которой он откроет полноэкранный
    /// просмотр (решение владельца 04.08.2026).
    /// </summary>
    private void SingleTap(float x, float y)
    {
        if (_list.FindChildViewUnder(x, y) is not { } view) return;

        int position = _list.GetChildAdapterPosition(view);
        if (position < 0) return;

        if (_editing)
        {
            ShowEditor(position);
            return;
        }

        // Вне правки короткий тап принадлежит графику: он открывает полноэкранный просмотр
        // (решение владельца 04.08.2026). По остальным плиткам тапать пока нечего.
        if (_history is { } history
            && _adapter.TileAt(position) is { Kind: TileKind.Chart, Chart: { } options } tile
            && MetricCatalogue.Find(tile.MetricId) is { } metric)
        {
            ChartViewer.Show(_context, _options, history, metric, _translate(metric.LabelKey),
                metric.UnitKey is { } unit ? _translate(unit) : "", options, tile.Limits);
        }
    }

    /// <param name="position">Правим плитку с этим номером либо <c>null</c> — заводим новую.</param>
    private void ShowEditor(int? position)
    {
        var tile = position is { } index ? _adapter.TileAt(index) : null;

        TileEditor.Show(_context, _translate, tile,
            saved =>
            {
                if (position is { } index)
                {
                    _adapter.Replace(index, saved);
                    return;
                }

                _adapter.Add(saved);

                // Новая плитка встаёт в конец, а конец бывает за краем экрана: без этого «нажал ОК —
                // и ничего не изменилось», хотя плитка уже есть.
                _list.SmoothScrollToPosition(_adapter.ItemCount - 1);
            },
            position is { } removed ? () => _adapter.RemoveAt(removed) : null);
    }

    /// <summary>
    /// Конец правки: сохранить — оставить как есть, отменить — вернуть раскладку, какой она была на
    /// входе в режим. До этого мига правки живут только в адаптере, а черновик
    /// (<see cref="TileLayoutDraft"/>) пишется одним разом: пиши он на каждое действие, «отменить»
    /// отменяло бы лишь последнее.
    /// </summary>
    private void StopEditing(bool save)
    {
        if (save) _adapter.Keep();
        else _adapter.Restore(_beforeEditing);

        SetEditing(false);
    }

    private void SetEditing(bool editing)
    {
        _editing = editing;
        _adapter.Editing = editing;
        _buttons.Visibility = editing ? ViewStates.Visible : ViewStates.Gone;
    }

    /// <summary>
    /// Который из двух укладчиков собирает сетку — <see cref="TilesLayout.PackTiles"/>. Оба живут
    /// рядом, пока не решено глазами: у сетки из плана дырки под низкими плитками остаются пустыми,
    /// свой их заполняет.
    /// </summary>
    private RecyclerView.LayoutManager LayoutManager(Context context)
    {
        if (TilesLayout.PackTiles) return new TileGridLayoutManager(context, _adapter.SizeAt);

        var grid = new GridLayoutManager(context, TilesLayout.Columns);
        grid.SetSpanSizeLookup(new TileSpans(_adapter.SizeAt));

        return grid;
    }

    /// <summary>Ширина плитки в колонках — единственное, что <c>GridLayoutManager</c> умеет спросить.</summary>
    private sealed class TileSpans(Func<int, TileSize> sizeAt) : GridLayoutManager.SpanSizeLookup
    {
        public override int GetSpanSize(int position) => sizeAt(position).Columns;
    }

    /// <summary>
    /// Полоса режима правки: завести плитку, отменить, сохранить. Внизу и во всю ширину — так её не
    /// закрывает палец, которым тащат плитку, и так видно, что правка ещё не закончена.
    /// </summary>
    private View EditButtons(Context context, DashboardPalette palette)
    {
        var row = new LinearLayout(context)
        {
            Orientation = Android.Widget.Orientation.Horizontal,
            Visibility = ViewStates.Gone,
        };

        int pad = context.Dp(TilesLayout.ButtonGapDp);
        row.SetPadding(pad, pad, pad, pad);
        row.SetBackgroundColor(palette.Background);

        row.AddView(Button(context, palette, "+", () => ShowEditor(null)), Weighted(context, 0.5f));
        row.AddView(Button(context, palette, _translate("TilesEditCancel"), () => StopEditing(save: false)), Weighted(context, 1f));
        row.AddView(Button(context, palette, _translate("TilesEditSave"), () => StopEditing(save: true)), Weighted(context, 1f));

        return row;
    }

    private static LinearLayout.LayoutParams Weighted(Context context, float weight)
    {
        int gap = context.Dp(TilesLayout.ButtonGapDp);

        return new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, weight)
        {
            LeftMargin = gap,
            RightMargin = gap,
        };
    }

    private static View Button(Context context, DashboardPalette palette, string text, Action tapped)
    {
        var background = new GradientDrawable();
        background.SetShape(ShapeType.Rectangle);
        background.SetCornerRadius(context.Dp(TilesLayout.CornerRadiusDp));
        background.SetColor(Color.Argb(TilesLayout.BackgroundAlpha, palette.Dim.R, palette.Dim.G, palette.Dim.B));

        var button = new TextView(context)
        {
            Text = text,
            Gravity = GravityFlags.Center,
            Background = background,
        };

        int pad = context.Dp(TilesLayout.ButtonPaddingDp);
        button.SetPadding(pad, pad, pad, pad);
        button.SetTextColor(palette.Ink);
        button.SetTextSize(ComplexUnitType.Sp, TilesLayout.ButtonSp);
        button.Click += (_, _) => tapped();

        return button;
    }

    /// <summary>
    /// Жесты сетки. Один класс на две роли намеренно: <see cref="RecyclerView"/> отдаёт события
    /// только слушателю своего вида, а разбирает их <see cref="GestureDetector"/>, и разводить их по
    /// двум классам значило бы завести третий — мост между ними.
    /// <para>
    /// События не перехватываются (<c>false</c> из <see cref="OnInterceptTouchEvent"/>): прокрутка
    /// сетки и перетаскивание плитки должны видеть тот же палец.
    /// </para>
    /// </summary>
    private sealed class TileTouch : GestureDetector.SimpleOnGestureListener, RecyclerView.IOnItemTouchListener
    {
        private readonly TilesScreen _screen;
        private readonly GestureDetector _gestures;

        public TileTouch(Context context, TilesScreen screen)
        {
            _screen = screen;
            _gestures = new GestureDetector(context, this);
        }

        public bool OnInterceptTouchEvent(RecyclerView view, MotionEvent e)
        {
            _gestures.OnTouchEvent(e);
            return false;
        }

        public void OnTouchEvent(RecyclerView view, MotionEvent e)
        {
        }

        public void OnRequestDisallowInterceptTouchEvent(bool disallow)
        {
        }

        public override void OnLongPress(MotionEvent e) => _screen.LongPress(e.GetX(), e.GetY());

        public override bool OnSingleTapUp(MotionEvent e)
        {
            _screen.SingleTap(e.GetX(), e.GetY());
            return false;
        }
    }

    /// <summary>
    /// Перетаскивание плиток — все четыре направления: без горизонтали сетка не двигается
    /// (план 23 §3.3). Смахивания нет: убрать плитку жестом — не то, что делают случайно, для этого
    /// есть «убрать» в меню плитки.
    /// </summary>
    private sealed class DragCallback(TilesScreen screen) : ItemTouchHelper.SimpleCallback(
        ItemTouchHelper.Up | ItemTouchHelper.Down | ItemTouchHelper.Left | ItemTouchHelper.Right, 0)
    {
        /// <summary>Тащить можно только в режиме правки: иначе экран уезжал бы под пальцем у того, кто просто смотрит.</summary>
        public override bool IsLongPressDragEnabled => screen._editing;

        /// <summary>
        /// Насколько глубоко палец должен зайти на соседа, чтобы сетка переложилась. При половине —
        /// значении по умолчанию — она перетекает от каждого касания краёв, и предсказать, куда
        /// встанет плитка, нельзя: к отпусканию картина уже другая, чем была в начале.
        /// </summary>
        public override float GetMoveThreshold(RecyclerView.ViewHolder holder) => TilesLayout.DragMoveThreshold;

        public override bool OnMove(RecyclerView view, RecyclerView.ViewHolder holder, RecyclerView.ViewHolder target)
        {
            screen._adapter.Move(holder.BindingAdapterPosition, target.BindingAdapterPosition);
            return true;
        }

        public override void OnSwiped(RecyclerView.ViewHolder holder, int direction)
        {
        }
    }

    /// <summary>
    /// Плитки как список: адаптер отдаёт вид, а величину каждой берёт из каталога по имени. Плитка,
    /// сославшаяся на неизвестную величину, в список не попадает вовсе — то же правило, которым
    /// план 17 §4 требует отвергать скин со ссылкой на несуществующую величину.
    /// </summary>
    private sealed class TileAdapter : RecyclerView.Adapter
    {
        private readonly Context _context;
        private readonly DashboardOptions _options;
        private readonly Func<string, string> _translate;
        /// <summary>Величина у пустого места пуста: <see cref="TileKind.Empty"/> ни на что не ссылается.</summary>
        private readonly List<(MetricTile Tile, MetricDescriptor? Metric)> _tiles = [];

        /// <summary>
        /// Все созданные плитки. <see cref="RecyclerView"/> их переиспользует, но не выбрасывает, а
        /// экран короткий — так кадр обходит готовый список вместо <c>NotifyItemChanged</c> по
        /// каждой плитке пять раз в секунду.
        /// </summary>
        private readonly List<TileView> _views = [];

        private TelemetrySnapshot? _snapshot;
        private bool _editing;

        public TileAdapter(Context context, DashboardOptions options, Func<string, string> translate,
            IReadOnlyList<MetricTile> layout)
        {
            _context = context;
            _options = options;
            _translate = translate;

            foreach (var tile in layout)
            {
                if (Entry(tile) is { } entry) _tiles.Add(entry);
            }

            // Отсев неизвестных величин уходит и в черновик: иначе позиция плитки на экране разошлась
            // бы с позицией в хранимом списке, и перенос двигал бы не ту.
            Keep();
        }

        public override int ItemCount => _tiles.Count;

        /// <summary>Размер плитки для укладчика: сетке он нужен до того, как плитка построена.</summary>
        public TileSize SizeAt(int position) => _tiles[position].Tile.Size;

        public MetricTile TileAt(int position) => _tiles[position].Tile;

        /// <summary>Режим правки одинаков для всех плиток сразу — правят раскладку, а не одну из них.</summary>
        public bool Editing
        {
            set
            {
                _editing = value;
                foreach (var view in _views) view.Editing = value;
            }
        }

        /// <summary>Перенести плитку. Порядок в списке и есть порядок на экране — другого хранения позиции нет.</summary>
        public void Move(int from, int to)
        {
            var moved = _tiles[from];
            _tiles.RemoveAt(from);
            _tiles.Insert(to, moved);

            NotifyItemMoved(from, to);
        }

        public void Add(MetricTile tile)
        {
            if (Entry(tile) is not { } entry) return;

            _tiles.Add(entry);
            NotifyItemInserted(_tiles.Count - 1);
        }

        public void Replace(int position, MetricTile tile)
        {
            if (Entry(tile) is not { } entry) return;

            _tiles[position] = entry;
            NotifyItemChanged(position);
        }

        public void RemoveAt(int position)
        {
            _tiles.RemoveAt(position);
            NotifyItemRemoved(position);
        }

        /// <summary>Раскладка как она есть сейчас — её запоминают на входе в режим правки.</summary>
        public IReadOnlyList<MetricTile> Snapshot() => [.. _tiles.Select(entry => entry.Tile)];

        /// <summary>Вернуть раскладку из снимка — «отменить».</summary>
        public void Restore(IReadOnlyList<MetricTile> tiles)
        {
            _tiles.Clear();
            foreach (var tile in tiles)
            {
                if (Entry(tile) is { } entry) _tiles.Add(entry);
            }

            NotifyDataSetChanged();
        }

        /// <summary>Запомнить правку — «сохранить».</summary>
        public void Keep() => TileLayoutDraft.Keep(_tiles.Select(entry => entry.Tile));

        /// <summary>
        /// Плитка со своей величиной. Пустое место величины не имеет вовсе, а плитка, сославшаяся на
        /// неизвестную, отвергается целиком — то же правило, которым план 17 §4 требует отвергать
        /// скин со ссылкой на несуществующую величину.
        /// </summary>
        private static (MetricTile Tile, MetricDescriptor? Metric)? Entry(MetricTile tile)
        {
            if (tile.Kind == TileKind.Empty) return (tile, null);

            return MetricCatalogue.Find(tile.MetricId) is { } metric ? (tile, metric) : null;
        }

        /// <summary>
        /// Вид рисовальщика и есть тип держателя: число и график — разные <c>View</c>, и переиспользовать
        /// одну под другую нельзя. Пустое место идёт как число: рамка у них общая, а содержимого у
        /// него нет вовсе.
        /// </summary>
        public override int GetItemViewType(int position) => _tiles[position].Tile.Kind == TileKind.Chart ? 1 : 0;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            TileView view = viewType == 1
                ? new ChartTileView(_context, _options, _translate)
                : new MetricTileView(_context, _options);

            // Плитка могла родиться уже посреди правки: сетка создаёт держатели по мере надобности.
            view.Editing = _editing;

            // Просветы полями держит GridLayoutManager; свой укладчик отступает сам и эти поля
            // не читает — вреда от них там нет.
            view.LayoutParameters = new RecyclerView.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                LeftMargin = _context.Dp(TilesLayout.GapDp),
                RightMargin = _context.Dp(TilesLayout.GapDp),
                TopMargin = _context.Dp(TilesLayout.GapDp),
                BottomMargin = _context.Dp(TilesLayout.GapDp),
            };

            _views.Add(view);
            return new TileHolder(view);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            if (holder is not TileHolder tile) return;

            var (layout, metric) = _tiles[position];

            if (metric is null)
            {
                tile.Tile.BindEmpty(layout.Size);
                return;
            }

            string label = _translate(metric.LabelKey);
            string unit = metric.UnitKey is { } key ? _translate(key) : "";

            if (tile.Tile is ChartTileView chart)
            {
                chart.Bind(metric, label, unit, layout.Size, layout.ShowLabel,
                    layout.Chart ?? new TileChart(TilesLayout.ChartWindows[0], ShowValue: true, Zoom: false),
                    layout.Limits);
            }
            else if (tile.Tile is MetricTileView value)
            {
                value.Bind(metric, label, unit, layout.Size, layout.ShowLabel, layout.Limits);
            }

            tile.Tile.Render(_snapshot);
        }

        /// <summary>
        /// Кадров шестьдесят в секунду, а снимков — пять: пока приходит тот же самый, считать нечего.
        /// Сравнение по ссылке, а не по значению, и этого достаточно — снимок неизменяем, и каждый
        /// новый отсчёт колеса приходит новым объектом.
        /// </summary>
        public void Render(TelemetrySnapshot? snapshot)
        {
            if (ReferenceEquals(_snapshot, snapshot)) return;

            _snapshot = snapshot;
            foreach (var view in _views) view.Render(snapshot);
        }

        private sealed class TileHolder(TileView tile) : RecyclerView.ViewHolder(tile)
        {
            public TileView Tile { get; } = tile;
        }
    }
}
