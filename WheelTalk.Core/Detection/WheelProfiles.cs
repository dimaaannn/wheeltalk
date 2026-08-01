namespace WheelTalk.Core.Detection;

/// <summary>
/// Отпечатки всех семейств, какие знает оригинал. Порт `res/raw/bluetooth_services.json`
/// (WheelLog) — тот же состав, тот же порядок, те же варианты прошивок.
/// <para>
/// Как пересобирать, если оригинал обновится: там это массив объектов, где ключ `adapter` — имя
/// семейства, а остальные ключи — UUID служб со списком UUID характеристик. Здесь одна запись
/// таблицы = один объект оттуда.
/// </para>
/// <para>
/// Больше половины UUID — стандартные, вида <c>0000xxxx-0000-1000-8000-00805f9b34fb</c>: они
/// записаны четырьмя знаками через <see cref="Uuid"/>, иначе таблица превращается в простыню, где
/// опечатку не видно. Нестандартные — целиком.
/// </para>
/// </summary>
public static class WheelProfiles
{
    public static IReadOnlyList<WheelProfile> All { get; } =
    [
        // ---- Begode / Gotway (и Veteran на том же профиле) ---------------------------------
        new(WheelFamily.Gotway, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a02", "2a03", "2a04")),
            (Uuid("1801"), Uuids("2a05")),
            (Uuid("180a"), Uuids("2a23", "2a24", "2a25", "2a26", "2a27", "2a28", "2a29", "2a2a", "2a50")),
            (Uuid("ffe0"), Uuids("ffe1")))),

