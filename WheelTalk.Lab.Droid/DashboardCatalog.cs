using Android.Content;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Layouts;
using WheelTalk.Lab.Droid.Layouts;

namespace WheelTalk.Lab.Droid;

/// <summary>
/// Список вариантов панели. Выбирается не один из них, а набор приёмов: варианты — это способ
/// увидеть приёмы в работе и в сравнении, а не готовые экраны, из которых один победит целиком.
/// <para>
/// Буква — это место в очереди по актуальности, а не порядок появления: A — то, что собрано из
/// принятых решений последним и что сейчас стоит на главном экране приложения, дальше по убыванию,
/// в конце — самое старое. Неудачные варианты не удаляются, а сдвигаются вниз: сравнивать новое
/// будет не с чем, если убрать то, из чего оно выросло.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/DashboardCatalog.cs</c>. Единственная правка — фабрика
/// принимает <see cref="Context"/>: нативному <c>View</c> он нужен в конструкторе, у MAUI-контрола
/// такого параметра не было.
/// </para>
/// </summary>
public static class DashboardCatalog
{
    public sealed record Variant(string Id, string Title, string Idea, Func<Context, DashboardOptions, DashboardView> Create);

    public static IReadOnlyList<Variant> All { get; } =
    [
        new("A", "Две ленты · авиа", "ШИМ и напряжение лентами, скорость цифрой, четыре справочных",
            (context, options) => new TwinTapesDashboard(context, options)),
        new("B", "Авиа", "напряжение полосой, ШИМ лентой, скорость с кольцом",
            (context, options) => new AviaDashboard(context, options)),
        new("C", "Лента и цифра", "одна лента ШИМ сбоку, центр — скорости",
            (context, options) => new SingleTapeDashboard(context, options)),
        new("D", "Две ленты", "движение по обоим краям, крупных цифр нет",
            (context, options) => new TapesDashboard(context, options)),
        new("E", "Дуга и цифра", "одна дуга ШИМ, крупная скорость внутри",
            (context, options) => new ArcDashboard(context, options)),
        new("F", "Линейка сегментов", "shift lights вдоль края, счёт вместо чтения",
            (context, options) => new SegmentDashboard(context, options)),
        new("G", "Экран-индикатор", "заливка снизу и очень крупная цифра",
            (context, options) => new FillDashboard(context, options)),
    ];

    public static Variant ById(string id) => All.FirstOrDefault(v => v.Id == id) ?? All[0];
}
