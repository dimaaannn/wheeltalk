using Android.Content;
using WheelTalk.Core.Metrics;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Dashboard.Droid.Screen.Tiles;
using WheelTalk.Droid.Resources.Strings;

namespace WheelTalk.Droid.Main;

/// <summary>
/// Один основной экран в реестре: чем он назван, чем помечен на корешке и чем его собрать.
/// <para>
/// Фабрика берёт <see cref="Context"/> и отдаёт готовый <see cref="IMainScreen"/> — зависимости
/// экрана она собирает сама (план 17 §3: «зависимости экранов разные, реестр фабрик это
/// узаконивает»). Хозяин экрана о них не знает: у него в руках остаётся узкий контракт.
/// </para>
/// <para>
/// Подпись делегатом, а не строкой: язык меняется без перезапуска
/// (<see cref="TranslateExtension"/>), и запомненная однажды строка пережила бы смену языка.
/// </para>
/// </summary>
public sealed record MainScreenEntry(string Id, int Icon, Func<string> Label, Func<Context, IMainScreen> Create);

/// <summary>
/// Все основные экраны приложения — списком, а не поимённо по коду (план 17 §3). Отсюда кормятся
/// трое: корешки шторки, показ экрана и — для панели — выбор варианта в настройках. До реестра
/// <c>MainActivity</c> знала экраны в четырёх местах сразу (две константы, тернарник, ручной список
/// корешков и фабрика плиток), и пятый экран пришлось бы дописывать во все четыре.
/// <para>
/// Порядок списка — порядок корешков в шторке. Первый — экран по умолчанию: с него начинает тот,
/// кто корешков ни разу не трогал.
/// </para>
/// </summary>
public sealed class MainScreenRegistry
{
    /// <summary>
    /// Идентификаторы хранятся в настройках (<c>Screen:Main</c>), поэтому это часть уговора с
    /// прежними запусками: переименование id забудет выбор человека.
    /// </summary>
    public const string PanelId = "panel";

    public const string TilesId = "tiles";

    private readonly IReadOnlyList<MainScreenEntry> _screens;

    public MainScreenRegistry(
        DashboardOptions dashboard,
        PanelVariants panels,
        IMetricHistory history,
        ITileLayoutStore tileLayout,
        TripPoints tripPoints,
        Func<string> wheelAddress)
    {
        _screens =
        [
            new(PanelId, QuickIcons.Panel, () => AppStrings.ScreenPanel, panels.Create),
            new(TilesId, QuickIcons.Tiles, () => AppStrings.ScreenTiles, context => new TilesScreen(
                context, dashboard, TranslateExtension.Get, history, tileLayout, tripPoints,
                wheelAddress)),
        ];
    }

    public IReadOnlyList<MainScreenEntry> Screens => _screens;

    /// <summary>Экран по умолчанию — первый в списке.</summary>
    public string DefaultId => _screens[0].Id;

    /// <summary>
    /// Запись по идентификатору, а если такого больше нет — экран по умолчанию. Неизвестный id —
    /// это не поломка, а выбор, сделанный сборкой, где экран ещё был: падать из-за него нельзя.
    /// </summary>
    public MainScreenEntry Find(string id) =>
        _screens.FirstOrDefault(screen => screen.Id == id) ?? _screens[0];
}
