using Android.Content;
using Android.Graphics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Кромки <b>рисунка</b> строки относительно базовой линии — там, где ложится краска, а не там, куда
/// достаёт кегль.
/// </summary>
/// <param name="Top">Верх краски. Отрицательный: рисунок лежит над базовой линией.</param>
/// <param name="Bottom">Низ краски. У капса с выносом («Щ») он ниже базовой линии.</param>
/// <param name="Left">Левый вынос первого знака — пустая полоска между началом строки и краской.</param>
internal readonly record struct GlyphInk(float Top, float Bottom, float Left);

/// <summary>
/// Стиль подписи плитки — <b>один на все формы и одна техника на всех</b> (слова владельца
/// 11.08.2026: «единообразие вводили для быстрых правок, а не костылей»). Подпись рисует канва, где
/// бы она ни стояла: и метка в углу квадрата, и строка над числом, и слово сбоку в «строке». Отсюда
/// же берутся регистр, отступ и то место, которое подпись забирает у числа.
/// <para>
/// <b>Почему одна техника.</b> Пока квадрат рисовал канвой, а прочие формы держали <c>TextView</c> в
/// разметке, всякая правка стиля шла дважды и всякий раз по-разному: у вида свои поля шрифта, свой
/// клип по полю группы (<c>clipToPadding</c> срезал прижатой подписи верх букв) и свой норов в
/// каждой форме. Канва не режет, не добавляет полей и слушается кромки.
/// </para>
/// <para>
/// <b>Мера — рисунок, а не кегль.</b> У глифов свои внутренние поля: ни знак ▲, ни капс не заполняют
/// кегль доверху, и отступ, отмеренный от номинала, оставляет над видимой кромкой пустоту сверх
/// заданной. Ровно поэтому сдвиг с 8 dp на 6 глазом не увиделся: двигали кегль, а смотрят на краску.
/// Кромки снимаются <c>Paint.GetTextBounds</c>, и видимый зазор «линия рамки → краска» выходит
/// <c>CornerInsetDp − HeatStrokeDp</c> при любом шрифте.
/// </para>
/// <para>
/// <b>JNI — не в кадре</b> (уроки плана 31): всякий замер уходит за шов, поэтому считается он при
/// привязке плитки и смене её размера, а снятое ложится в кэш по кеглю и строке. Кэш и кисть-мерилка
/// общие: и привязка, и отрисовка идут с главного потока.
/// </para>
/// </summary>
internal static class TileLabelStyle
{
    /// <summary>Образец, по которому меряется строка: капс во всю высоту («Ш») и с выносом вниз («Щ»).</summary>
    private const string Sample = "ШЩ";

    /// <summary>Кисть-мерилка: то же начертание, каким набраны подписи, — обычное, не моноширинное.</summary>
    private static readonly Paint Brush = new(PaintFlags.AntiAlias);

    private static readonly Rect Box = new();

    /// <summary>Снятое по кеглю и строке: кромки от прочего текста не зависят, и мерить их дважды незачем.</summary>
    private static readonly Dictionary<(float Size, string Text), GlyphInk> Known = [];

    /// <summary>
    /// Подпись набирается <b>заглавными</b> (слова владельца 11.08.2026): она называет плитку, а не
    /// читается наравне с числом, и капс отличает её от показания вернее всякого кегля. Ключи
    /// ресурсов при этом остаются как были — заглавные это способ показа, а не второе имя величины.
    /// </summary>
    public static string Caps(string label) => label.ToUpperInvariant();

    /// <summary>
    /// Кромки рисунка этой строки <b>этой же кистью</b> — там, где строку кладут на канву и знают
    /// её заранее.
    /// </summary>
    public static GlyphInk InkOf(Paint paint, string text)
    {
        paint.GetTextBounds(text, 0, text.Length, Box);

        return new GlyphInk(Box.Top, Box.Bottom, Box.Left);
    }

    /// <summary>Где стоит видимая кромка подписи — один отступ от края плитки у всех форм.</summary>
    public static int InsetPx(Context context) => context.Dp(TilesLayout.CornerInsetDp);

    /// <summary>Базовая линия, при которой верх краски встаёт ровно на этот отступ.</summary>
    public static float BaselineFor(Context context, float inkTop) => InsetPx(context) - inkTop;

    /// <summary>Левый край строки, при котором её первая краска встаёт на тот же отступ.</summary>
    public static float LeftFor(Context context, float inkLeft) => InsetPx(context) - inkLeft;

    /// <summary>
    /// Место, которое подпись забирает у числа сверху, — <b>одна формула на все формы</b>: от верха
    /// содержимого (край плитки без общего поля) до низа краски. Общее поле вычитается потому, что
    /// подпись сидит выше него, на своём малом отступе, а число живёт внутри полей.
    /// <para>
    /// Строка меряется по худшему её жителю: капс во всю высоту и знак ▲ своим крупным кеглем —
    /// под него полоска и строится, иначе у крайних знак воткнут в чужую разметку. Освободившееся
    /// достаётся числу само: этим же числом идёт бюджет подбора кегля (<c>TileMetrics.SquareLabelPx</c>
    /// и <c>LabelHeightPx</c>), а не одна разметка.
    /// </para>
    /// </summary>
    /// <param name="labelDp">Кегль подписи этой формы: у «строки» он свой, крупнее.</param>
    public static int StripPx(Context context, float labelDp)
    {
        float word = context.Dp(labelDp);
        var caps = Ink(word, Sample);
        var mark = Ink(word * TilesLayout.MarkScale, TileView.MarkHighest);

        float inkTop = MathF.Min(caps.Top, mark.Top);
        float inkBottom = MathF.Max(caps.Bottom, mark.Bottom);

        return (int)MathF.Round(InsetPx(context) + (inkBottom - inkTop) - context.Dp(TilesLayout.PaddingDp));
    }

    private static GlyphInk Ink(float sizePx, string text)
    {
        if (Known.TryGetValue((sizePx, text), out var known)) return known;

        Brush.TextSize = sizePx;
        var ink = InkOf(Brush, text);

        Known[(sizePx, text)] = ink;
        return ink;
    }
}
