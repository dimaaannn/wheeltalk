namespace WheelTalk.Core.Services;

/// <summary>
/// Что делает тап по плашке связи (bugfix 2 §2.1). Решает по <see cref="WheelLink"/> — той же фазе,
/// которую показывает плашка, — а не по сыром <c>ConnectionState</c>: разойдись они, плашка говорила
/// бы одно, а тап делал другое.
/// </summary>
public enum LinkBadgeTapAction
{
    /// <summary>Ничего — связь жива и свежа, случайный тап её не трогает.</summary>
    None,

    /// <summary>«Оставь это колесо»: гасим сессию, ставим признак, ведём в поиск.</summary>
    GoToScan,

    /// <summary>Ведём в настройки: пароль просить нечем, только направить к полю, где он задаётся.</summary>
    GoToSettings,

    /// <summary>Реплей без активной сессии — тап переключает воспроизведение записи.</summary>
    ToggleReplay,
}

/// <summary>
/// Решение вынесено из <c>MainActivity.OnLinkBadgeTapped</c>: чистая функция от уже посчитанной фазы
/// связи, проверяется тестами, а не тапом по телефону.
/// </summary>
public static class LinkBadgeTap
{
    /// <param name="link">Фаза связи, посчитанная тем же <see cref="LinkStatus.Evaluate"/>, что рисует плашку.</param>
    /// <param name="awaitingPassword">Колесо ждёт пароль — молчание объяснимо, а не «связь пропала».</param>
    /// <param name="isReplay">Идёт воспроизведение записи, а не живое колесо.</param>
    public static LinkBadgeTapAction Decide(WheelLink link, bool awaitingPassword, bool isReplay) => link switch
    {
        // Подключены и данные свежи — обрывать связь случайным касанием посреди поездки нельзя.
        WheelLink.Connected => LinkBadgeTapAction.None,

        // Пароль не спрашиваем окном (решение владельца 08.08.2026) — ведём туда, где он задаётся.
        // Реплей исключён: писать в запись некуда, и решение здесь то же, что у самой плашки (LinkState).
        WheelLink.NoData when awaitingPassword && !isReplay => LinkBadgeTapAction.GoToSettings,

        // Связь жива, но кадров нет дольше порога — решение владельца 09.08.2026: тап уводит в поиск,
        // а не ждёт переподключения (это дело сессии самой, см. bugfix-1 §1.1).
        WheelLink.NoData => LinkBadgeTapAction.GoToScan,

        // Реплей без сессии — тап тот же, что у плашки «Запись готова»: пуск воспроизведения.
        (WheelLink.Idle or WheelLink.Failed) when isReplay => LinkBadgeTapAction.ToggleReplay,

        // Connecting, Reconnecting и живое «отключено хозяином/бедой» — как и раньше: оставляем колесо и ищем новое.
        _ => LinkBadgeTapAction.GoToScan,
    };
}
