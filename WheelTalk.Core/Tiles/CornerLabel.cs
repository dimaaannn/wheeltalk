namespace WheelTalk.Core.Tiles;

/// <summary>Как подпись села в угол квадратной плитки: каким кеглем набрать слово и что от него осталось.</summary>
/// <param name="WordSp">Кегль слова. Может оказаться мельче заказанного — место дороже ровности.</param>
/// <param name="Word">Само слово, при нужде укороченное многоточием.</param>
public readonly record struct CornerFit(float WordSp, string Word);

/// <summary>
/// Посадка угловой подписи квадратной плитки: «▲ Напряжение» обязано влезть в свою полоску при
/// любой длине слова (регресс, пойманный владельцем 11.08.2026 — слово срезало краем плитки без
/// многоточия, потому что канва сама ничего не ужимает и не обрезает).
/// <para>
/// <b>Три ступени, и порядок в них не случаен.</b> Сперва слово набирается как заказано; не влезло
/// — <b>ужимается кегль слова</b> до пола читаемости (то же правило, каким живёт единица при числе:
/// у пола платит не читаемость, а ровность); всё ещё не влезло — слово честно обрезается
/// многоточием. Обрезка последней, потому что укоротить слово значит отнять смысл, а уменьшить —
/// только вес.
/// </para>
/// <para>
/// <b>Знак не ужимается никогда.</b> Он смысловой: ▲ и ▼ отличают крайнее значение от обычной
/// плитки, и мельчая, он перестаёт делать единственную свою работу — узнаваться с одного взгляда.
/// Место под него вычитается из полоски до всякого счёта.
/// </para>
/// </summary>
public static class CornerLabel
{
    /// <summary>Многоточие — одним знаком, а не тремя точками: три отняли бы у слова три места.</summary>
    public const string Ellipsis = "…";

    /// <param name="word">Слово подписи — уже короткое, если у величины есть короткое имя.</param>
    /// <param name="room">Ширина, оставшаяся слову: полоска подписи без знака и просвета за ним.</param>
    /// <param name="wordSp">Заказанный кегль слова.</param>
    /// <param name="minSp">Пол читаемости: мельче слово не набирают даже ради того, чтобы влезло.</param>
    public static CornerFit Fit(string word, float room, float wordSp, float minSp, ITextRuler ruler)
    {
        if (word.Length == 0 || room <= 0) return new CornerFit(wordSp, "");

        for (float size = wordSp; size > minSp; size--)
        {
            if (ruler.Width(word, size, mono: false) <= room) return new CornerFit(size, word);
        }

        return new CornerFit(minSp, Cut(word, room, minSp, ruler));
    }

    /// <summary>
    /// Слово, укороченное до места, с многоточием на конце. Пустая строка — не влезает даже
    /// многоточие: тогда честнее не рисовать ничего, чем печатать огрызок в один знак.
    /// </summary>
    private static string Cut(string word, float room, float sizeSp, ITextRuler ruler)
    {
        for (int kept = word.Length - 1; kept > 0; kept--)
        {
            string cut = word[..kept] + Ellipsis;
            if (ruler.Width(cut, sizeSp, mono: false) <= room) return cut;
        }

        return ruler.Width(Ellipsis, sizeSp, mono: false) <= room ? Ellipsis : "";
    }
}
