using WheelTalk.Core.Tiles;

namespace WheelTalk.Core.Dashboard;

/// <summary>
/// Чем набран справочный блок центра: единый кегль на все строки и сколько строк вообще показывать.
/// </summary>
/// <param name="FontPx">Кегль значения. Подпись под ним — доля от него, см. <see cref="CenterMetrics.CaptionScale"/>.</param>
/// <param name="Rows">Сколько строк влезло. Меньше заказанного — остальные не показываются вовсе.</param>
public readonly record struct CenterFit(float FontPx, int Rows);

/// <summary>Что подбор знает о месте и о читаемости. Всё в пикселях этого экрана.</summary>
/// <param name="FloorPx">
/// Пол читаемости — <b>не подвинуть</b>. ISO 15008 требует не мельче 12 угловых минут: на вытянутой
/// руке (700 мм) это 2,44 мм, то есть 15,4 dp, — округлено вверх до 16 dp
/// (<c>2 · 700 · tan(6′) = 2,44 мм</c>; 1 dp = 25,4/160 мм). Прежние 11 pt справочных давали 6′ —
/// вдвое ниже стандарта, и это была не придирка к вкусу, а несоблюдение нормы (прогон 3).
/// </param>
/// <param name="CeilingPx">Потолок: выше него строка спорит с самой скоростью, ради которой экран и сделан.</param>
/// <param name="RowToFont">
/// Во сколько раз строка выше кегля значения: значение, подпись под ним и межстрочный зазор.
/// </param>
/// <param name="CaptionScale">Кегль подписи долей от кегля значения.</param>
public readonly record struct CenterMetrics(
    float FloorPx, float CeilingPx, float RowToFont = 1.75f, float CaptionScale = 0.46f);

/// <summary>
/// Автомасштаб справочного блока (решение владельца 12.08.2026: «два элемента — большие, пять —
/// меньше»). Тот же приём, что у плиток: мерить тем, чем рисуем, худшей строкой, и один кегль на
/// всех — разные кегли в столбце читались бы как разная важность, а важность здесь задаёт порядок.
/// <para>
/// <b>Пол читаемости не уступает месту.</b> Не влезло — показывается меньше строк, а не мельче:
/// нечитаемая строка не показание, а помеха, и лучше честно не показать её вовсе. Снимаются
/// последние — порядок собран человеком, и первые для него важнее.
/// </para>
/// </summary>
public static class CenterTypography
{
    /// <param name="worstValue">Худшая строка значения: самая широкая из тех, что вообще могут встать.</param>
    /// <param name="worstCaption">
    /// Худшая подпись. Меряется своим кеглем (<see cref="CenterMetrics.CaptionScale"/>), а не
    /// кеглем значения: подпись мельче, и считать её наравне значило бы зря ужимать само число —
    /// «Темп. тек / макс» длиннее любого показания, но занимает вдвое меньше.
    /// </param>
    /// <param name="rows">Сколько строк заказано — столько, сколько собрал человек.</param>
    /// <param name="roomPx">Высота, отданная блоку.</param>
    /// <param name="widthPx">Ширина, отданная строке.</param>
    public static CenterFit Fit(
        string worstValue, string worstCaption, int rows, float roomPx, float widthPx,
        ITextRuler ruler, CenterMetrics metrics)
    {
        if (rows <= 0 || roomPx <= 0 || widthPx <= 0) return new CenterFit(metrics.FloorPx, 0);

        // Ширина линейна по кеглю (PaintRuler мерит так же), поэтому меряем один раз на потолке и
        // пересчитываем — вместо перебора кеглей, как у плиток: строк здесь единицы, а не двадцать.
        float byWidth = MathF.Min(
            Widest(worstValue, 1, widthPx, ruler, metrics),
            Widest(worstCaption, metrics.CaptionScale, widthPx, ruler, metrics));

        for (int shown = Math.Min(rows, CenterLayout.MaxRows); shown >= 1; shown--)
        {
            float byHeight = roomPx / shown / metrics.RowToFont;
            float font = MathF.Min(MathF.Min(byHeight, byWidth), metrics.CeilingPx);

            if (font >= metrics.FloorPx) return new CenterFit(font, shown);
        }

        // Даже одна строка не встаёт по полу читаемости — значит блоку места не дали вовсе
        // (плеер, узкая полоса). Ноль строк честнее нечитаемой одной.
        return new CenterFit(metrics.FloorPx, 0);
    }

    /// <summary>Наибольший кегль значения, при котором эта строка своим кеглем влезает в ширину.</summary>
    private static float Widest(
        string text, float scale, float widthPx, ITextRuler ruler, CenterMetrics metrics)
    {
        if (text.Length == 0 || scale <= 0) return metrics.CeilingPx;

        float atCeiling = ruler.Width(text, metrics.CeilingPx * scale, mono: false);

        return atCeiling > 0 ? metrics.CeilingPx * widthPx / atCeiling : metrics.CeilingPx;
    }
}
