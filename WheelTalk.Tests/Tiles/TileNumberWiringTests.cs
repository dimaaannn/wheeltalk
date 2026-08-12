using System.Text.RegularExpressions;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Как неподвижный бокс числа (<see cref="WheelTalk.Core.Tiles.NumberBox"/>) вставлен в экран.
/// Сам бокс — арифметика и проверен ею; здесь стережётся то, что арифметикой не поймать:
/// <b>единообразие</b> (подход касается всех видов с числом — слово владельца 11.08.2026),
/// <b>общий счёт</b> с подбором кегля и <b>срок жизни</b> — до пересоздания экрана.
/// <para>
/// По исходникам: android-проекты тестам не видны, а правила эти читаются глазами и ломаются
/// молча — новый вид плитки, забывший про бокс, ничем себя не выдаст, кроме дрожащих цифр на
/// телефоне.
/// </para>
/// </summary>
public class TileNumberWiringTests
{
    private const string Tiles = "WheelTalk.Dashboard.Droid/Screen/Tiles/";

    /// <summary>
    /// Каждый, кто рисует число, ставит его в бокс. Четверо: величина, крайнее значение, дистанция
    /// и число поверх графика — единообразно, как и велено.
    /// </summary>
    [Theory]
    [InlineData("MetricTileView.cs")]
    [InlineData("ExtremumTileView.cs")]
    [InlineData("TripTileView.cs")]
    [InlineData("ChartTileView.cs")]
    public void Every_drawer_of_a_number_puts_it_in_the_box(string view)
    {
        Assert.Contains("NumberBox.Fit(", RepoFiles.Read(Tiles + view));
    }

    /// <summary>
    /// Кегль и позиция считаются от одного и того же увиденного: бокс берётся той же худшей строкой
    /// (<c>MetricNumber.Widest</c>) и тем же счётом разрядов (<c>_digits</c>), что и подбор кегля.
    /// Разойдись они — число встанет в бокс, под который кегль не считался, и вылезет за край.
    /// </summary>
    [Fact]
    public void The_box_and_the_size_are_counted_from_the_same_sighting()
    {
        string source = RepoFiles.Read(Tiles + "TilesScreen.cs");
        string box = RepoFiles.MethodBody(
            source, "private int BoxWidth(MetricTile tile, MetricDescriptor metric)");

        Assert.Contains("MetricNumber.Widest(", box);
        Assert.Contains("_digits", box);

        // Тот же счёт кормит и строки, по которым садится кегль.
        Assert.Contains("_digits", RepoFiles.MethodBody(source, "private IEnumerable<TileText> Texts(TileMetrics metrics)"));
    }

    /// <summary>
    /// Фиксация живёт до пересоздания экрана и не дольше: счёт увиденного — поле адаптера, а не
    /// общее на приложение и не хранимое. Новый экран начинает с чистого счёта — «прыжок» один, и
    /// тот при первом широком показании.
    /// <para>
    /// Два отрицания — «поле не статическое» и «счёт не отдаётся хранилищу» — сняты ревизией
    /// 12.08.2026: они запрещали то, чего никто и не писал, и уронить их было нечем. Объявление
    /// поля ниже держит оба смысла разом: <c>private readonly</c> — это и «не статическое», и «не
    /// живёт дольше экрана».
    /// </para>
    /// </summary>
    [Fact]
    public void The_sighting_dies_with_the_screen()
    {
        string source = RepoFiles.Read(Tiles + "TilesScreen.cs");

        Assert.Matches(@"private readonly Dictionary<string, int> _digits = new\(StringComparer\.Ordinal\);", source);
    }
}
