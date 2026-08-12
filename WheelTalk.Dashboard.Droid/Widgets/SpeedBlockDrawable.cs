using Android.Graphics;
using WheelTalk.Core.Dashboard;
using WheelTalk.Dashboard.Droid.Screen.Tiles;

namespace WheelTalk.Dashboard.Droid.Widgets;

/// <summary>
/// Центр экрана целиком: крупная цифра скорости и справочные значения под ней.
/// <para>
/// Размеры не заданы числами: кегль скорости подбирается под ширину, справочные — доли от неё, а
/// высоты — доли от высоты. Экраны отличаются пропорциями, и раскладка, посчитанная под один,
/// на другом либо жмётся, либо оставляет пустоту.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/SpeedBlockDrawable.cs</c>. Закрывает пробел
/// бенча: <c>WheelTalk.Native/Drawing/DashboardView.Centre</c> всегда показывал все четыре пары
/// справочных значений и всегда с десятыми — здесь читаются <see cref="DashboardOptions.HideExtrasAbove"/>
/// и <see cref="DashboardOptions.FormatSpeed"/> (десятые скрываются через
/// <c>Options.HideTenthsAbove</c>) динамически на каждый кадр, а не один раз при сборке. Ни одна
/// константа раскладки (доли ширины/высоты) не изменилась.
/// </para>
/// </summary>
public sealed class SpeedBlockDrawable
{
    /// <summary>Доля высоты, на которой стоит центр цифры скорости — ровно верхняя четверть.</summary>
    private const float SpeedAt = 0.25f;

    /// <summary>
    /// Доля высоты панели над цифрой скорости — всё, что выше, свободно и может быть занято
    /// накладкой (плеер ставит туда время кадра). Считается от потолка кегля, то есть от самой
    /// крупной скорости, какая вообще может встать: полоса, посчитанная по факту, была бы шире, но
    /// съезжала бы от «8,4» к «147».
    /// </summary>
    public static float SpaceAboveSpeed => SpeedAt - SpeedOfHeight * SpeedHalf;

    /// <summary>Половина высоты цифры скорости в долях её кегля — тем же числом рисуется сама цифра.</summary>
    private const float SpeedHalf = 0.6f;

    /// <summary>
    /// Доля высоты, с которой начинаются справочные значения. Окна лент стоят на половине, и между
    /// ними и справочными нужен зазор больше обычного межстрочного: иначе три группы чисел на
    /// тёмном фоне читаются как одна таблица, а они разного веса.
    /// <para>
    /// Открыта наружу: по этой же доле раскладка ловит долгий тап по блоку (<c>CentreExtras</c>) —
    /// зона правки обязана совпадать с картинкой, а не считаться своим числом.
    /// </para>
    /// </summary>
    public const float ExtrasAt = 0.64f;

    /// <summary>Какую часть ширины занимает цифра скорости; остальное — воздух по краям.</summary>
    private const float SpeedOfWidth = 0.94f;

    /// <summary>Потолок кегля скорости в долях высоты — чтобы на узком экране она не подпирала ленты.</summary>
    private const float SpeedOfHeight = 0.24f;

    /// <summary>Доля высоты, оставленная снизу под подписи лент.</summary>
    private const float BottomMargin = 0.05f;

    private const float ValueOfWidth = 0.2f;
    private const float CaptionOfValue = 0.46f;

    /// <summary>Какую долю ширины центра занимает справочная строка; остальное — воздух по краям.</summary>
    private const float ExtrasOfWidth = 0.92f;

    /// <summary>
    /// Пол читаемости справочных, dp. ISO 15008 требует не мельче 12 угловых минут: на вытянутой
    /// руке (700 мм) это <c>2 · 700 · tan(6′) = 2,44 мм</c>, то есть 15,4 dp при 25,4/160 мм на dp —
    /// округлено вверх до 16. Прежние 11 pt давали 6′, вдвое ниже нормы (прогон 3), и потому кегль
    /// растили множителем; теперь норма стоит полом, а не множителем — её не обойти составом.
    /// </summary>
    private const float FloorDp = 16;

    /// <summary>
    /// Во сколько раз кегль справочных пар вырос против прежнего. dashboard-feedback.md
    /// «Решения 29.07.2026» → «Кегль справочных значений по ISO 15008»: 17/11 pt в MAUI-версии
    /// давали 9,3′/6,0′ угловых при минимуме ISO 15008 в 12′ — прежний кегль не дотягивал до
    /// стандарта, не просто читался мельче желаемого. Точная подгонка — по стенду глазами; здесь
    /// только механика и разумная величина.
    /// </summary>
    private const float ExtrasFontScale = 1.5f;

    /// <summary>
    /// Во сколько раз визуальная высота пары (кегль значения + подпись + межстрочный зазор,
    /// см. <see cref="Pair"/>) больше кегля значения. Используется дважды: чтобы получить кегль от
    /// высоты строки (кегль = строка / RowToFont) и чтобы проверить обратное — влезает ли уже
    /// выросший в <see cref="ExtrasFontScale"/> раз кегль обратно в ту же строку.
    /// </summary>
    private const float RowToFont = 1.75f;

    private readonly Paint _bold = new() { AntiAlias = true };
    private readonly Paint _text = new() { AntiAlias = true };

    public SpeedBlockDrawable() => _bold.SetTypeface(Typeface.DefaultBold);

    public required DashboardOptions Options { get; init; }

    public DashboardReading Reading { get; set; } = DashboardReading.Idle;

