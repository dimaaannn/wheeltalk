using WheelTalk.Core.Tiles;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Точки отсчёта плиток-дистанций (решение владельца 10.08.2026). Правило одно и жёсткое:
/// <b>точку двигает только человек</b>. Всё остальное — смена колеса, новая поездка, перезапуск
/// приложения — обязано её не заметить, и проверяется это здесь, потому что заметить она может
/// молча: показанная дистанция выглядит правдоподобно при любой точке.
/// </summary>
public class TripBaselinesTests
{
    private const string Sherman = "AA:BB:CC:DD:EE:01";
    private const string Begode = "AA:BB:CC:DD:EE:02";

    /// <summary>
    /// Первая встреча заводит точку на нынешнем одометре: дистанция начинается с нуля, а не с
    /// полного пробега колеса, который к сегодняшнему пути отношения не имеет.
    /// </summary>
    [Fact]
    public void The_first_sight_of_a_wheel_starts_the_count_from_zero()
    {
        var points = new TripBaselines();

        Assert.Equal(0, points.Since(Sherman, "tile", 4218.7));
        Assert.Equal(12.3, points.Since(Sherman, "tile", 4231.0), 3);
    }

    /// <summary>
    /// Две дистанции рядом — «с последнего ТО» и «за сегодня» — считают один и тот же одометр от
    /// разных точек. Ради этого у плитки и заведено устойчивое имя.
    /// </summary>
    [Fact]
    public void Two_tiles_count_from_their_own_points()
    {
        var points = new TripBaselines();

        points.Since(Sherman, "service", 4000);
        points.Since(Sherman, "today", 4180);

        Assert.Equal(200, points.Since(Sherman, "service", 4200));
        Assert.Equal(20, points.Since(Sherman, "today", 4200));

        // Сброс одной другую не трогает: у каждой своя рука хозяина.
        points.Reset(Sherman, "today", 4200);

        Assert.Equal(200, points.Since(Sherman, "service", 4200));
        Assert.Equal(0, points.Since(Sherman, "today", 4200));
    }

    /// <summary>
    /// Колёса не мешают друг другу, а возвращение к прежнему продолжает прежний счёт — это и
    /// значит «дистанция запоминается для колеса».
    /// </summary>
    [Fact]
    public void A_wheel_change_does_not_touch_the_count()
    {
        var points = new TripBaselines();

        points.Since(Sherman, "tile", 4000);
        points.Since(Begode, "tile", 700);

        Assert.Equal(50, points.Since(Begode, "tile", 750));
        Assert.Equal(180, points.Since(Sherman, "tile", 4180));
    }

    /// <summary>Сброс — единственное, чем точку двигают, и после него счёт идёт с нуля.</summary>
    [Fact]
    public void A_reset_moves_the_point_and_nothing_else_does()
    {
        var points = new TripBaselines();

        points.Since(Sherman, "tile", 4000);
        Assert.Equal(180, points.Since(Sherman, "tile", 4180));

        points.Reset(Sherman, "tile", 4180);

        Assert.Equal(0, points.Since(Sherman, "tile", 4180));
        Assert.Equal(20, points.Since(Sherman, "tile", 4200));
    }

    /// <summary>
    /// Перезапуск приложения: хранилище отдаёт ту же строку новому набору точек, и счёт
    /// продолжается, а не начинается. Без этого дистанция «с последнего ТО» жила бы до первого
    /// закрытия приложения.
    /// </summary>
    [Fact]
    public void The_points_survive_a_restart()
    {
        var before = new TripBaselines();
        before.Since(Sherman, "service", 4000);
        before.Since(Begode, "today", 700);

        // Тот самый перезапуск: строка из хранилища — и совсем другой экземпляр.
        var after = TripBaselines.Read(before.Write());

        Assert.Equal(180, after.Since(Sherman, "service", 4180));
        Assert.Equal(50, after.Since(Begode, "today", 750));
    }

    /// <summary>
    /// Одометр ниже точки — это другое колесо под тем же адресом либо счётчик, сброшенный самим
    /// колесом. Отрицательный путь показывать нечестнее, чем начать заново.
    /// </summary>
    [Fact]
    public void An_odometer_below_the_point_starts_over_instead_of_going_negative()
    {
        var points = new TripBaselines();
        points.Since(Sherman, "tile", 4000);

        Assert.Equal(0, points.Since(Sherman, "tile", 12));
        Assert.Equal(8, points.Since(Sherman, "tile", 20));
    }

    /// <summary>
    /// Счётчик перемен — то, по чему хозяин хранилища понимает, что пора записать. Показ, которому
    /// нечего менять, обязан его не двигать: иначе хранилище писалось бы на каждом кадре.
    /// </summary>
    [Fact]
    public void Only_a_change_moves_the_revision()
    {
        var points = new TripBaselines();

        points.Since(Sherman, "tile", 4000);
        int afterFirst = points.Revision;

        points.Since(Sherman, "tile", 4100);
        Assert.Equal(afterFirst, points.Revision);

        points.Reset(Sherman, "tile", 4100);
        Assert.NotEqual(afterFirst, points.Revision);
    }

    /// <summary>
    /// Битая строка не роняет экран: точки заведутся заново. Потерять счёт обидно, но показать
    /// вместо экрана падение — хуже.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("не json вовсе")]
    [InlineData("""[{"wheel":"","tile":"t","km":1}]""")]
    public void Rubbish_reads_as_no_points_at_all(string? saved)
    {
        var points = TripBaselines.Read(saved);

        Assert.False(points.Knows(Sherman, "t"));
        Assert.Equal(0, points.Since(Sherman, "t", 4000));
    }
}
