using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Detection;
using WheelTalk.Core.Ports;

namespace WheelTalk.Tests.Detection;

/// <summary>
/// Опознание семейства по дереву GATT — порт `WheelData.detectWheel`. Проверяется здесь, а не на
/// телефоне, потому что колёс четырёх семейств у нас нет и не будет: отпечатки взяты из таблицы
/// оригинала, и тесты стерегут именно её — что совпадение точное и что похожие деревья не
/// путаются между собой.
/// </summary>
public class WheelDetectorTests
{
    private static readonly WheelDetector Detector = new(NullLogger<WheelDetector>.Instance);

    private static string Uuid(string shortForm) => $"0000{shortForm}-0000-1000-8000-00805f9b34fb";

    private static DiscoveredService Service(string shortForm, params string[] characteristics) =>
        new(Uuid(shortForm), [.. characteristics.Select(Uuid)]);

    /// <summary>Дерево Sherman L — первый отпечаток Begode; на нём же сидит Veteran.</summary>
    private static DiscoveredService[] Gotway() =>
    [
        Service("1800", "2a00", "2a01", "2a02", "2a03", "2a04"),
        Service("1801", "2a05"),
        Service("180a", "2a23", "2a24", "2a25", "2a26", "2a27", "2a28", "2a29", "2a2a", "2a50"),
        Service("ffe0", "ffe1"),
    ];

    [Fact]
    public void The_wheel_we_ride_is_recognised()
    {
        Assert.Equal(WheelFamily.Gotway, Detector.Detect(Gotway()));
    }

    [Fact]
    public void Order_of_services_and_characteristics_does_not_matter()
    {
        // Android отдаёт службы в порядке, который зависит от прошивки, — состав важен, порядок нет.
        var shuffled = Gotway().Reverse().ToArray();

        Assert.Equal(WheelFamily.Gotway, Detector.Detect(shuffled));
    }

    /// <summary>
    /// Ninebot — это то же дерево минус служба 180a. Ровно тот случай, ради которого сравнение
    /// точное: по одной ffe0 эти два семейства неразличимы, и ошибка здесь означала бы чужие
    /// команды в колесо.
    /// </summary>
    [Fact]
    public void Ninebot_is_not_taken_for_a_begode()
    {
        DiscoveredService[] ninebot =
        [
            Service("1800", "2a00", "2a01", "2a02", "2a03", "2a04"),
            Service("1801", "2a05"),
            Service("ffe0", "ffe1"),
        ];

        Assert.Equal(WheelFamily.Ninebot, Detector.Detect(ninebot));
    }

    [Fact]
    public void KingSong_is_recognised_by_the_rest_of_the_tree_not_by_ffe0()
    {
        DiscoveredService[] kingsong =
        [
            Service("1800", "2a00", "2a01", "2a02", "2a03", "2a04"),
            Service("1801", "2a05"),
            Service("180a", "2a23", "2a24", "2a25", "2a26", "2a27", "2a28", "2a29", "2a2a", "2a50"),
            Service("fff0", "fff1", "fff2", "fff3", "fff4", "fff5"),
            Service("ffe0", "ffe1"),
        ];

        // Отличается от Begode единственной лишней службой fff0 — и это уже другое семейство.
        Assert.Equal(WheelFamily.KingSong, Detector.Detect(kingsong));
    }

    [Fact]
    public void An_extra_service_leaves_the_wheel_unrecognised()
    {
        DiscoveredService[] withExtra = [.. Gotway(), Service("1234", "5678")];

        // Строгость нарочная: неизвестную прошивку лучше не опознать, чем принять за соседнюю.
        Assert.Null(Detector.Detect(withExtra));
    }

    [Fact]
    public void A_missing_characteristic_leaves_the_wheel_unrecognised()
    {
        DiscoveredService[] short_ =
        [
            Service("1800", "2a00", "2a01", "2a02", "2a03", "2a04"),
            Service("1801", "2a05"),
            Service("180a", "2a23", "2a24", "2a25", "2a26", "2a27", "2a28", "2a29", "2a2a"),
            Service("ffe0", "ffe1"),
        ];

        Assert.Null(Detector.Detect(short_));
    }

    [Fact]
    public void Nothing_discovered_is_not_a_wheel()
    {
        Assert.Null(Detector.Detect([]));
    }

    /// <summary>
    /// Прошивка InMotion V2, у которой служба 1801 пуста. Пустой список обязан совпасть с пустым —
    /// иначе этот вариант просто не опознаётся, а он в таблице оригинала есть.
    /// </summary>
    [Fact]
    public void A_service_without_characteristics_still_matches()
    {
        DiscoveredService[] inmotionV2 =
        [
            Service("1800", "2a00", "2a01", "2a04", "2aa6"),
            Service("1801"),
            new("6e400001-b5a3-f393-e0a9-e50e24dcca9e",
                ["6e400002-b5a3-f393-e0a9-e50e24dcca9e", "6e400003-b5a3-f393-e0a9-e50e24dcca9e"]),
        ];

        Assert.Equal(WheelFamily.InMotionV2, Detector.Detect(inmotionV2));
    }

    [Fact]
    public void Case_of_the_uuids_does_not_matter()
    {
        var upper = Gotway()
            .Select(s => new DiscoveredService(s.Uuid.ToUpperInvariant(),
                [.. s.Characteristics.Select(c => c.ToUpperInvariant())]))
            .ToArray();

        Assert.Equal(WheelFamily.Gotway, Detector.Detect(upper));
    }

    [Fact]
    public void All_five_ported_protocol_families_are_supported()
    {
        Assert.True(WheelFamilies.IsSupported(WheelFamily.Gotway));
        Assert.True(WheelFamilies.IsSupported(WheelFamily.KingSong));
        Assert.True(WheelFamilies.IsSupported(WheelFamily.InMotion));
        Assert.True(WheelFamilies.IsSupported(WheelFamily.InMotionV2));
        Assert.False(WheelFamilies.IsSupported(WheelFamily.Ninebot));
    }
}
