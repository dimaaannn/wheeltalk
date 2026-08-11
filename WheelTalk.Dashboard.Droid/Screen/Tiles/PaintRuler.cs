using Android.Graphics;
using WheelTalk.Core.Tiles;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Мерилка строк для подбора кегля (<see cref="ITextRuler"/>) — <b>тем же шрифтом, которым плитка
/// потом рисует</b>. Это и есть тот подводный камень, ради которого шов заведён: таблица средних
/// ширин врёт на точке — в моноширинном начертании она шириной с цифру, — и «74.2» оказывается
/// шире, чем считалось, ровно на краю плитки.
/// <para>
/// <b>Каждое обращение к шрифту — это JNI</b>, и стоит оно на три порядка дороже арифметики.
/// Подбор кегля спрашивает мерилку тысячи раз за один пересчёт раскладки (замер 10.08.2026: 846
/// ширин и 2249 высот на восемнадцати плитках), и на телефоне это вылилось в секунду стоя́щего
/// экрана и ANR со 109 % CPU. Поэтому здесь два кэша и ни одного лишнего вызова:
/// </para>
/// <list type="bullet">
///   <item><b>Начертания — поля, а не свойства.</b> Раньше <c>Typeface.Create</c> звался на каждый
///   замер: три тысячи созданий шрифта на пересчёт.</item>
///   <item><b>Ширина строки меряется один раз</b> на постоянном кегле: <c>MeasureText</c> линеен по
///   размеру, и все прочие кегли получаются умножением. Сорок разных строк вместо восьмисот
///   замеров.</item>
///   <item><b>Высота — один замер на кегль</b>: метрики начертания от текста не зависят.</item>
/// </list>
/// </summary>
internal sealed class PaintRuler(float density) : ITextRuler
{
    /// <summary>Начертания те же, что ставит плитка. Созданы один раз: <c>Typeface.Create</c> — не бесплатная справка.</summary>
    public static readonly Typeface Mono = Typeface.Create("monospace", TypefaceStyle.Normal)!;

    public static readonly Typeface Sans = Typeface.Default!;

    /// <summary>Кегль, на котором меряется ширина. Прочие получаются умножением — <c>MeasureText</c> линеен.</summary>
    private const float MeasureAt = 100f;

    private readonly Paint _mono = new(PaintFlags.AntiAlias) { TextSize = MeasureAt, };
    private readonly Paint _sans = new(PaintFlags.AntiAlias) { TextSize = MeasureAt, };

    /// <summary>Ширина строки на <see cref="MeasureAt"/> — по строке и начертанию.</summary>
    private readonly Dictionary<(string Text, bool Mono), float> _widths = [];

    /// <summary>Высота строки по кеглю: от начертания, а не от текста.</summary>
    private readonly Dictionary<float, float> _heights = [];

    private bool _typefacesSet;

    /// <summary>
    /// Высота строки — <b>по метрикам начертания</b>, а не по поправке на глазок: от подъёма до
    /// спуска, то есть ровно столько, сколько займёт <c>TextView</c>. Кегль приходит в sp, ответ —
    /// в пикселях, как и у ширины: единицы у мерилки одни, и перепутать их снаружи больше нечем.
    /// </summary>
    public float Height(float sizeSp)
    {
        if (_heights.TryGetValue(sizeSp, out float known)) return known;

        Prepare();
        _mono.TextSize = sizeSp * density;
        var metrics = _mono.GetFontMetrics()!;
        float height = metrics.Descent - metrics.Ascent;

        // Кегль замера вернуть на место обязана та же рука, что его сдвинула: ширины считаются от
        // MeasureAt, и оставленный чужой размер тихо соврал бы на них всем сразу.
        _mono.TextSize = MeasureAt;
        _heights[sizeSp] = height;
        return height;
    }

    public float Width(string text, float sizeSp, bool mono)
    {
        var key = (text, mono);
        if (!_widths.TryGetValue(key, out float atHundred))
        {
            Prepare();
            atHundred = (mono ? _mono : _sans).MeasureText(text);
            _widths[key] = atHundred;
        }

        return atHundred * sizeSp * density / MeasureAt;
    }

    /// <summary>
    /// Мерилка поверх <b>чужой готовой кисти</b> — той самой, которой сейчас и рисуют. Нужна там,
    /// где текст кладут на канву руками (угловая подпись квадрата): правило посадки живёт в ядре и
    /// меряет через <see cref="ITextRuler"/>, а мерить обязано тем же, чем рисует.
    /// <para>
    /// Кегль здесь приходит уже в пикселях: у канвы своих sp нет — кисть знает только размер, каким
    /// её поставили.
    /// </para>
    /// </summary>
    internal sealed class Ruler(Paint paint) : ITextRuler
    {
        public float Width(string text, float sizePx, bool mono)
        {
            float was = paint.TextSize;
            paint.TextSize = sizePx;
            float width = paint.MeasureText(text);
            paint.TextSize = was;

            return width;
        }

        public float Height(float sizePx)
        {
            float was = paint.TextSize;
            paint.TextSize = sizePx;
            var metrics = paint.GetFontMetrics()!;
            paint.TextSize = was;

            return metrics.Descent - metrics.Ascent;
        }
    }

    private void Prepare()
    {
        if (_typefacesSet) return;

        _mono.SetTypeface(Mono);
        _sans.SetTypeface(Sans);
        _typefacesSet = true;
    }
}
