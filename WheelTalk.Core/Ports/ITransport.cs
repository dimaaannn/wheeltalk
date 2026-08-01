namespace WheelTalk.Core.Ports;

/// <summary>
/// Lower boundary of the core — the wheel-facing byte transport. Implemented by
/// <c>Ble.WindowsBleClient</c> and <c>Ble.AndroidBleClient</c> (real hardware) and
/// <c>Playback.ReplayTransport</c> (a recorded dump instead of a wheel).
/// Core code must never depend on this interface's implementations, only the interface itself.
/// </summary>
public interface ITransport
{
    event Action<byte[]>? DataReceived;

    /// <summary>
    /// Raised when the link drops on its own — the wheel was switched off, or went out of range.
    /// A <see cref="DisconnectAsync"/> the caller asked for does not raise it. Fires on whichever
    /// thread the transport learns about it, so handlers must not assume a UI thread.
    /// </summary>
    event Action? ConnectionLost;

    /// <summary>
    /// Scans until <paramref name="ct"/> is cancelled, yielding each peripheral as it is seen.
    /// A device is reported again if its name resolves after the first sighting, so consumers
    /// building a list should key it by <see cref="DiscoveredDevice.Address"/>.
    /// </summary>
    IAsyncEnumerable<DiscoveredDevice> ScanAsync(CancellationToken ct = default);

    /// <summary>
    /// Подключается и возвращает то, что нашёл: службы GATT со своими характеристиками. Что это за
    /// колесо, транспорт не решает — решает <c>WheelDetector</c> в ядре по этому самому списку.
    /// <para>
    /// Пустой список означает «дерева нет вовсе» — так отвечает реплей записанной поездки, где
    /// никакого GATT не было. Это не отказ: протокол там опознаётся по заголовкам кадров, как и на
    /// живом колесе.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<DiscoveredService>> ConnectAsync(string address, CancellationToken ct = default);

    /// <summary>
    /// То же подключение, но к колесу, которого сейчас может не быть рядом: ждёт его появления,
    /// пока жив <paramref name="ct"/>, самым дешёвым способом, какой есть у платформы. У Android
    /// это <c>connectGatt(autoConnect: true)</c> — ожидание на уровне BLE-контроллера, радио
    /// свободно; так переподключается оригинальный WheelLog. Платформа без такого способа просто
    /// пробует подключиться сейчас — повторы, как и всегда, остаются за <c>WheelSession</c>.
    /// </summary>
    Task<IReadOnlyList<DiscoveredService>> WaitForWheelAsync(string address, CancellationToken ct = default)
        => ConnectAsync(address, ct);

    Task DisconnectAsync(CancellationToken ct = default);
    Task WriteAsync(byte[] cmd, CancellationToken ct = default);

    /// <summary>
    /// Кормит ли транспорт записанным дампом вместо живого колеса. По этому свойству UI решает
    /// всё, что зависит от режима: реплей не автоподключается, не просит разрешений BLE и
    /// исключения из экономии заряда, получает кнопку «Пуск». Свойство контракта, а не проверка
    /// типа в экране: конкретных транспортов UI знать не должен (план 19, Б1). Умолчание —
    /// «живое колесо», переопределяет его один <c>Playback.ReplayTransport</c>.
    /// </summary>
    bool IsReplay => false;
}
