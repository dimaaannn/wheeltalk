using Android.Content;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using WheelTalk.Core.Tiles;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Укладчик плиток: клеточная сетка, где плитка занимает прямоугольник в колонках и строках, а
/// место ей ищется потоком — первое свободное сверху-слева.
/// <para>
/// <b>Почему не <see cref="GridLayoutManager"/>.</b> Тот знает спан только по горизонтали: ряд у
/// него кончается, когда набралась строка, и вторая пара четвертных уехала бы под весь ряд вместо
/// того, чтобы встать вторым этажом рядом с двухстрочной половиной. Ряд «половина плюс четыре
/// четвертных» (владелец 04.08.2026) им не выкладывается ни при какой ширине колонки.
/// </para>
/// <para>
/// <b>Место ищется, а не хранится.</b> Раскладка остаётся списком: порядок плиток и есть раскладка,
/// координат в ней нет — иначе перенос двигал бы не порядок, а пару чисел, и хранение (план 23
/// §3.4) выросло бы вдвое.
/// </para>
/// <para>
/// <b>Поиск идёт только вперёд</b> (владелец 04.08.2026). Заполнять дырки задним числом укладчик
/// умел, и от этого плитки скакали: маленькая из конца списка прыгала в дырку в начале, стоило
/// поправить чужой размер. Теперь что за чем стоит в списке, то так и ложится, а пустое место
/// оставляют нарочно — плиткой <see cref="TileKind.Empty"/>.
/// </para>
/// <para>
/// Плитки не перерабатываются: экран короткий и весь помещается в память — те же полтора десятка
/// <c>View</c>, что адаптер и так держит для отрисовки кадра. Прокрутка двигает готовых детей.
/// </para>
/// </summary>
internal sealed class TileGridLayoutManager(
    Context context, Func<int, TileSize> sizeAt, Func<int, bool> dividerAt) : RecyclerView.LayoutManager
{
    private int _scroll;
    private int _contentHeight;

    public override RecyclerView.LayoutParams GenerateDefaultLayoutParams() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

    public override bool CanScrollVertically() => true;

    public override void OnLayoutChildren(RecyclerView.Recycler? recycler, RecyclerView.State? state)
    {
        if (recycler is null) return;

        DetachAndScrapAttachedViews(recycler);

        _contentHeight = PaddingTop + PaddingBottom;
        if (ItemCount == 0) return;

        int usable = Width - PaddingLeft - PaddingRight;
        int rowHeight = context.Dp(TilesLayout.RowHeightDp);
        int dividerHeight = context.Dp(TilesLayout.DividerRowDp);
        int gap = context.Dp(TilesLayout.GapDp);
        var packer = new TilePacker(TilesLayout.Columns);

        // Проход первый: разложить по клеткам и узнать, какие строки заняты разделителем. Высоту
        // строк складывать по дороге нельзя — место элемента зависело бы от того, докуда дошёл
        // проход, и низ раскладки ехал бы при каждой перестановке.
        var placed = new List<(int Position, int Row, int Column, TileSize Size)>(ItemCount);
        var heights = new List<float>();

        for (int position = 0; position < ItemCount; position++)
        {
            var size = sizeAt(position);
            var (row, column) = packer.Place(size);

            placed.Add((position, row, column, size));

            while (heights.Count < row + size.Rows) heights.Add(rowHeight);

            // Разделитель занимает свою строку целиком и делает её ниже обычной: он и есть тот
            // видимый зазор, ради которого заведён.
            if (dividerAt(position)) heights[row] = dividerHeight;
        }

        var tops = TileRows.Tops(heights);

        foreach (var (position, row, column, size) in placed)
        {
            var view = recycler.GetViewForPosition(position);
            AddView(view);

            // Границы считаются от края сетки, а не сложением ширин колонок: остаток от деления
            // иначе копился бы к правому краю, и последний столбик не дотягивал бы до него.
            int left = PaddingLeft + column * usable / TilesLayout.Columns + gap;
            int right = PaddingLeft + (column + size.Columns) * usable / TilesLayout.Columns - gap;
            int top = PaddingTop + (int)tops[row] + gap - _scroll;
            int bottom = PaddingTop + (int)tops[row + size.Rows] - gap - _scroll;

            view.Measure(
                View.MeasureSpec.MakeMeasureSpec(right - left, MeasureSpecMode.Exactly),
                View.MeasureSpec.MakeMeasureSpec(bottom - top, MeasureSpecMode.Exactly));
            LayoutDecorated(view, left, top, right, bottom);

            _contentHeight = Math.Max(
                _contentHeight, PaddingTop + (int)tops[row + size.Rows] + PaddingBottom);
        }
    }

    /// <summary>
    /// Прокрутка сдвигом уложенных детей: перекладывать их заново незачем — сетка не меняется от
    /// того, что её проматывают.
    /// </summary>
    public override int ScrollVerticallyBy(int dy, RecyclerView.Recycler? recycler, RecyclerView.State? state)
    {
        int limit = Math.Max(0, _contentHeight - Height);
        int scrolled = Math.Clamp(_scroll + dy, 0, limit) - _scroll;

        _scroll += scrolled;
        OffsetChildrenVertical(-scrolled);

        return scrolled;
    }

    public override int ComputeVerticalScrollRange(RecyclerView.State state) => _contentHeight;

    public override int ComputeVerticalScrollOffset(RecyclerView.State state) => _scroll;

    public override int ComputeVerticalScrollExtent(RecyclerView.State state) => Height;

    /// <summary>
    /// Занятость клеток. Строки заводятся по мере надобности: сколько их выйдет, заранее не знает
    /// никто — это зависит от того, как легли предыдущие плитки.
    /// </summary>
    private sealed class TilePacker(int columns)
    {
        private readonly List<bool[]> _rows = [];

        private int _row;
        private int _column;

        /// <summary>
        /// Первое свободное место <b>не раньше предыдущей плитки</b>: поиск начинается там, где
        /// кончилась она, и назад не идёт. Ниже последней занятой строки свободно всегда, поэтому
        /// перебор завершается.
        /// </summary>
        public (int Row, int Column) Place(TileSize size)
        {
            for (int row = _row; ; row++)
            {
                for (int column = row == _row ? _column : 0; column + size.Columns <= columns; column++)
                {
                    if (!Free(row, column, size)) continue;

                    Occupy(row, column, size);
                    (_row, _column) = (row, column + size.Columns);

                    return (row, column);
                }
            }
        }

        private bool Free(int row, int column, TileSize size)
        {
            for (int y = row; y < row + size.Rows && y < _rows.Count; y++)
            {
                for (int x = column; x < column + size.Columns; x++)
                {
                    if (_rows[y][x]) return false;
                }
            }

            return true;
        }

        private void Occupy(int row, int column, TileSize size)
        {
            while (_rows.Count < row + size.Rows) _rows.Add(new bool[columns]);

            for (int y = row; y < row + size.Rows; y++)
            {
                for (int x = column; x < column + size.Columns; x++) _rows[y][x] = true;
            }
        }
    }
}
