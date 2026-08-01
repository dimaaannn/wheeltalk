using Android.Graphics;
using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Dashboard.Droid.Widgets.Tape;

/// <summary>
/// Окно текущего значения — неподвижная рамка, мимо которой едет шкала. Белая цифра в рамке на
/// чёрном с небольшими отступами, поверх шкалы: проверено на стенде, читается лучше всего.
/// <para>
/// Состояние набирается ступенями, и каждая добавляет сигнал, не отменяя предыдущий: в жёлтой зоне
/// желтеют рамка и цифры, в красной под ними появляется красный фон, за критическим порогом этот
/// фон начинает мигать. Так по одному взгляду видно не только «плохо», но и насколько.
/// </para>
/// <para>
/// Мигание нарочно не в такт полосам тревоги: полосы говорят «опасно вообще», окно — «вот этот
/// прибор дошёл до предела», и совпадающий ритм слил бы два сообщения в одно.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/Tape/TapeWindowPart.cs</c>: закрывает главный
/// пробел бенча — <c>WheelTalk.Native/Drawing/TapeRenderer.Window</c> рисовал только статичную
/// заливку, без ступени <see cref="Critical"/> (мигающий фон на пределе). Здесь она перенесена
/// как в MAUI: тот же период мигания (<see cref="BlinkPeriod"/>, 200 мс) и то же условие через
/// <c>Environment.TickCount64</c> — часы .NET, а не Android, чтобы формула совпадала с
/// MAUI-исходником буквально, не только по смыслу.
/// </para>
/// </summary>
public sealed class TapeWindowPart
{
    /// <summary>Доля ширины окна на кегль. «105» тремя знаками занимает как раз около неё.</summary>
    private const float FontOfWidth = 0.52f;

    /// <summary>Высота окна в кеглях: цифра плюс поля сверху и снизу.</summary>
    private const float HeightOfFont = 1.35f;

    private const float Gap = 4;
    private const float Padding = 8;

    /// <summary>
    /// Длительность одной фазы мигания на пределе, мс. Полный цикл — вдвое больше (см. условие в
    /// <see cref="Draw"/>), то есть 400 мс = 2,5 вспышки в секунду: с запасом ниже потолка
    /// WCAG 2.3.1 (не больше трёх в секунду, период не короче 333 мс) — см. dashboard-feedback.md
    /// «Решения 29.07.2026» → «Мигание и WCAG 2.3.1». Прежний комментарий здесь называл частоту
    /// «вдвое быстрее полос тревоги»: при дефолтном <c>DashboardOptions.BlinkHz</c> = 3 (период
    /// 333 мс) вдвое быстрее означало бы 6 Гц и нарушало бы тот же порог, так что число никогда не
    /// было буквально привязано к BlinkHz — сигналы просто идут не в такт (см. ниже).
    /// </summary>
    private const long BlinkPeriod = 200;

    private readonly Paint _fill = new() { AntiAlias = true };
    private readonly Paint _stroke = new() { AntiAlias = true, StrokeWidth = 1 };
    private readonly Paint _bold = new() { AntiAlias = true };
    private readonly RectF _rounded = new();

    public TapeWindowPart()
    {
        _stroke.SetStyle(Paint.Style.Stroke);
        _bold.SetTypeface(Typeface.DefaultBold);
    }

    public string Format { get; set; } = "F0";

    /// <summary>
    /// Своё представление значения, если формата мало. Нужно напряжению: на трёхзначном паке в
    /// окне остаются две последние цифры и десятая, а сотни видно по шкале — «147,5» пятью знаками
    /// ужимает кегль сильнее, чем стоит того старший разряд, который и так не меняется.
    /// </summary>
    public Func<double, string>? Text { get; set; }

    /// <summary>Потолок кегля. Ниже него окно подстраивается под ширину ленты само.</summary>
    public float MaxFontSize { get; set; } = 56;

    public Color Fill { get; set; } = global::Android.Graphics.Color.Black;
    public Color Ink { get; set; } = global::Android.Graphics.Color.White;
    public Color Border { get; set; } = global::Android.Graphics.Color.White;

    /// <summary>Прибор дошёл до предела: заливка мигает.</summary>
    public bool Critical { get; set; }

    /// <summary>
    /// Сырое значение. Оно не то же, что положение разметки: ход ленты сглажен, а цифра —
    /// нет. У формы задача показать темп, у числа — показать, сколько сейчас.
    /// </summary>
    public double Value { get; set; }

    public void Draw(Canvas canvas, in TapeGeometry geometry, DashboardPalette palette, float density)
    {
        var rect = geometry.LabelArea;
        float gap = Gap * density;
        float padding = Padding * density;

        // Кегль и высота окна считаются от ширины ленты, а не задаются числом: экраны отличаются
        // пропорциями, и окно, подогнанное под один, на другом либо жмётся, либо теряет место.
        // Высота при этом берётся от базового кегля, а не от подогнанного под строку, — иначе
        // рамка прыгала бы при каждой смене числа знаков.
        float baseFont = Math.Min(MaxFontSize * density, rect.Width() * FontOfWidth);
        float height = baseFont * HeightOfFont;
        float top = geometry.WindowY - height / 2;

        // Зазор фоном вокруг окна. На пределе окно заливается цветом опасности, и полоса за ним в
        // этот момент того же цвета: два объекта с контрастом 1,00 сливаются в одно пятно ровно
        // тогда, когда на них смотрят. Обводки в две точки для этого мало.
        _fill.Color = palette.Background;
        _rounded.Set(rect.Left - gap, top - gap, rect.Right + gap, top + height + gap);
        canvas.DrawRoundRect(_rounded, 6 * density, 6 * density, _fill);

        bool dark = Critical && Environment.TickCount64 % (BlinkPeriod * 2) < BlinkPeriod;
        _fill.Color = dark ? palette.Background : Fill;
        _rounded.Set(rect.Left, top, rect.Right, top + height);
        canvas.DrawRoundRect(_rounded, 4 * density, 4 * density, _fill);

        _stroke.Color = Border;
        _stroke.StrokeWidth = 3 * density;
        canvas.DrawRoundRect(_rounded, 4 * density, 4 * density, _stroke);

        string text = Text is null ? Value.ToString(Format) : Text(Value);

        _bold.Color = Ink;
        _bold.TextSize = Fit(text, rect.Width() - padding, baseFont);
        canvas.DrawString(_bold, text, rect.Left, top, rect.Width(), height, HAlign.Center, VAlign.Center);
    }

    /// <summary>
    /// Кегль, при котором строка помещается в окно. Трёхзначный ШИМ и напряжение вида «147,5» шире
    /// обычного, и упереться в рамку они не должны — но и ужимать всё до размера самого длинного
    /// случая незачем: тот бывает считанные секунды за поездку.
    /// </summary>
    private float Fit(string text, float available, float font)
    {
        _bold.TextSize = font;
        float width = _bold.MeasureText(text);
        return width <= available ? font : font * available / width;
    }
}
