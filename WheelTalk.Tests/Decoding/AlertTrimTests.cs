using System.Text.RegularExpressions;
using WheelTalk.Core.Decoding;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Замок хвостового пробела в строке тревог (план 11 §5.6). Оригинал склеивает слова с пробелом на
/// конце — <c>"errMosfet "</c>, — и он уезжал в журнал поездки: сравнение с <c>"errMosfet"</c> не
/// сходилось ни у нас, ни у того, кто потом читает CSV.
/// <para>
/// Чинится на <b>нашем</b> шве. Здесь заперты обе половины: порт строит строку тем же способом
/// (пробел на конце слова), а хвост обрезан — по поведению, не по исходнику.
/// </para>
/// <para>
/// Слова тревог обновлены на <c>errMosfet</c>/<c>errGyroscope</c> вслед за планом 35 §9 (было
/// <c>Speed2</c>/<c>Speed1</c>) — сам факт трима это не меняет, только пример.
/// </para>
/// </summary>
public class AlertTrimTests
{
    [Theory]
    [InlineData("errMosfet ", "errMosfet")]
    [InlineData("errMosfet errGyroscope ", "errMosfet errGyroscope")]
    [InlineData("  LowVoltage  ", "LowVoltage")]
    [InlineData("", "")]
    [InlineData("TransportMode", "TransportMode")]
    public void The_alert_line_reaches_the_state_without_its_tail(string decoded, string expected)
    {
        var state = new WheelState(new AppWheelConfig(), TimeProvider.System);

        state.SetAlert(decoded);

        Assert.Equal(expected, state.Alert);
        Assert.Equal(expected, state.ToSnapshot().Alert);
    }

    /// <summary>
    /// Декодер — построчный порт, и правка хвоста в нём была бы расхождением с оригиналом там, где
    /// мы обязаны быть с ним побайтово одинаковы. Пробел в <c>"errMosfet "</c> обязан остаться на
    /// месте: чинит его наша сторона, а не порт.
    /// </summary>
    [Fact]
    public void The_ported_decoder_still_builds_the_line_the_way_the_original_does()
    {
        string source = RepoFiles.Read("WheelTalk.Core/Decoding/GotwayDecoder.cs");

        Assert.Contains("""alertLine.Append("errMosfet ")""", source);
        Assert.DoesNotMatch(new Regex(@"alertLine\.ToString\(\)\.Trim\(\)"), source);
    }
}
