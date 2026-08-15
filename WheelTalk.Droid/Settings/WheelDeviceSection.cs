using WheelTalk.Core.Services;
using WheelTalk.Core.Settings.Device;

namespace WheelTalk.Droid.Settings;

/// <summary>
/// Раздел «Конфигурация колеса» глазами обоих экранов: корень настроек решает по нему, показывать
/// ли карточку и что писать в сводке, а страница раздела — показывать ли список и какой строкой
/// объяснить пустоту (план 34 §5).
/// <para>
/// Здесь только два перевода: состояние ядра → ключ ресурса и состояние → «есть ли что показывать».
/// Сам разбор четырёх состояний живёт в <see cref="WheelSettingsState"/>, где его держат тесты, а
/// не экран.
/// </para>
/// </summary>
internal static class WheelDeviceSection
{
    /// <summary>
    /// Состояние раздела прямо сейчас. Часы — системные, те же, по которым декодер ставит время
    /// снимку.
    /// </summary>
    /// <param name="watchingSince">
    /// С какого мгновения экран ждёт ответа. Берётся появление экрана: у сессии нет наружного
    /// «на связи с такого-то часа», и заводить его в ядре ради одной надписи дороже, чем считать от
    /// того, когда спросили. На вердикт это влияет только там, где снимка не было вовсе.
    /// </param>
    public static WheelSettingsView Resolve(WheelSession session, DateTimeOffset watchingSince, TimeProvider clock) =>
        WheelSettingsState.Resolve(session.CurrentState, session.LastSnapshot, watchingSince, clock.GetUtcNow());

    /// <summary>Список строк показывается ровно в одном состоянии — когда снимок свеж.</summary>
    public static bool ShowsValues(WheelSettingsView view) => view == WheelSettingsView.Values;

    /// <summary>
    /// Чем объяснить пустоту. <c>null</c> — объяснять нечего: ожидание короче десяти секунд молчит,
    /// потому что сказать ему пока нечего, а показанные значения говорят сами за себя.
    /// <para>
    /// «Колесо другой марки» — на случай, когда страницу уже открыли, а колесо сменилось на чужое:
    /// в корне карточки такого раздела нет вовсе, но пустой экран без слов на месте открытой
    /// страницы был бы враньём (план 34 §5).
    /// </para>
    /// </summary>
    public static string? TextKey(WheelSettingsView view) => view switch
    {
        WheelSettingsView.Offline => "SettingsWheelDeviceEmpty",
        WheelSettingsView.OtherBrand => "SettingsWheelDeviceOtherBrand",
        WheelSettingsView.NotReported => "SettingsWheelDeviceNotReported",
        WheelSettingsView.NoAnswer => "SettingsWheelDeviceNoAnswer",
        _ => null,
    };
}
