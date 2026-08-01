namespace WheelTalk.Core.Services;

/// <summary>
/// How hard <see cref="WheelSession"/> chases a wheel that went away. This is the only place the
/// retry policy lives: transports connect once and report failure, and never retry on their own.
/// </summary>
public sealed class ConnectionOptions
{
    public const string SectionName = "Connection";

    /// <summary>
    /// Pause before the first retry. Short on purpose: a failure right after a drop is usually a
    /// half-open link or a stale service cache, and those clear on an immediate second attempt.
    /// </summary>
    public TimeSpan FirstRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Ceiling the pause grows to, doubling after every failed attempt. A wheel that is simply
    /// switched off is not worth a connect every half-second — that is what buried the log on the
    /// field trip of 28.07.2026, 215 lines a second. Attempts continue for as long as the app runs.
    /// <para>
    /// Погоня после первой прямой попытки переходит на пассивное ожидание
    /// (<c>ITransport.WaitForWheelAsync</c>), которое обычно не возвращается, пока колесо не
    /// появится, — так что пауза на практике разделяет только попытки, отказавшие сразу:
    /// выключенный Bluetooth, транспорт без пассивного режима.
    /// </para>
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Сколько молчания при живом соединении считать обрывом. Колесо шлёт пять раз в секунду, так
    /// что пятнадцать секунд — это семьдесят пропущенных пакетов подряд: связи нет, что бы ни
    /// думал о ней Bluetooth-стек.
    /// <para>
    /// Число — оригинала (<c>BluetoothService.startReconnectTimer</c>, «magicPeriod = 15000»), а
    /// вот его арифметику мы не берём: там миллисекунды сравниваются с секундами
    /// (<c>(now - lastLifeData) / 1000 &gt; 15000</c>), отчего порог получается не пятнадцать
    /// секунд, а четыре с лишним часа. Проверено 30.07.2026 на телефоне: выключенное колесо
    /// оставалось «подключённым» 68 минут — ровно то, от чего сторож и заводится.
    /// </para>
    /// <para>Ноль выключает сторожа — соглашение оригинала для всех порогов.</para>
    /// </summary>
    public TimeSpan DataTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
