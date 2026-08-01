using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Detection;
using WheelTalk.Core.Ports;

namespace WheelTalk.Tests.Detection;

/// <summary>
/// Опознание устройств-посредников («третий глаз», план 20) — порт второго прохода `detectWheel`
/// (`BluetoothService.kt:182-193`) по таблице `bluetooth_proxy_services.json`.
/// <para>
/// Деревья в тестах ниже набраны **из JSON оригинала**, а не из <see cref="ProxyProfiles"/> — иначе
/// тест проверял бы нашу копию по нашей же копии, и опечатка в UUID при переносе прошла бы насквозь
/// незамеченной. Единственное исключение — тест на фикс <c>ffa8</c>: у оригинала этой метки нет
/// вовсе, и здесь неизбежно проверяется наше собственное отклонение (план 20 §6).
/// </para>
/// </summary>
public class ProxyProfilesTests
{
    private static readonly WheelDetector Detector = new(NullLogger<WheelDetector>.Instance);

    private static string Uuid(string shortForm) => $"0000{shortForm}-0000-1000-8000-00805f9b34fb";

    private static DiscoveredService Service(string shortForm, params string[] characteristics) =>
        new(Uuid(shortForm), [.. characteristics.Select(Uuid)]);

    /// <summary>Подпись чужого стека (Nordic SoftDevice), общая у всех восьми записей JSON.</summary>
    private static DiscoveredService[] NordicSoftDeviceSignature() =>
    [
        Service("1800", "2a00", "2a01", "2a04", "2aa6"),
        Service("1801"),
    ];

    // ---- Записи 1 и 2 — gotway, метка ffa7 (Veteran), байт в байт дублируют друг друга ---------

    [Fact]
    public void Record_1_gotway_ffa7_is_recognised()
    {
        DiscoveredService[] tree =
        [
            .. NordicSoftDeviceSignature(),
            Service("ffa0", "ffa1", "ffa7"),
            Service("ffe0", "ffe1"),
        ];

        Assert.Equal(WheelFamily.Gotway, Detector.Detect(tree));
    }

    [Fact]
    public void Record_2_gotway_ffa7_duplicate_is_recognised()
    {
        // Дословный дубликат записи №1 — те же байты, дерево то же самое. Опознаётся через
        // запись №1 (первое совпадение), запись №2 в её оригинальном виде (ffa7) в нашей таблице
        // больше не существует — она стала записью ffa8 (план 20 §6, коммит "Fix Begode proxy
        // tag"). Тест фиксирует дерево оригинала, а не нашу правку.
        DiscoveredService[] tree =
        [
            .. NordicSoftDeviceSignature(),
            Service("ffa0", "ffa1", "ffa7"),
            Service("ffe0", "ffe1"),
        ];

        Assert.Equal(WheelFamily.Gotway, Detector.Detect(tree));
    }

    // ---- Запись 3 — inmotion, метка ffa5 --------------------------------------------------------

    [Fact]
    public void Record_3_inmotion_ffa5_is_recognised()
    {
        DiscoveredService[] tree =
        [
            .. NordicSoftDeviceSignature(),
            Service("ffe0", "ffe4"),
            Service("ffe5", "ffe9"),
            Service("ffa0", "ffa1", "ffa5"),
            Service("fe00", "fe01", "fe02", "fe03", "fe04", "fe05", "fe06"),
        ];

        Assert.Equal(WheelFamily.InMotion, Detector.Detect(tree));
    }

    // ---- Запись 4 — inmotion_v2, метка ffa6, Nordic UART -----------------------------------------

    [Fact]
    public void Record_4_inmotion_v2_ffa6_is_recognised()
    {
        DiscoveredService[] tree =
        [
            .. NordicSoftDeviceSignature(),
            Service("ffa0", "ffa1", "ffa6"),
            new("6e400001-b5a3-f393-e0a9-e50e24dcca9e",
                ["6e400002-b5a3-f393-e0a9-e50e24dcca9e", "6e400003-b5a3-f393-e0a9-e50e24dcca9e"]),
        ];

        Assert.Equal(WheelFamily.InMotionV2, Detector.Detect(tree));
    }

    // ---- Запись 5 — kingsong, метка ffa9 ----------------------------------------------------------

