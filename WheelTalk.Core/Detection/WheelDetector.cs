using Microsoft.Extensions.Logging;
using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Detection;

/// <summary>
/// Кто перед нами — по дереву GATT, а не по выбору человека. Порт `WheelData.detectWheel`
/// оригинала: обнаруженные службы сверяются с таблицей отпечатков
/// (<see cref="WheelProfiles"/>), первое полное совпадение и есть семейство.
/// <para>
/// Сервисом в ядре, а не проверкой внутри транспорта: транспорт умеет только перечислить, что
/// нашёл, — решение общее для всех платформ и проверяется тестами без телефона.
/// </para>
/// <para>
/// Опознание — не то же, что поддержка. Семейство может быть узнано и при этом не иметь у нас
/// декодера: InMotion в таблице есть, а разговаривать с ним пока нечем
/// (<see cref="WheelFamilies.IsSupported"/>). Разница важна для человека: «это InMotion, мы его
/// пока не умеем» и «непонятное устройство» — разные сообщения.
/// </para>
/// </summary>
public sealed partial class WheelDetector(ILogger<WheelDetector> logger)
{
    private readonly IReadOnlyList<WheelProfile> _profiles = WheelProfiles.All;

    /// <summary>
    /// Семейство или <c>null</c>, если ни один отпечаток не совпал.
    /// <para>
    /// Неопознанное дерево уходит в журнал целиком, службами и характеристиками. Это единственный
    /// способ потом добавить прошивку в таблицу: отпечаток снимается с чужого колеса один раз, и
    /// пересобрать его из «не подключилось» невозможно.
    /// </para>
    /// </summary>
    public WheelFamily? Detect(IReadOnlyList<DiscoveredService> discovered)
    {
        foreach (var profile in _profiles)
        {
            if (!profile.Matches(discovered)) continue;

            LogDetected(profile.Family);
            return profile.Family;
        }

        // Второй проход — по отпечаткам устройств-посредников (план 20, порт двойного вызова
        // `detectWheel` в `BluetoothService.kt:182-193`). Держим отдельно от таблицы колёс, а не
        // сливаем в одну: так в коде видно правило «колесо всегда проверяется первым», и diff с
        // оригиналом читается по файлам один к одному.
        foreach (var profile in ProxyProfiles.All)
        {
            if (!profile.Matches(discovered)) continue;

            LogDetectedViaProxy(profile.Family);
            return profile.Family;
        }

        LogUnknown(Describe(discovered));
        return null;
    }

    /// <summary>Дерево одной строкой: `ffe0[ffe1] 180a[2a23,2a24]`. Для журнала, не для человека.</summary>
    private static string Describe(IReadOnlyList<DiscoveredService> discovered) =>
        string.Join(" ", discovered.Select(s => $"{s.Uuid}[{string.Join(",", s.Characteristics)}]"));

    [LoggerMessage(EventId = 1400, EventName = "Wheel.Detected", Level = LogLevel.Information,
        Message = "Wheel.Detected {Family}")]
    private partial void LogDetected(WheelFamily family);

    [LoggerMessage(EventId = 1401, EventName = "Wheel.NotDetected", Level = LogLevel.Warning,
        Message = "Wheel.NotDetected — ни один отпечаток не совпал: {Tree}")]
    private partial void LogUnknown(string tree);

    // Отдельно от Wheel.Detected: у оригинала оба случая пишутся одной строкой, и потом не понять,
    // как подключились — через колесо напрямую или через посредника.
    [LoggerMessage(EventId = 1402, EventName = "Wheel.DetectedViaProxy", Level = LogLevel.Information,
        Message = "Wheel.DetectedViaProxy {Family}")]
    private partial void LogDetectedViaProxy(WheelFamily family);
}
