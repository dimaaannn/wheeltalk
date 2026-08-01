namespace WheelTalk.Core.Detection;

/// <summary>
/// Отпечатки устройств-посредников («третий глаз», план 20) — порт
/// `res/raw/bluetooth_proxy_services.json` (WheelLog), тот же состав и порядок, ничего не
/// исправлено, кроме одного места, помеченного отдельно ниже.
/// <para>
/// Посредник держит колесо и притворяется им для телефона: кадры зеркалятся байт в байт на тех же
/// UUID, что у настоящего колеса, поэтому декодеры и транспорт этой таблицы не касаются — только
/// опознание. К зеркалу железка добавляет свою подпись (служба <c>1800</c> с полем
/// <c>2aa6</c>/Central Address Resolution и пустая <c>1801</c> — стек Nordic SoftDevice) и
/// собственную служебную службу <c>ffa0</c>, где вторая характеристика — метка семейства. Разбор —
/// <see href="../../docs/proxy-devices.md">docs/proxy-devices.md</see> §2.
/// </para>
/// </summary>
public static class ProxyProfiles
{
    public static IReadOnlyList<WheelProfile> All { get; } =
    [
        // ---- Gotway (Veteran, метка ffa7) ---------------------------------------------------
        new(WheelFamily.Gotway, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a04", "2aa6")),
            (Uuid("1801"), []),
            (Uuid("ffa0"), Uuids("ffa1", "ffa7")),
            (Uuid("ffe0"), Uuids("ffe1")))),

        // Запись №2 оригинала была дословным дубликатом записи №1 (обе ffa7) — недостижимой,
        // потому что побеждает первое совпадение в порядке файла. Наше отклонение от оригинала
        // (решение владельца 02.08.2026, план 20 §6): метка исправлена на ffa8. У оригинала этой
        // метки в таблице нет вообще, хотя прошивка EUC Watch v2 объявляет Begode-посреднику
        // именно её (proxy-devices.md §2.2, §3.3) — то есть дословная копия реальный
        // Begode-посредник этой прошивки не опознаёт и рвёт соединение. Семейство остаётся
        // Gotway: Begode и Veteran внутри него по-прежнему различает AutoDecoder по заголовку
        // первого кадра, как и при прямом подключении. Проверить нечем — устройства нет; см.
        // AGENTS.md «Отклонения от оригинала».
        new(WheelFamily.Gotway, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a04", "2aa6")),
            (Uuid("1801"), []),
            (Uuid("ffa0"), Uuids("ffa1", "ffa8")),
            (Uuid("ffe0"), Uuids("ffe1")))),

        // ---- InMotion (метка ffa5) -----------------------------------------------------------
        new(WheelFamily.InMotion, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a04", "2aa6")),
            (Uuid("1801"), []),
            (Uuid("ffe0"), Uuids("ffe4")),
            (Uuid("ffe5"), Uuids("ffe9")),
            (Uuid("ffa0"), Uuids("ffa1", "ffa5")),
            (Uuid("fe00"), Uuids("fe01", "fe02", "fe03", "fe04", "fe05", "fe06")))),

        // ---- InMotion V2 (метка ffa6, Nordic UART) --------------------------------------------
        new(WheelFamily.InMotionV2, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a04", "2aa6")),
            (Uuid("1801"), []),
            (Uuid("ffa0"), Uuids("ffa1", "ffa6")),
            (Nordic, [NordicWrite, NordicNotify]))),

        // ---- KingSong (метка ffa9) -------------------------------------------------------------
        new(WheelFamily.KingSong, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a04", "2aa6")),
            (Uuid("1801"), []),
            (Uuid("ffa0"), Uuids("ffa1", "ffa9")),
            (Uuid("ffe0"), Uuids("ffe1")),
            (Uuid("fff0"), Uuids("fff1")))),

        // ---- Ninebot (метка ffa2) --------------------------------------------------------------
        new(WheelFamily.Ninebot, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a04", "2aa6")),
            (Uuid("1801"), []),
            (Uuid("ffa0"), Uuids("ffa1", "ffa2")),
            (Uuid("ffe0"), Uuids("ffe1")))),

        // ---- Ninebot Z (метки ffa3/ffa4, Nordic UART) -----------------------------------------
        // Ни одна разобранная прошивка EUC Watch v2 не подтверждает эти две метки (proxy-devices.md
        // §3.3) — оставлены как есть, дословной копией: проверить нечем, а исправлять таблицу без
        // возможности проверки значит вносить свою ошибку вместо чужой.
        new(WheelFamily.NinebotZ, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a04", "2aa6")),
            (Uuid("1801"), []),
            (Uuid("ffa0"), Uuids("ffa1", "ffa3")),
            (Nordic, [NordicNotify, NordicWrite]))),

        new(WheelFamily.NinebotZ, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a04", "2aa6")),
            (Uuid("1801"), []),
            (Uuid("ffa0"), Uuids("ffa1", "ffa4")),
            (Nordic, [NordicNotify, NordicWrite]))),

        // Ninebot S (проксируется как ffe0 [ffe1, ffe2], метка не задокументирована) в таблицу
        // оригинала не входит и здесь не добавляется — решение плана 20 §6: у Ninebot нет декодера,
        // чинить опознание протокола, с которым мы не говорим, незачем.
    ];

    /// <summary>Nordic UART — на нём сидят InMotion V2 и Ninebot Z, как и у настоящих колёс.</summary>
    private const string Nordic = "6e400001-b5a3-f393-e0a9-e50e24dcca9e";
    private const string NordicWrite = "6e400002-b5a3-f393-e0a9-e50e24dcca9e";
    private const string NordicNotify = "6e400003-b5a3-f393-e0a9-e50e24dcca9e";

    /// <summary>Стандартный 16-битный UUID в полном виде.</summary>
    private static string Uuid(string shortForm) => $"0000{shortForm}-0000-1000-8000-00805f9b34fb";

    private static string[] Uuids(params string[] shortForms) => [.. shortForms.Select(Uuid)];

    private static Dictionary<string, string[]> Tree(params (string Service, string[] Characteristics)[] services) =>
        services.ToDictionary(s => s.Service, s => s.Characteristics, StringComparer.OrdinalIgnoreCase);
}
