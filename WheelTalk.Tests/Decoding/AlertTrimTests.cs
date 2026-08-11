using System.Text.RegularExpressions;
using WheelTalk.Core.Decoding;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Замок хвостового пробела в строке тревог (план 11 §5.6). Оригинал склеивает слова с пробелом на
/// конце — <c>"Speed2 "</c>, — и он уезжал в журнал поездки: сравнение с <c>"Speed2"</c> не сходилось
/// ни у нас, ни у того, кто потом читает CSV.
/// <para>
/// Чинится на <b>нашем</b> шве. Здесь заперты обе половины: порт остался нетронутым (по исходнику),
/// а хвост обрезан (по поведению).
/// </para>
/// </summary>
public class AlertTrimTests
{
    [Theory]
    [InlineData("Speed2 ", "Speed2")]
    [InlineData("Speed2 Speed1 ", "Speed2 Speed1")]
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
    /// мы обязаны быть с ним побайтово одинаковы. Пробел в <c>"Speed2 "</c> обязан остаться на
    /// месте: чинит его наша сторона, а не порт.
    /// </summary>
    [Fact]
    public void The_ported_decoder_still_builds_the_line_the_way_the_original_does()
    {
        string source = RepoFiles.Read("WheelTalk.Core/Decoding/GotwayDecoder.cs");

        Assert.Contains("""alertLine.Append("Speed2 ")""", source);
        Assert.DoesNotMatch(new Regex(@"alertLine\.ToString\(\)\.Trim\(\)"), source);
    }
}