    /// <summary>Плотность экрана: по ней считается пол читаемости — он в миллиметрах глаза, а не в пикселях.</summary>
    public float Density { get; set; } = 1;

    public void Draw(Canvas canvas, RectF rect)
    {
        var palette = Options.Palette;
        bool extras = Options.HideExtrasAbove <= 0 || Reading.SpeedKmh < Options.HideExtrasAbove;

        string speed = Options.FormatSpeed(Reading.SpeedKmh);
        float size = FitSpeed(speed, rect);
        float centre = rect.Top + rect.Height() * SpeedAt;
        float half = size * SpeedHalf;

        // Скорость сидит в верхней трети, а не в середине: посередине стоят окна обеих лент, и три
        // крупных числа в один ряд читаются как одна строка.
        _bold.Color = palette.Ink;
        _bold.TextSize = size;
        canvas.DrawString(_bold, speed, rect.Left, centre - half, rect.Width(), half * 2, HAlign.Center, VAlign.Center);

        _text.Color = palette.Dim;
        _text.TextSize = size * 0.16f;
        canvas.DrawString(_text, "км/ч", rect.Left, centre + half, rect.Width(), size * 0.24f, HAlign.Center, VAlign.Center);

        if (!extras) return;

        DrawExtras(canvas, rect, palette);
    }

    /// <summary>
    /// Справочный блок — <b>состав, собранный человеком</b> (решение владельца 12.08.2026: «взять
    /// подход табличек»), а не четыре зашитые пары. Величины приходят из каталога, порядок и набор
    /// — из хранилища, кегль подбирается под число строк и место (<see cref="CenterTypography"/>).
    /// <para>
    /// Высота строки считается от оставшегося места, а не от ширины: сверху панели полоса под
    /// кнопки, и справочные значения, посчитанные «от себя», уезжали за нижний край прямо на
    /// подписи лент. Ширина никогда не была узким местом — узкое место высота (центр около 152 dp
    /// на эталонном экране), и потому пол читаемости решает, сколько строк показать.
    /// </para>
    /// <para>
    /// Не влезло — показывается меньше строк, а не мельче: снимаются последние, потому что порядок
    /// собран человеком и первое в нём для него важнее. Прежний код делал то же самое жёстко —
    /// «четыре или две», — только выбор за райдера делали мы.
    /// </para>
    /// </summary>
    private void DrawExtras(Canvas canvas, RectF rect, DashboardPalette palette)
    {
        var rows = Options.CentreRows;
        if (rows.Count == 0) return;

        float top = rect.Top + rect.Height() * ExtrasAt;
        float room = rect.Bottom - top - rect.Height() * BottomMargin;

        // Десятые прячутся на ходу тем же порогом, что и у самой скорости: рябь в углу глаза не
        // читается, а место занимает (HideTenthsAbove, прогон 3).
        bool tenths = Options.HideTenthsAbove <= 0 || Reading.SpeedKmh < Options.HideTenthsAbove;

        var (worstValue, worstCaption) = CenterReadings.Worst(rows, Options.Words);
        var fit = CenterTypography.Fit(
            worstValue,
            worstCaption,
            rows.Count,
            room,
            rect.Width() * ExtrasOfWidth,
            new PaintRuler.Ruler(_text),
            new CenterMetrics(
                FloorPx: Density * FloorDp,
                CeilingPx: rect.Width() * ValueOfWidth * ExtrasFontScale));

        if (fit.Rows == 0) return;

        float row = room / fit.Rows;
        for (int index = 0; index < fit.Rows; index++)
        {
            var line = rows[index];
            string value = string.Join(" / ",
                line.Readings().Select(reading => CenterReadings.Text(reading, Reading, tenths)));

            Pair(canvas, rect, top + row * index, fit.FontPx, value, CenterReadings.Caption(line, Options.Words), palette);
        }
    }

    /// <summary>
    /// Кегль скорости — наибольший, при котором строка помещается в отведённую ширину. Не константа
    /// потому, что «8,4» и «147» разной длины, а место одно и то же; и не доля высоты потому, что
    /// упирается цифра в ширину, а высота лишь ставит потолок.
    /// </summary>
    private float FitSpeed(string text, RectF rect)
    {
        float ceiling = rect.Height() * SpeedOfHeight;
        float available = rect.Width() * SpeedOfWidth;
        _bold.TextSize = ceiling;
        float width = _bold.MeasureText(text);

        return width <= available ? ceiling : ceiling * available / width;
    }

    /// <summary>
    /// Справочные значения рисуются приглушённо. Они не для взгляда на ходу — они для взгляда на
    /// светофоре, и белым спорили бы за внимание с тем, ради чего экран сделан.
    /// </summary>
    private void Pair(Canvas canvas, RectF rect, float top, float font,
        string value, string caption, DashboardPalette palette)
    {
        _text.Color = palette.Dim;
        _text.TextSize = font;
        canvas.DrawString(_text, value, rect.Left, top, rect.Width(), font * 1.15f, HAlign.Center, VAlign.Center);

        _text.Color = WithAlpha(palette.Dim, 0.75f);
        _text.TextSize = font * CaptionOfValue;
        canvas.DrawString(_text, caption, rect.Left, top + font * 1.05f, rect.Width(), font * 0.7f, HAlign.Center, VAlign.Center);
    }

    private static Color WithAlpha(Color color, float alpha) =>
        global::Android.Graphics.Color.Argb((int)Math.Round(alpha * 255), color.R, color.G, color.B);
}
