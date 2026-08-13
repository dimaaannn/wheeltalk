using WheelTalk.Core.Tiles;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Ось времени полноэкранного графика: секунда окна → мгновение, а мусор → «мгновения нет».
/// <para>
/// <b>Случившаяся поломка (телефон владельца, сборка 20).</b> Открытие полноэкранного графика роняло
/// приложение: <c>ArgumentOutOfRangeException</c> в <c>AddSeconds</c>. Звала разметку не отрисовка, а
/// система доступности — она читает график вслух и просит подписать значения ещё до первого кадра,
/// когда видимая область пуста и чужая библиотека держит в ней крайние числа <c>float</c>. Такое
/// число, прибавленное к дате, уносит её за пределы календаря.
/// </para>
/// <para>
/// Само число здесь и проверяется — счёт вынесен из android-библиотеки в ядро именно затем
/// (<see cref="AxisTime"/>); буквы подписи остались у того, кто рисует, и стерегутся замком по
/// исходнику ниже.
/// </para>
/// </summary>
public class AxisTimeTests
{
    private static readonly DateTimeOffset From = new(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Час окна — тот, каким открывают график с плитки по умолчанию.</summary>
    private const double Window = 3600;

    /// <summary>Своя секунда окна отвечает своим мгновением — ради этого разметка и живёт.</summary>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(1800, 10)]
    [InlineData(3600, 11)]
    public void A_second_of_the_window_answers_with_its_moment(double seconds, int hour)
    {
        var at = AxisTime.At(From, seconds, Window);

        Assert.NotNull(at);
        Assert.Equal(From.AddSeconds(seconds), at);
        Assert.Equal(hour, at.Value.Hour);
    }

    /// <summary>
    /// Мусор из чужой библиотеки не становится ни датой, ни исключением: <c>Float.MaxValue</c> в
    /// пустой видимой области, <c>NaN</c>, бесконечность и отрицательная бездна — всё это «точки нет».
    /// </summary>
    [Theory]
    [InlineData(float.MaxValue)]
    [InlineData(-float.MaxValue)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1e9)]
    [InlineData(1e12)]
    public void Rubbish_from_the_chart_library_has_no_moment_at_all(double seconds)
    {
        Assert.Null(AxisTime.At(From, seconds, Window));
    }

    /// <summary>
    /// Запас по обе стороны окна есть, и он не бесконечен: краевую метку и уехавший от зума край
    /// подписать надо, а числа, оторванные от окна на порядки, — уже не ось.
    /// </summary>
    [Theory]
    [InlineData(-Window, true)]
    [InlineData(Window * 2, true)]
    [InlineData(-Window * 1.001, false)]
    [InlineData(Window * 2.001, false)]
    public void The_slack_around_the_window_is_a_window_wide(double seconds, bool answered)
    {
        Assert.Equal(answered, AxisTime.At(From, seconds, Window) is not null);
    }

    /// <summary>
    /// Невозможное окно не строит невозможной даты. Длина окна приходит из сохранённой раскладки —
    /// то есть из файла, который правили не мы; календарь на этом кончиться не должен.
    /// </summary>
    [Theory]
    [InlineData(double.NaN, 60)]
    [InlineData(double.PositiveInfinity, 60)]
    [InlineData(-1, 60)]
    [InlineData(1e18, 1e18)]
    public void An_impossible_window_never_builds_an_impossible_date(double window, double seconds)
    {
        Assert.Null(AxisTime.At(From, seconds, window));
    }

    /// <summary>
    /// <b>Обе двери закрыты одним счётом.</b> Разметку оси спрашивает чтение вслух, выбранную точку —
    /// тап; вход у них один природы, и вторая дверь осталась бы открытой, если бы защита стояла
    /// только на первой. Показ живёт в android-библиотеке, потому и проверяется по исходнику.
    /// </summary>
    [Fact]
    public void Both_the_axis_and_the_picked_point_go_through_the_same_count()
    {
        string viewer = RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/Tiles/ChartViewer.cs");

        // Метка оси: нет мгновения — нет и подписи, пустая строка вместо краха.
        Assert.Contains(
            "AxisTime.At(from, value, windowSeconds) is { } at ? at.ToString(\"HH:mm\") : \"\"", viewer);

        // Выбранная точка — тем же счётом и тем же окном.
        Assert.Contains("AxisTime.At(from, entry.GetX(), options.Window.TotalSeconds) is { } at", viewer);

        // Голого сложения с датой в просмотре не осталось: оно и было тем самым крахом.
        Assert.DoesNotContain("from.AddSeconds(", viewer);
    }
}
