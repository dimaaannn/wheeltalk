using WheelTalk.Core.Tiles;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Неподвижная запятая (решение владельца 11.08.2026): короткое показание не двигает цифры, уже
/// стоящие на плитке.
/// <para>
/// <b>Проверяется мерилкой, а не длиной строки.</b> Равная длина — это лишь способ; обещано же
/// человеку другое: запятая стоит на одном и том же месте экрана. Поэтому здесь считается
/// <b>координата запятой</b> в том же центрировании и тем же моноширинным счётом, каким плитка
/// рисует, — сломай кто-нибудь способ, и тест назовёт настоящую беду, а не расхождение с образцом.
/// </para>
/// </summary>
public class NumberBoxTests
{
    /// <summary>Ширина знака в моноширинном — та же доля кегля, что и в соседних проверках подбора.</summary>
    private sealed class Ruler : ITextRuler
    {
        public float Width(string text, float sizeSp, bool mono) => text.Length * sizeSp * (mono ? 0.6f : 0.5f);

        public float Height(float sizeSp) => sizeSp * 1.25f;
    }

    private const float Size = 40;

    private const float Box = 400;

    /// <summary>
    /// Где на плитке стоит запятая: строка центруется в боксе плитки, поэтому её место — это левый
    /// край строки плюс ширина всего, что до запятой.
    /// </summary>
    private static float PointX(string text)
    {
        var ruler = new Ruler();
        int point = text.IndexOfAny([',', '.']);
        float line = ruler.Width(text, Size, mono: true);

        return ((Box - line) / 2) + ruler.Width(text[..point], Size, mono: true);
    }

    /// <summary>
    /// То, на что смотрел владелец: «10,2» сменилось на «9,8» — и цифры не поехали. Худшая строка
    /// уже была шире, значит бокс её и держит.
    /// </summary>
    [Fact]
    public void A_shorter_value_does_not_move_the_point()
    {
        // Худшая увиденная строка — «888.8»: четыре знака до запятой не показывались, но три и
        // точка с десятыми — да.
        const int width = 6;

        Assert.Equal(PointX(NumberBox.Fit("10.2", width)), PointX(NumberBox.Fit("9.8", width)), 3);
        Assert.Equal(PointX(NumberBox.Fit("100.2", width)), PointX(NumberBox.Fit("9.8", width)), 3);
    }

    /// <summary>
    /// Минус — такой же житель строки, как разряд: на рекуперации ток уходит в минус и возвращается
    /// обратно по нескольку раз за спуск. Ширину он держит наравне с цифрой, и запятая от него не
    /// двигается.
    /// </summary>
    [Fact]
    public void A_minus_sign_does_not_move_the_point()
    {
        const int width = 6;

        Assert.Equal(PointX(NumberBox.Fit("-12.5", width)), PointX(NumberBox.Fit("12.5", width)), 3);
        Assert.Equal(PointX(NumberBox.Fit("-1.5", width)), PointX(NumberBox.Fit("12.5", width)), 3);
    }

    /// <summary>
    /// Бокс подпирает слева, а не справа: хвостовые пробелы разметка при центровании отбрасывает, и
    /// подпорка справа была бы мнимой. Заодно это и есть то, чем держится запятая, — хвост у
    /// показания фиксирован форматом.
    /// </summary>
    [Fact]
    public void The_box_props_the_number_from_the_left()
    {
        Assert.Equal("   9.8", NumberBox.Fit("9.8", 6));
        Assert.Equal("9.8", NumberBox.Fit("9.8", 3));
    }

    /// <summary>
    /// Строка шире бокса остаётся как есть: показание не режут ради ровного края — бокс дорастёт
    /// сам, увидев её.
    /// </summary>
    [Fact]
    public void A_value_wider_than_the_box_stays_whole()
    {
        Assert.Equal("88888.8", NumberBox.Fit("88888.8", 4));
    }

    /// <summary>
    /// Прочерк не число: равнять в нём нечего, а прижатый вправо он читался бы как обрубок. Молчащая
    /// величина стоит по середине плитки, как стояла.
    /// </summary>
    [Fact]
    public void A_dash_is_left_where_it_was()
    {
        Assert.Equal("—", NumberBox.Fit("—", 6));
    }
}
