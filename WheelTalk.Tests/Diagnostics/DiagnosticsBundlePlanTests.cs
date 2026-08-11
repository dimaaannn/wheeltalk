using WheelTalk.Core.Diagnostics;

namespace WheelTalk.Tests.Diagnostics;

/// <summary>
/// Замок состава комплекта диагностики (план 11 §4.3). Держит обещание, данное владельцу
/// устройства: наружу уходят журналы и выжимка по поездкам — <b>ни сырого дампа, ни базы</b>.
/// Обещание такого рода нарушается один раз и молча, поэтому проверяется не глазами.
/// </summary>
public class DiagnosticsBundlePlanTests
{
    [Theory]
    [InlineData("diagnostics.log")]
    [InlineData("diagnostics.log.1")]
    [InlineData("rides.txt")]
    public void The_bundle_takes_logs_and_our_own_summaries(string name) =>
        Assert.True(DiagnosticsBundlePlan.Allows(name));

    /// <summary>
    /// Сырой дамп и база — данные владельца целиком, им положена отдельная кнопка с отдельным
    /// вопросом. Спутники базы (<c>-wal</c>, <c>-shm</c>) — та же база, просто с другим хвостом.
    /// </summary>
    [Theory]
    [InlineData("raw-2026-08-11.csv")]
    [InlineData("ride.csv")]
    [InlineData("rides.db")]
    [InlineData("rides.db-wal")]
    [InlineData("rides.db-shm")]
    [InlineData("usersettings.json")]
    [InlineData("layout.json")]
    [InlineData("")]
    public void The_bundle_never_takes_the_owners_own_data(string name) =>
        Assert.False(DiagnosticsBundlePlan.Allows(name));

    [Fact]
    public void Composing_keeps_the_order_and_drops_what_there_is_nothing_to_send()
    {
        var composed = DiagnosticsBundlePlan.Compose(
        [
            new("diagnostics.log", "/files/diagnostics.log", 2048),
            // Пустой журнал прошлого запуска: строка «0 байт» обещала бы содержимое, которого нет.
            new("diagnostics.log.1", "/files/diagnostics.log.1", 0),
            // Дампа в комплекте не бывает, даже если кто-то однажды подставит его в список.
            new("raw.csv", "/files/raw.csv", 999_999),
            new("rides.txt", "/cache/rides.txt", 512),
            // Части без пути не существует.
            new("missing.txt", "", 100),
        ]);

        Assert.Equal(["diagnostics.log", "rides.txt"], composed.Select(part => part.Name));
        Assert.Equal(2560, DiagnosticsBundlePlan.TotalBytes(composed));
    }

    [Theory]
    [InlineData(512, "512 Б")]
    [InlineData(2048, "2,0 КБ")]
    [InlineData(3 * 1024 * 1024, "3,0 МБ")]
    public void The_weight_is_said_the_way_a_file_manager_says_it(long bytes, string expected) =>
        Assert.Equal(expected, DiagnosticsBundlePlan.Weigh(bytes).Replace('.', ','));
}
