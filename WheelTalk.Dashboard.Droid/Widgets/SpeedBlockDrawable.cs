using Android.Graphics;

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
    /// </summary>
    private const float ExtrasAt = 0.64f;

    /// <summary>Какую часть ширины занимает цифра скорости; остальное — воздух по краям.</summary>
    private const float SpeedOfWidth = 0.94f;

    /// <summary>Потолок кегля скорости в долях высоты — чтобы на узком экране она не подпирала ленты.</summary>
    private const float SpeedOfHeight = 0.24f;

    /// <summary>Доля высоты, оставленная снизу под подписи лент.</summary>
    private const float BottomMargin = 0.05f;

    private const float ValueOfWidth = 0.2f;
    private const float CaptionOfValue = 0.46f;

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

        // Высота строки считается от оставшегося места, а не от ширины: сверху панели зарезервирована
        // полоса под кнопки, и справочные значения, посчитанные «от себя», уезжали за нижний край
        // прямо на подписи лент.
        float top = rect.Top + rect.Height() * ExtrasAt;
        float room = rect.Bottom - top - rect.Height() * BottomMargin;

        // Четыре пары на выросшем кегле (ExtrasFontScale) визуально требуют в полтора раза больше
        // строки, чем сама строка даёт при делении на четыре — центр всего ~152 dp на эталонном
        // экране, ширина никогда не была узким местом, узкое место высота. Если по факту места
        // (не по значениям телеметрии — «прыгающую разметку не делаем», dashboard-feedback.md)
        // четыре пары не влезают, остаются две: макс. ШИМ и температура нужны на ходу чаще, а
        // поездка и заряд/просадка дублируются лентами по краям (след и подпись под шкалой
        // напряжения) и уходят первыми.
        // Желаемый кегль задаёт ширина; высота строки — потолок, и он последний, а не первый:
        // раньше кегль подгонялся под строку и **потом** умножался на ExtrasFontScale, отчего пара
        // выходила в полтора раза выше своей строки. На панели во весь экран это ещё пряталось в
        // запасе, а в плеере, где панели досталось меньше двух третей высоты, пары полезли друг на
        // друга и на подписи лент (playback-plan.md §0.1).
        float wanted = rect.Width() * ValueOfWidth * ExtrasFontScale;

        int rows = wanted * RowToFont <= room / 4 ? 4 : 2;
        float row = room / rows;
        float value = Math.Min(wanted, row / RowToFont);

        Pair(canvas, rect, top, value, $"{Reading.MaxPwm:F0} %", "макс ШИМ", palette);
        Pair(canvas, rect, top + row, value, $"{Reading.TemperatureC} / {Reading.MaxTemperatureC}", "t° тек / макс", palette);

        if (rows == 4)
        {
            Pair(canvas, rect, top + row * 2, value, $"{Reading.TripKm:F1}", "поездка, км", palette);

            // Минимальное напряжение за поездку, а не просадка (dashboard-feedback.md, прогон 4
            // §3). Просадка — дельта: «3,2» само по себе не читается, потому что зависит от пакета
            // и от того, от чего отсчитано, а рядом стояли проценты заряда и никаких единиц.
            // Минимум — величина с местом на шкале: он в тех же вольтах, что лента напряжения, и
            // совпадает с риской минимума на ней (Tapes: Mark.Value = reading.MinVoltageV). Число
            // под центром и след на ленте становятся одним фактом, а не двумя разными.
            Pair(canvas, rect, top + row * 3, value, $"{Reading.Battery} / {Reading.MinVoltageV:F1}", "заряд % / мин В", palette);
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