        new(WheelFamily.Gotway, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01")),
            (Uuid("1801"), Uuids("2a05", "2b2a", "2b29")),
            (Uuid("180a"), Uuids("2a23", "2a24", "2a25", "2a26", "2a27", "2a28", "2a29", "2a50")),
            (Uuid("ffe0"), Uuids("ffe1")),
            (Uuid("fff0"), Uuids("fff1")),
            ("1d14d6ee-fd63-4fa1-bfa4-8f47b42119f0", ["f7bf3564-fb6d-4e53-88a4-5e37e0326063"]))),

        // ---- InMotion ----------------------------------------------------------------------
        new(WheelFamily.InMotion, Tree(
            (Uuid("180a"), Uuids("2a23", "2a26", "2a29")),
            (Uuid("180f"), Uuids("2a19")),
            (Uuid("ffe0"), Uuids("ffe4")),
            (Uuid("ffe5"), Uuids("ffe9")),
            (Uuid("fff0"), Uuids("fff1", "fff2", "fff3", "fff4", "fff5", "fff6", "fff7", "fff8", "fff9")),
            (Uuid("ffd0"), Uuids("ffd1", "ffd2", "ffd3", "ffd4")),
            (Uuid("ffc0"), Uuids("ffc1", "ffc2")),
            (Uuid("ffb0"), Uuids("ffb1", "ffb2", "ffb3", "ffb4")),
            (Uuid("ffa0"), Uuids("ffa2", "ffa1")),
            (Uuid("ff90"), Uuids("ff91", "ff92", "ff93", "ff94", "ff95", "ff96", "ff97", "ff98", "ff99", "ff9a")),
            (Uuid("fc60"), Uuids("fc64")),
            (Uuid("fe00"), Uuids("fe01", "fe02", "fe03", "fe04", "fe05", "fe06")))),

        // ---- InMotion V2 -------------------------------------------------------------------
        new(WheelFamily.InMotionV2, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a04", "2aa6")),
            (Uuid("1801"), Uuids("2a05")),
            (Nordic, [NordicWrite, NordicNotify]))),

        new(WheelFamily.InMotionV2, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a04", "2aa6")),
            (Uuid("1801"), Uuids("2a05")),
            (Nordic, [NordicWrite, NordicNotify]),
            (Uuid("ffe5"), Uuids("ffe9")),
            (Uuid("ffe0"), Uuids("ffe4")))),

        // Служба 1801 без единой характеристики — так и в оригинале; пустой список обязан
        // совпасть с пустым, иначе прошивка не опознаётся.
        new(WheelFamily.InMotionV2, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a04", "2aa6")),
            (Uuid("1801"), []),
            (Nordic, [NordicWrite, NordicNotify]))),

        // ---- KingSong ----------------------------------------------------------------------
        new(WheelFamily.KingSong, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a02", "2a03", "2a04")),
            (Uuid("1801"), Uuids("2a05")),
            (Uuid("180a"), Uuids("2a23", "2a24", "2a25", "2a26", "2a27", "2a28", "2a29", "2a2a", "2a50")),
            (Uuid("fff0"), Uuids("fff1", "fff2", "fff3", "fff4", "fff5")),
            (Uuid("ffe0"), Uuids("ffe1")))),

        new(WheelFamily.KingSong, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a04", "2ac9")),
            (Uuid("1801"), Uuids("2a05")),
            (Uuid("180a"), Uuids("2a23", "2a24", "2a25", "2a26", "2a27", "2a28", "2a29", "2a50")),
            ("02f00000-0000-0000-0000-00000000fe00",
                ["02f00000-0000-0000-0000-00000000ff03", "02f00000-0000-0000-0000-00000000ff02",
                 "02f00000-0000-0000-0000-00000000ff00", "02f00000-0000-0000-0000-00000000ff01"]),
            (Uuid("ffe0"),
                [Uuid("ffe1"), Uuid("fff3"), Uuid("fff5"),
                 "0783b03e-8535-b5a0-7140-a304d2495cba", "0783b03e-8535-b5a0-7140-a304d2495cb8"]))),

        new(WheelFamily.KingSong, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01")),
            (Uuid("1801"), Uuids("2a05", "2b29", "2b2a")),
            (Uuid("ffe0"), Uuids("ffe2", "ffe1")),
            (Uuid("180a"), Uuids("2a29", "2a24", "2a25", "2a27", "2a26", "2a28", "2a23", "2a2a", "2a50")))),

        // ---- Ninebot -----------------------------------------------------------------------
        //
        // Тот же состав, что у первого отпечатка Begode, минус служба 180a. Ровно поэтому
        // сравнение и обязано быть точным: по одной ffe0 эти два семейства неразличимы.
        new(WheelFamily.Ninebot, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a02", "2a03", "2a04")),
            (Uuid("1801"), Uuids("2a05")),
            (Uuid("ffe0"), Uuids("ffe1")))),

        new(WheelFamily.NinebotZ, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a04")),
            (Uuid("1801"), []),
            (Nordic, [NordicNotify, NordicWrite]))),

        new(WheelFamily.NinebotZ, Tree(
            (Uuid("1800"), Uuids("2a00", "2a01", "2a04")),
            (Uuid("1801"), []),
            (Nordic, [NordicNotify, NordicWrite]),
            (Uuid("fee7"), Uuids("fec8", "fec7", "fec9")))),
    ];

    /// <summary>Nordic UART — на нём сидят InMotion V2 и Ninebot Z.</summary>
    private const string Nordic = "6e400001-b5a3-f393-e0a9-e50e24dcca9e";
    private const string NordicWrite = "6e400002-b5a3-f393-e0a9-e50e24dcca9e";
    private const string NordicNotify = "6e400003-b5a3-f393-e0a9-e50e24dcca9e";

    /// <summary>Стандартный 16-битный UUID в полном виде.</summary>
    private static string Uuid(string shortForm) => $"0000{shortForm}-0000-1000-8000-00805f9b34fb";

    private static string[] Uuids(params string[] shortForms) => [.. shortForms.Select(Uuid)];

    private static Dictionary<string, string[]> Tree(params (string Service, string[] Characteristics)[] services) =>
        services.ToDictionary(s => s.Service, s => s.Characteristics, StringComparer.OrdinalIgnoreCase);
}
