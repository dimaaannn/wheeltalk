using Android.Content;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Layouts;
using WheelTalk.Dashboard.Droid.Screen;

namespace WheelTalk.Droid.Main;

/// <summary>
/// Вариант панели: та же панель, нарисованная иначе. Подпись — ключом строки, а не строкой:
/// варианты живут в настройках, а настройки переводятся.
/// </summary>
/// <param name="Create">
/// Как собрать. <see cref="DashboardOptions"/> приходит снаружи — живой объект настроек панели один
/// на приложение, и вариант его не заводит, а получает.
/// </param>
public sealed record PanelVariant(string Id, string LabelKey, Func<Context, DashboardOptions, IMainScreen> Create);

/// <summary>
/// Второе измерение реестра (план 17 §3): экран «панель» один, а нарисован он может быть
/// по-разному. Здесь и список вариантов, и <b>живой</b> выбор — <see cref="CurrentId"/>, которым
/// правит строка настроек «Отображения»; хранится он общим слоем (решение владельца 09.08.2026:
/// выбор общий на приложение, не свойство колеса).
/// <para>
/// <b>В Release вариант ровно один</b> — нынешняя двухленточная панель. Прототипы стенда сюда не
/// переносятся: это решение владельца, и настройка с единственным пунктом всё равно нарушала бы
/// правило плана 6 §0 «настройка обязана что-то менять» — потому строка и прячется, пока вариант
/// один. Второй вариант под <c>#if DEBUG</c> заведён ради проверки самого механизма, а не ради
/// показа: он ничего не обещает райдеру и в собранное приложение не попадает.
/// </para>
/// </summary>
public sealed class PanelVariants(DashboardOptions options)
{
    /// <summary>Нынешняя панель: две ленты и крупная скорость. Она же — умолчание.</summary>
    public static readonly PanelVariant TwinTapes =
        new("twin-tapes", "PanelVariantTwinTapes", (context, dashboard) => new TwinTapesDashboard(context, dashboard));

#if DEBUG
    /// <summary>
    /// Проверочный вариант: та же панель без лент — одна скорость. Существует затем, чтобы выбор
    /// варианта было чем проверить: механизм, у которого один пункт, не проверяется вовсе.
    /// </summary>
    public static readonly PanelVariant SpeedOnly =
        new("speed-only", "PanelVariantSpeedOnly", (context, dashboard) => new SpeedOnlyDashboard(context, dashboard));
#endif

    /// <summary>
    /// Варианты в порядке показа. Первый — умолчание: им живут все, кто настройки не трогал, и он
    /// же остаётся единственным в Release.
    /// </summary>
    public IReadOnlyList<PanelVariant> All { get; } =
#if DEBUG
        [TwinTapes, SpeedOnly];
#else
        [TwinTapes];
#endif

    /// <summary>
    /// Выбранный вариант. Живой объект настройки — тот самый единственный экземпляр, который правит
    /// строка «Отображения» и читает сборка панели (план 29 §29.2). Неизвестный id — умолчание:
    /// в базе мог остаться выбор сборки, где вариант ещё был.
    /// </summary>
    public string CurrentId { get; set; } = TwinTapes.Id;

    public PanelVariant Current => All.FirstOrDefault(variant => variant.Id == CurrentId) ?? All[0];

    /// <summary>Собрать панель выбранного варианта — фабрика записи «панель» в реестре экранов.</summary>
    public IMainScreen Create(Context context) => Current.Create(context, options);
}