    [Fact]
    public void Record_5_kingsong_ffa9_is_recognised()
    {
        DiscoveredService[] tree =
        [
            .. NordicSoftDeviceSignature(),
            Service("ffa0", "ffa1", "ffa9"),
            Service("ffe0", "ffe1"),
            Service("fff0", "fff1"),
        ];

        Assert.Equal(WheelFamily.KingSong, Detector.Detect(tree));
    }

    // ---- Запись 6 — ninebot, метка ffa2 -----------------------------------------------------------

    [Fact]
    public void Record_6_ninebot_ffa2_is_recognised()
    {
        DiscoveredService[] tree =
        [
            .. NordicSoftDeviceSignature(),
            Service("ffa0", "ffa1", "ffa2"),
            Service("ffe0", "ffe1"),
        ];

        Assert.Equal(WheelFamily.Ninebot, Detector.Detect(tree));
    }

    // ---- Записи 7 и 8 — ninebot_z, метки ffa3/ffa4, Nordic UART ------------------------------------

    [Fact]
    public void Record_7_ninebot_z_ffa3_is_recognised()
    {
        DiscoveredService[] tree =
        [
            .. NordicSoftDeviceSignature(),
            Service("ffa0", "ffa1", "ffa3"),
            new("6e400001-b5a3-f393-e0a9-e50e24dcca9e",
                ["6e400003-b5a3-f393-e0a9-e50e24dcca9e", "6e400002-b5a3-f393-e0a9-e50e24dcca9e"]),
        ];

        Assert.Equal(WheelFamily.NinebotZ, Detector.Detect(tree));
    }

    [Fact]
    public void Record_8_ninebot_z_ffa4_is_recognised()
    {
        DiscoveredService[] tree =
        [
            .. NordicSoftDeviceSignature(),
            Service("ffa0", "ffa1", "ffa4"),
            new("6e400001-b5a3-f393-e0a9-e50e24dcca9e",
                ["6e400003-b5a3-f393-e0a9-e50e24dcca9e", "6e400002-b5a3-f393-e0a9-e50e24dcca9e"]),
        ];

        Assert.Equal(WheelFamily.NinebotZ, Detector.Detect(tree));
    }

    /// <summary>
    /// Сравнение отпечатков точное в обе стороны (план 20 §5, п.2): ни один отпечаток настоящего
    /// колеса не совпадает с отпечатком посредника — иначе нестрогое сравнение однажды отправило бы
    /// чужую команду не тому колесу (Ninebot — подмножество Begode, KingSong — те же ffe0/ffe1).
    /// </summary>
    [Fact]
    public void No_wheel_fingerprint_matches_a_proxy_fingerprint_or_the_other_way_round()
    {
        foreach (var wheel in WheelProfiles.All)
        {
            var discovered = ToDiscovered(wheel);
            Assert.DoesNotContain(ProxyProfiles.All, proxy => proxy.Matches(discovered));
        }

        foreach (var proxy in ProxyProfiles.All)
        {
            var discovered = ToDiscovered(proxy);
            Assert.DoesNotContain(WheelProfiles.All, wheel => wheel.Matches(discovered));
        }
    }

    /// <summary>
    /// Запись №2 больше не дубликат записи №1 (план 20 §6, тест из §5 п.3): после фикса они
    /// различаются ровно одной меткой в служебной службе (<c>ffa7</c> у Veteran, <c>ffa8</c> у
    /// Begode) и обе по-прежнему дают семейство <see cref="WheelFamily.Gotway"/> — AutoDecoder
    /// различит их сам по заголовку первого кадра, как и при прямом подключении.
    /// </summary>
    [Fact]
    public void Record_2_is_no_longer_a_duplicate_after_the_ffa8_fix()
    {
        DiscoveredService[] veteranTag =
        [
            .. NordicSoftDeviceSignature(),
            Service("ffa0", "ffa1", "ffa7"),
            Service("ffe0", "ffe1"),
        ];
        DiscoveredService[] begodeTag =
        [
            .. NordicSoftDeviceSignature(),
            Service("ffa0", "ffa1", "ffa8"),
            Service("ffe0", "ffe1"),
        ];

        Assert.NotEqual(veteranTag[2], begodeTag[2]);
        Assert.Equal(WheelFamily.Gotway, Detector.Detect(veteranTag));
        Assert.Equal(WheelFamily.Gotway, Detector.Detect(begodeTag));
    }

    private static DiscoveredService[] ToDiscovered(WheelProfile profile) =>
        [.. profile.Services.Select(s => new DiscoveredService(s.Key, s.Value))];
}
