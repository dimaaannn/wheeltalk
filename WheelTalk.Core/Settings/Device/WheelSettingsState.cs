using WheelTalk.Core.Contracts;
using WheelTalk.Core.Services;

namespace WheelTalk.Core.Settings.Device;

/// <summary>
/// Что раздел «Конфигурация колеса» может сказать прямо сейчас (план 34 §5). Состояний четыре, и
/// ответ у каждого свой: ни в одном из них список не выдумывается, а показанное число всегда
/// значит «столько у колеса сейчас», а не «столько было когда-то».
/// </summary>
public enum WheelSettingsView
{
    /// <summary>Связи нет. Настройка колеса — состояние устройства, и вчерашняя, выданная за сегодняшнюю, опаснее пустого экрана.</summary>
    Offline,

    /// <summary>Колесо не той марки, чьи настройки мы читать умеем. В корне настроек карточки такого раздела не бывает вовсе (решение владельца 16.08.2026).</summary>
    OtherBrand,

    /// <summary>Прошивка страниц не шлёт: кадр короче, чем байт номера страницы, — у такого колеса настроек не спросишь.</summary>
    NotReported,

    /// <summary>Связь живая, кадр настроек ещё в пути. Меньше десяти секунд — говорить нечего, и мы молчим, а не гадаем.</summary>
    Waiting,

    /// <summary>Десять секунд при живой связи прошли, кадра нет.</summary>
    NoAnswer,

    /// <summary>Снимок свежий — показываем строки.</summary>
    Values,
}

/// <summary>
/// Разбор четырёх состояний раздела — чистой функцией от связи, последнего кадра и часов
/// (план 34 §5). Живёт в ядре, а не в экране, ровно по правилу §2 плана: всё, что можно проверить
/// тестом, проверяется тестом, а Droid остаётся тонким слоем показа.
/// </summary>
public static class WheelSettingsState
{
    /// <summary>
    /// Сколько ждать кадра настроек, прежде чем сказать «не получены», и сколько снимок считается
    /// свежим. Десять секунд — два с половиной периода: страница настроек приходит раз в 4 секунды,
    /// без единого пропуска на всех трёх наших записях (план 34 §1.2). Один пропущенный кадр — не
    /// повод стирать экран, три подряд — уже не связь, а память.
    /// </summary>
    public static readonly TimeSpan Silence = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Настройки мы сегодня читаем у одной марки — Veteran (LeaperKim). Для прочих раздела нет
    /// вовсе: не серого, не пустого, а никакого (решение владельца 16.08.2026, план 34 §12.0 п. 4).
    /// </summary>
    public static bool Readable(WheelType type) => type == WheelType.Veteran;

    /// <summary>
    /// Шлёт ли колесо страницы вовсе. Номер страницы стоит байтом 46 кадра, и у колёс младше пятого
    /// поколения кадр до него не дотягивает — <c>VeteranDecoder.DecodeSmartBms</c> выходит по тому
    /// же признаку (<c>_protocolVersion &lt; 5 || buff.Length &lt;= 46</c>), так что ни страниц BMS,
    /// ни страницы настроек от такого колеса не будет никогда. Abrams (002), Patton (004) — сюда.
    /// <para>
    /// Версия читается из строки прошивки (<c>"002.0.02"</c>), а её колесо присылает первым же
    /// кадром телеметрии. Пустая строка — кадра ещё не было: это не приговор, а незнание, и мы
    /// отвечаем «шлёт», чтобы не объявить молчащее колесо старым.
    /// </para>
    /// </summary>
    public static bool SendsPages(string version)
    {
        if (version.Length == 0) return true;

        int dot = version.IndexOf('.');
        string head = dot < 0 ? version : version[..dot];
        return !int.TryParse(head, out int generation) || generation >= 5;
    }

    /// <param name="link">Связь с колесом. Погоня за оборвавшейся связью — тоже «нет связи»: на экране остались бы показания, которых колесо больше не подтверждает.</param>
    /// <param name="frame">Последний кадр телеметрии — <c>null</c>, если колесо ещё не сказало ни слова.</param>
    /// <param name="watchingSince">С какого мгновения экран ждёт ответа. От него отсчитываются десять секунд, пока снимка нет вовсе.</param>
    /// <param name="now">Сейчас.</param>
    public static WheelSettingsView Resolve(
        ConnectionState link,
        TelemetrySnapshot? frame,
        DateTimeOffset watchingSince,
        DateTimeOffset now)
    {
        // Марка — раньше связи: у колеса чужой марки раздела нет ни на связи, ни без неё, и
        // предложить «подключитесь» там значило бы пообещать то, чего подключение не даст.
        if (frame is not null && !Readable(frame.WheelType)) return WheelSettingsView.OtherBrand;

        if (link != ConnectionState.Connected) return WheelSettingsView.Offline;

        if (frame is not null && !SendsPages(frame.Version)) return WheelSettingsView.NotReported;

        // Снимок стареет: показанное число обязано значить «столько у колеса сейчас». Устаревший
        // снимок не показывается и заодно служит точкой отсчёта — молчание считается от последнего
        // ответа, а не от открытия экрана.
        var received = frame?.WheelSettings?.ReceivedAt;
        if (received is { } at && now - at < Silence) return WheelSettingsView.Values;

        // Возраст снимка и есть мера молчания, когда снимок был: заходить на страницу второй раз —
        // не повод начинать отсчёт заново и показывать ожидание там, где ответ уже известен. От
        // появления экрана считается только случай, когда снимка не было вовсе.
        var since = received ?? watchingSince;
        return now - since >= Silence ? WheelSettingsView.NoAnswer : WheelSettingsView.Waiting;
    }
}
