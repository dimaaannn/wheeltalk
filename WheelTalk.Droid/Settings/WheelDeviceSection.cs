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
    /// Чем объяснить пустоту. Ответ **один на все случаи** — «настройки недоступны» (решение
    /// владельца 16.08.2026, правило на весь раздел любой марки: не реализовано, не знаем, что
    /// показать, или иная ошибка — снаружи это одно и то же). Разные причины различает журнал, а
    /// не экран: см. <see cref="Reason"/>.
    /// <para>
    /// <c>null</c> — объяснять нечего: ожидание короче десяти секунд молчит, потому что сказать ему
    /// пока нечего, а показанные значения говорят сами за себя.
    /// <para>
    /// Прежде здесь стояло четыре разных текста; они сохранены как причины в <see cref="Reason"/>.
    /// </para>
    /// </summary>
    public static string? TextKey(WheelSettingsView view) => view switch
    {
        WheelSettingsView.Values => null,
        WheelSettingsView.Waiting => null,
        _ => "SettingsWheelDeviceUnavailable",
    };

    /// <summary>
    /// Подробность для журнала — то, чего человеку не говорят. Наружу у всех этих случаев один
    /// ответ, и без такой строки разбор жалобы «у меня пусто» упирается в текст, одинаковый для
    /// пяти разных причин.
    /// </summary>
    public static string Reason(WheelSettingsView view) => view switch
    {
        WheelSettingsView.Offline => "связи с колесом нет",
        WheelSettingsView.OtherBrand => "колесо марки, чьи настройки читать не умеем",
        WheelSettingsView.NotReported => "прошивка страниц настроек не шлёт (кадр короче байта номера страницы)",
        WheelSettingsView.NoAnswer => "связь живая, кадра настроек нет дольше десяти секунд",
        WheelSettingsView.Values => "колесо ответило, но ни одного поля не показано — все закрыты сентинелом",
        _ => "причина не разобрана",
    };
}
