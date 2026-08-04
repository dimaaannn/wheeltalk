using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Metrics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Плитка вида <see cref="TileKind.Value"/>: подпись и текущее число с единицей. Величина приходит
/// описанием (<see cref="MetricDescriptor"/>), поэтому плитка одна на все двадцать шесть величин —
/// новая не добавляет сюда ни строки.
/// <para>
/// <b>Молчащая величина рисует прочерк, а не ноль</b> (план 23 §3.1): <c>null</c> из
/// <see cref="MetricDescriptor.Read"/> значит «этого колесо не сообщает», и ноль на его месте был бы
/// показанием, которого не было.
/// </para>
/// </summary>
internal sealed class MetricTileView : TileView
{
    private readonly TextView _value;

    private MetricDescriptor? _metric;
    private string _format = "F0";
    private string _unit = "";
    private string _shown = "";

    public MetricTileView(Context context, DashboardPalette palette) : base(context, palette)
    {
        _value = new TextView(context) { Gravity = GravityFlags.Center };
        _value.SetTextColor(palette.Ink);
        _value.SetMaxLines(1);
        // Кегль подбирает платформа (API 26+): показание занимает плитку целиком — в узкой
        // однострочной и в широкой двухстрочной оно одно и то же, но разного размера. Своего расчёта
        // здесь быть не должно: встроенный меряет тем же кодом, которым потом и рисует.
        //
        // Ширина и высота у него не WrapContent намеренно — при них автоподбору не от чего
        // отталкиваться, и он не работает вовсе (документация Android, «Autosizing TextView»).
        _value.SetAutoSizeTextTypeUniformWithConfiguration(
            TilesLayout.ValueMinSp, TilesLayout.ValueMaxSp, TilesLayout.ValueStepSp, (int)ComplexUnitType.Sp);

        // Остаток плитки под показанием: подпись сверху, число — во всём, что осталось, и по центру
        // этого остатка. Отсюда и «стоит по центру плитки», и то, во что упирается автоподбор.
        AddView(_value, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f)
        {
            TopMargin = context.Dp(TilesLayout.ValueTopMarginDp),
        });
    }

    /// <summary>
    /// Чью величину показывать. Слова приходят готовыми, а не ключами: библиотека ресурсов
    /// приложения не видит — тот же порядок, что у подписей шторки и плашки связи.
    /// </summary>
    public void Bind(MetricDescriptor metric, string label, string unit, TileSize size, bool showLabel)
    {
        _metric = metric;
        _format = "F" + metric.Decimals;
        _unit = unit;

        BindFrame(label, size, showLabel);

        _shown = "";
        Render(null);
    }

    protected override void ShowContent(bool visible) =>
        _value.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;

    /// <summary>
    /// Очередной снимок. Зовётся на каждом кадре, поэтому текст переставляется только при
    /// изменении: <c>TextView.SetText</c> тянет за собой перекладку строки, а число меняется впятеро
    /// реже, чем идут кадры.
    /// </summary>
    public override void Render(TelemetrySnapshot? snapshot)
    {
        if (_metric is not { } metric) return;

        string text = MetricNumber.Text(metric, snapshot, _format);

        if (_shown == text) return;

        _shown = text;
        _value.TextFormatted = MetricNumber.Compose(text, _unit, Palette.Dim);
    }
}
