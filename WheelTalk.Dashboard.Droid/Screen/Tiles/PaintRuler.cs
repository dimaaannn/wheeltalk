using Android.Graphics;
using WheelTalk.Core.Tiles;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Мерилка строк для подбора кегля (<see cref="ITextRuler"/>) — <b>тем же шрифтом, которым плитка
/// потом рисует</b>. Это и есть тот подводный камень, ради которого шов заведён: таблица средних
/// ширин врёт на точке — в моноширинном начертании она шириной с цифру, — и «74.2» оказывается
/// шире, чем считалось, ровно на краю плитки.
/// <para>
/// Живёт один на экран: <see cref="Paint"/> дорог не измерением, а созданием, а классов формы на
/// экране немного и меряются они разом при сборке.
/// </para>
/// </summary>
internal sealed class PaintRuler(float density) : ITextRuler
{
    /// <summary>Число — моноширинным: в нём цифры не пляшут по ширине, и строка не дёргается при смене показания.</summary>
    private readonly Paint _mono = new(PaintFlags.AntiAlias) { TextSize = 100 };

    private readonly Paint _sans = new(PaintFlags.AntiAlias) { TextSize = 100 };

    /// <summary>Начертания те же, что ставит плитка. Одно место на оба: разойтись им нельзя.</summary>
    public static Typeface Mono => Typeface.Create("monospace", TypefaceStyle.Normal)!;

    public static Typeface Sans => Typeface.Default!;

    public float Width(string text, float sizeSp, bool mono)
    {
        var paint = mono ? _mono : _sans;
        paint.SetTypeface(mono ? Mono : Sans);

        // Меряем на постоянном кегле и масштабируем: MeasureText линеен по TextSize, и так замер не
        // зависит от того, сколько раз его позвали и с какой стороны.
        paint.TextSize = 100;
        return paint.MeasureText(text) * sizeSp * density / 100f;
    }
}
