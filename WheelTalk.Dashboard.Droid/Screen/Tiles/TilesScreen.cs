using Android.Content;
using Android.Views;
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
/// Сетка — <see cref="GridLayoutManager"/> на шесть колонок и <see cref="GridLayoutManager.SpanSizeLookup"/>
/// (план 23 §3.3): шесть есть НОК для одной, двух и трёх плиток в ряд, и ширина плитки — это её
/// спан 6/3/2. Готового такого не нашлось разведкой 03.08.2026 — ни одна найденная библиотека не
/// умеет менять ширину, — а <see cref="RecyclerView"/> в проекте уже одобрен.
/// </para>
/// </summary>
public sealed class TilesScreen : IMainScreen
{
    private readonly RecyclerView _list;
    private readonly TileAdapter _adapter;
    private readonly int _padding;

    private int _topInset = -1;

    /// <param name="translate">
    /// Ключ ресурса → слово. Слова — забота вызывающего, как у подписей шторки: у приложения они
    /// переводимые, у стенда свои, а библиотека ресурсов приложения не видит.
    /// </param>
    public TilesScreen(Context context, DashboardOptions options, Func<string, string> translate)
    {
        _padding = context.Dp(6);
        _adapter = new TileAdapter(context, options.Palette, translate, TilesLayout.Fixed);

        var grid = new GridLayoutManager(context, TilesLayout.Columns);
        grid.SetSpanSizeLookup(_adapter.Spans);

        _list = new RecyclerView(context);
        _list.SetLayoutManager(grid);
        _list.SetAdapter(_adapter);
        _list.SetBackgroundColor(options.Palette.Background);
        // Отступ сверху отдан инсету статус-бара, и содержимое под ним должно прокручиваться, а не
        // обрезаться по краю паддинга.
        _list.SetClipToPadding(false);
    }

    public View View => _list;

    /// <summary>Плитки намерений не подают: тапать на этом экране пока нечего — правка раскладки это шаг 5.</summary>
    public Action<MainScreenIntent>? OnIntent { get; set; }

    public void Show(MainScreenFrame frame)
    {
        ApplyInset((int)frame.TopInset);
        _adapter.Render(frame.Snapshot);
    }

    public void Tap(float windowX, float windowY)
    {
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
    /// Плитки как список: адаптер отдаёт вид, а величину каждой берёт из каталога по имени. Плитка,
    /// сославшаяся на неизвестную величину, в список не попадает вовсе — то же правило, которым
    /// план 17 §4 требует отвергать скин со ссылкой на несуществующую величину.
    /// </summary>
    private sealed class TileAdapter : RecyclerView.Adapter
    {
        private readonly Context _context;
        private readonly DashboardPalette _palette;
        private readonly Func<string, string> _translate;
        private readonly List<(MetricTile Tile, MetricDescriptor Metric)> _tiles = [];

        /// <summary>
        /// Все созданные плитки. <see cref="RecyclerView"/> их переиспользует, но не выбрасывает, а
        /// экран короткий — так кадр обходит готовый список вместо <c>NotifyItemChanged</c> по
        /// каждой плитке пять раз в секунду.
        /// </summary>
        private readonly List<MetricTileView> _views = [];

        private TelemetrySnapshot? _snapshot;

        public TileAdapter(Context context, DashboardPalette palette, Func<string, string> translate,
            IReadOnlyList<MetricTile> layout)
        {
            _context = context;
            _palette = palette;
            _translate = translate;

            foreach (var tile in layout)
            {
                if (MetricCatalogue.Find(tile.MetricId) is { } metric) _tiles.Add((tile, metric));
            }

            Spans = new TileSpans(_tiles);
        }

        public GridLayoutManager.SpanSizeLookup Spans { get; }

        public override int ItemCount => _tiles.Count;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var view = new MetricTileView(_context, _palette)
            {
                // Высоту ставит сама плитка по своей ширине (MetricTileView.Bind): сетка знает
                // только про колонки.
                LayoutParameters = new RecyclerView.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
                {
                    LeftMargin = _context.Dp(TilesLayout.GapDp),
                    RightMargin = _context.Dp(TilesLayout.GapDp),
                    TopMargin = _context.Dp(TilesLayout.GapDp),
                    BottomMargin = _context.Dp(TilesLayout.GapDp),
                },
            };

            _views.Add(view);
            return new TileHolder(view);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            if (holder is not TileHolder tile) return;

            var (layout, metric) = _tiles[position];
            tile.Tile.Bind(metric, _translate(metric.LabelKey),
                metric.UnitKey is { } unit ? _translate(unit) : "", layout.Width);
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

        private sealed class TileHolder(MetricTileView tile) : RecyclerView.ViewHolder(tile)
        {
            public MetricTileView Tile { get; } = tile;
        }

        private sealed class TileSpans(List<(MetricTile Tile, MetricDescriptor Metric)> tiles)
            : GridLayoutManager.SpanSizeLookup
        {
            public override int GetSpanSize(int position) => (int)tiles[position].Tile.Width;
        }
    }
}
