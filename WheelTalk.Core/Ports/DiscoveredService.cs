namespace WheelTalk.Core.Ports;

/// <summary>
/// Одна служба GATT, какой её увидел транспорт: UUID и UUID её характеристик. Транспорт только
/// перечисляет находку — что она означает, решает <see cref="Detection.WheelDetector"/>.
/// <para>
/// UUID строками, а не платформенным типом: у Android это <c>Java.Util.UUID</c>, у Windows —
/// <c>System.Guid</c>, и ядру нельзя знать ни того ни другого. Сравнение регистронезависимое —
/// приводить к одному виду обязан детектор, а не каждый транспорт по-своему.
/// </para>
/// </summary>
public sealed record DiscoveredService(string Uuid, IReadOnlyList<string> Characteristics);
