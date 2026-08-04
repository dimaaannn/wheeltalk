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

    private MetricDescriptor? _metric;
    private string _format = "F0";
    private string _shown = "";

    public MetricTileView(Context context, DashboardPalette palette) : base(context)
    {
        Orientation = Android.Widget.Orientation.Vertical;
        int pad = context.Dp(9);
        SetPadding(pad, pad, pad, pad);

        var background = new GradientDrawable();
        background.SetShape(ShapeType.Rectangle);
        background.SetCornerRadius(context.Dp(12));
        // Фон плитки — та же приглушённая краска палитры, взятая почти прозрачной: так плитки видны
        // на фоне панели при любой палитре, и второго набора цветов заводить не пришлось.
        background.SetColor(Color.Argb(28, palette.Dim.R, palette.Dim.G, palette.Dim.B));
        Background = background;

        _label = new TextView(context);
        _label.SetTextSize(ComplexUnitType.Sp, 11);
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
        _value.SetAutoSizeTextTypeUniformWithConfiguration(12, 64, 1, (int)ComplexUnitType.Sp);
        row.AddView(_value, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));

        _unit = new TextView(context);
        _unit.SetTextSize(ComplexUnitType.Sp, 11);
        _unit.SetTextColor(palette.Dim);
        _unit.SetPadding(context.Dp(3), 0, 0, 0);
        row.AddView(_unit);

        // Остаток плитки под числом: подпись сверху, число — во всём, что осталось, и по центру
        // этого остатка. Отсюда и «стоит по центру плитки», и то, во что упирается автоподбор.
        AddView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f)
        {
            TopMargin = context.Dp(2),
        });
    }

    /// <summary>
    /// Чью величину показывать. Слова приходят готовыми, а не ключами: библиотека ресурсов
    /// приложения не видит — тот же порядок, что у подписей шторки и плашки связи.
    /// </summary>
    public void Bind(MetricDescriptor metric, string label, string unit, TileWidth width)
    {
        _metric = metric;
        _format = "F" + metric.Decimals;
        _label.Text = label;
        _unit.Text = unit;
        _unit.Visibility = unit.Length == 0 ? ViewStates.Gone : ViewStates.Visible;

        // Кегль числа не задаётся: он следует из размера плитки, а размер — из её ширины.
        SetRows(width.Rows());

        _shown = "";
        Render(null);
    }

    /// <summary>
    /// Точная высота вместо высоты по содержимому: строка сетки — одна мера на всех
    /// (<see cref="TilesLayout.RowHeightDp"/>), и двухстрочная плитка встаёт ровно на место двух
    /// однострочных вместе с просветом между ними. Считать высоту по тексту нельзя — тогда ряд из
    /// узкой плитки и широкой разъедется, а размер плитки перестанет зависеть только от её ширины.
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
