using Android.Graphics;
using Android.Text;
using Android.Text.Style;
using Java.Lang;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Metrics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Число величины словами экрана: прочерк вместо нуля у молчащей (план 23 §3.1) и единица вплотную
/// к числу, а не у дальнего края плитки (решение владельца 04.08.2026).
/// <para>
/// Единица живёт в той же строке, что и число, потому что <b>читаются они вместе</b>: «77 В» — это
/// одно показание, а не число и подпись к нему. Отдельным <c>TextView</c> её прижать к числу
/// нельзя — число тянется автоподбором кегля на всю ширину, и единица уезжает вправо.
/// </para>
/// </summary>
internal static class MetricNumber
{
    public const string NoValue = "—";

    /// <summary>
    /// Само значение либо <c>null</c> — «колесо молчит». Отдельно от <see cref="Text"/> потому, что
    /// по числу плитка не только пишет строку, но и красит подложку (<see cref="MetricHeat"/>), а
    /// разбирать обратно уже округлённую строку значило бы считать одно и то же дважды.
    /// </summary>
    public static double? Value(MetricDescriptor metric, TelemetrySnapshot? snapshot) =>
        snapshot is null ? null : metric.Read(snapshot);

    /// <summary>Сколько разрядов до точки закладывать квадратной плитке, пока величина не показала больше.</summary>
    public const int SeedDigits = 3;

    /// <summary>
    /// Разряды прямоугольной породы — пять, как их приняли: одометр (99999 км) и знак минуса живут
    /// в них же. Число от этого мельче, чем могло бы, и это <b>принятый вид</b>, а не недосмотр:
    /// попытка сделать его крупнее «заодно» вылезла за низ плитки (стенд 10.08.2026).
    /// </summary>
    public const int RectangleDigits = 5;

    /// <summary>
    /// Строка, по ширине которой подбирается кегль класса, — <b>не живое показание</b>: иначе «9.9»
    /// сменилось бы на «10.0», и весь класс перерисовался бы мельче прямо на ходу.
    /// <para>
    /// Разрядов до точки — <paramref name="digits"/>, и это <b>не потолок величины, а увиденное</b>.
    /// Пять разрядов «на всякий случай» стоили четвертной плитке двух третей кегля: температура
    /// «41» набиралась так, будто на ней стоит «88888.8». Ширина растёт вслед за показанием
    /// (<see cref="Digits"/>) и никогда не падает — значит и кегль не скачет вверх-вниз, а один раз
    /// садится под то, что колесо действительно показывает.
    /// </para>
    /// </summary>
    /// <param name="decimals">
    /// Знаков после запятой у <b>этой плитки</b>, а не у величины: округление задаётся плиткой
    /// (<see cref="MetricRounding"/>), и мерить надо ту строку, которая попадёт на экран, — иначе
    /// плитка, которой велели показывать целые, села бы под ширину сотых.
    /// </param>
    public static string Widest(int decimals, int digits)
    {
        string whole = new('8', System.Math.Max(SeedDigits, digits));
        return decimals > 0 ? whole + "." + new string('8', decimals) : whole;
    }

    /// <summary>
    /// Сколько разрядов до точки в этом показании. Знак минуса считается разрядом: он занимает
    /// столько же, сколько цифра, — начертание моноширинное.
    /// </summary>
    public static int Digits(double? value)
    {
        if (value is not { } number) return 0;

        int digits = (int)System.Math.Floor(System.Math.Log10(System.Math.Max(1, System.Math.Abs(number)))) + 1;
        return number < 0 ? digits + 1 : digits;
    }

    /// <summary>Текст показания без единицы — им сравнивают, менялось ли значение.</summary>

    /// <summary>Текст показания без единицы — им сравнивают, менялось ли значение.</summary>
    public static string Text(double? value, string format) =>
        value is { } number ? number.ToString(format) : NoValue;

    /// <summary>
    /// Показание с единицей: единица мельче и приглушена, но стоит вплотную. Кегль ей задаётся долей
    /// от кегля числа (<see cref="TilesLayout.UnitScale"/>), а не своим размером, — иначе при
    /// автоподборе она то тонула бы в числе, то спорила с ним.
    /// </summary>
    /// <param name="unitPx">
    /// Кегль единицы в пикселях — <b>абсолютный, а не доля</b> (план плиток §4). У доли нет пола, а
    /// пол в 11 sp есть: подбор кегля считает ширину строки по той единице, которая попадёт на
    /// экран, и доля разошлась бы с этим расчётом ровно на величину пола.
    /// </param>
    public static ICharSequence Compose(string text, string unit, Color unitColor, int unitPx)
    {
        if (unit.Length == 0) return new Java.Lang.String(text);

        // Тонкий пробел, а не обычный: «77 В» с обычным читается как два слова, без пробела вовсе —
        // сливается в «77В».
        var line = new SpannableString(text + " " + unit);
        int from = text.Length;
        int to = line.Length();

        line.SetSpan(new AbsoluteSizeSpan(unitPx), from, to, SpanTypes.InclusiveExclusive);
        line.SetSpan(new ForegroundColorSpan(unitColor), from, to, SpanTypes.InclusiveExclusive);

        return line;
    }
}
