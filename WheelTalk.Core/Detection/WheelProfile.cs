using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Detection;

/// <summary>
/// Отпечаток одного семейства колёс: полный набор служб GATT и характеристик в каждой. Перенесено
/// из `app/src/main/res/raw/bluetooth_services.json` оригинала — там это ресурс, здесь таблица в
/// коде, чтобы UUID проверялись компилятором и читались в diff.
/// <para>
/// Отпечатков у семейства бывает несколько: прошивки отличаются составом служб, и оригинал держит
/// по строке на каждый встреченный вариант. Совпасть должно **всё** — и число служб, и число
/// характеристик в каждой. Это нарочно строго: колесо с лишней службой лучше не опознать вовсе,
/// чем принять за соседнее семейство и слать ему чужие команды.
/// </para>
/// </summary>
public sealed record WheelProfile(WheelFamily Family, IReadOnlyDictionary<string, string[]> Services)
{
    /// <summary>
    /// Совпало ли обнаруженное дерево с этим отпечатком. Порядок служб и характеристик не важен —
    /// важен состав: Android отдаёт их в порядке, который зависит от прошивки.
    /// </summary>
    public bool Matches(IReadOnlyList<DiscoveredService> discovered)
    {
        if (discovered.Count != Services.Count) return false;

        foreach (var service in discovered)
        {
            if (!Services.TryGetValue(service.Uuid, out string[]? expected)) return false;
            if (service.Characteristics.Count != expected.Length) return false;

            foreach (string characteristic in expected)
            {
                if (!service.Characteristics.Contains(characteristic, StringComparer.OrdinalIgnoreCase)) return false;
            }
        }

        return true;
    }
}
