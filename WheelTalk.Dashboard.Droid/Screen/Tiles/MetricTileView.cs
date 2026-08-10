using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Metrics;
using WheelTalk.Core.Tiles;

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
    private TileLimits? _limits;
    private int _unitPx = 11;

    public MetricTileView(Context context, DashboardOptions options) : base(context, options)
    {
        _value = new TextView(context) { Gravity = GravityFlags.Center };
        _value.SetTextColor(Palette.Ink);
        _value.SetMaxLines(1);
        _value.SetTypeface(PaintRuler.Mono, Android.Graphics.TypefaceStyle.Normal);

        // Автоподбор платформы снят (план плиток §3): он считает кегль на каждую плитку отдельно, и
        // соседние плитки одного размера читались тем мельче, чем длиннее у величины имя. Кегль
        // теперь один на класс формы и приходит снаружи — Apply.
        _value.SetTextSize(ComplexUnitType.Sp, TilesLayout.ValueMinSp);

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
    public void Bind(MetricDescriptor metric, string label, string unit, TileSize size, bool showLabel,
        TileLimits? limits, TileTypeface face, bool heatBar)
    {
        _metric = metric;
        _limits = limits;
        _format = "F" + metric.Decimals;

        // Единицы на четвертной плитке нет вовсе: 25 px «км/ч» в 61 px содержимого — сорок
        // процентов плитки, а величину называет подпись (план плиток §4).
        _unit = TileTypography.UnitOn(new TileClass(size.Columns, size.Rows), unit);

        // Шкалы жара нет у величины без порогов: одометру и наклону не от чего греться, и
        // объявлять шкалу, на которой никогда ничего не появится, — обман. Тогда рамка обычная,
        // замкнутая, как была до шкалы.
        BindFrame(label, size, showLabel, heatBar && MetricHeat.Limits(metric.Id, Options, limits) is not null);
        Apply(face, size);

        _shown = "";
        Render(null);
    }

    /// <summary>
    /// Кегль и форма приходят снаружи, посчитанные на класс (<see cref="TileTypography"/>): плитка
    /// сама себе размера не выбирает — иначе равные плитки снова читались бы неравно.
    /// </summary>
    private void Apply(TileTypeface face, TileSize size)
    {
        ApplyForm(face.Form, size);

        _value.SetTextSize(ComplexUnitType.Sp, face.ValueSp);
        _value.Gravity = face.Form == TileForm.Row ? GravityFlags.End | GravityFlags.CenterVertical : GravityFlags.Center;
        _unitPx = (int)Math.Round((double)Context!.Dp(face.UnitSp));

        if (_value.LayoutParameters is LinearLayout.LayoutParams layout)
        {
            layout.Width = face.Form == TileForm.Row ? 0 : ViewGroup.LayoutParams.MatchParent;
            layout.Height = face.Form == TileForm.Row ? ViewGroup.LayoutParams.MatchParent : 0;
            layout.Weight = 1f;
            layout.TopMargin = face.Form == TileForm.Row ? 0 : Context.Dp(TilesLayout.ValueTopMarginDp);

            // Поля ужимает только квадрат — ровно на столько, на сколько его расширил подбор
            // кегля (ValueBleedDp). Прямоугольные породы живут со своими полями, как их приняли.
            int bleed = face.Form == TileForm.Square ? -Context.Dp(TilesLayout.ValueBleedDp) : 0;
            layout.LeftMargin = face.Form == TileForm.Row ? Context.Dp(TilesLayout.RowGapDp) : bleed;
            layout.RightMargin = face.Form == TileForm.Row ? 0 : bleed;
            _value.LayoutParameters = layout;
        }
    }

    protected override void ShowContent(bool visible) =>
        _value.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;

    /// <summary>
    /// Очередной снимок. Зовётся на каждом кадре, поэтому текст переставляется только при
    /// изменении: <c>TextView.SetText</c> тянет за собой перекладку строки, а число меняется впятеро
    /// реже, чем идут кадры.
    /// <para>
    /// Подложка перекрашивается там же и по тому же условию: цвет плитки — про то число, которое на
    /// ней написано, и меняться раньше него ему незачем.
    /// </para>
    /// </summary>
    public override void Render(TelemetrySnapshot? snapshot)
    {
        if (_metric is not { } metric) return;

        double? value = MetricNumber.Value(metric, snapshot);
        string text = MetricNumber.Text(value, _format);

        if (_shown == text) return;

        _shown = text;
        _value.TextFormatted = MetricNumber.Compose(text, _unit, Palette.Dim, _unitPx);
        ShowMuted(value is null);
        ShowHeat(MetricHeat.Of(metric.Id, value, Options, _limits));
    }
}
