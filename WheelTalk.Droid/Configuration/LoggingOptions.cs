namespace WheelTalk.Droid.Configuration;

/// <summary>
/// What gets written to disk. The ride history is a button on the screen; the raw dump is here,
/// off by default, exactly as the original gates it — it is a debugging tool that costs about a
/// megabyte per ten minutes and never rotates.
/// </summary>
public sealed class LoggingOptions
{
    public const string SectionName = "Logging";

    public bool RawDump { get; set; }

    /// <summary>
    /// Когда писать поток телеметрии — см. <see cref="Configuration.TelemetryRecording"/>. Кнопка
    /// «Запись» от этого меняет смысл: в <see cref="TelemetryRecording.Always"/> она только
    /// размечает поток («отсюда покатушка»), в остальных остаётся тем, чем была.
    /// </summary>
    public TelemetryRecording TelemetryRecording { get; set; } = TelemetryRecording.RideOnly;

    /// <summary>
    /// Начинать запись поездки самой, как только колесо на связи. **Включено** по умолчанию, в
    /// отличие от оригинала: там журнал — дело добровольное, а у нас на записанных поездках
    /// держится всё остальное, и забытая кнопка стоит целого выхода. Именно так и вышло 28.07.
    /// </summary>
    public bool AutoStartRide { get; set; } = true;

    /// <summary>
    /// Начинать запись не при подключении, а когда колесо впервые поехало быстрее этого, км/ч.
    /// Ноль — писать с момента подключения. Порт <c>startAutoLoggingWhenIsMovingMore</c>
    /// (<c>MainActivity.kt:439-444</c>), вместе с его значением по умолчанию — 7 км/ч.
    /// <para>
    /// Порог смотрят <b>один раз за подключение</b>: как только он взят, запись идёт непрерывно —
    /// стоянки, светофоры и зарядка включительно. Фильтровать каждую строку по скорости нельзя,
    /// и это не мелочь: кривая «напряжение покоя ↔ заряд» для прогнозов (план 9 §3) строится
    /// именно на стоящем колесе. Оригинал по той же причине разбирается со стоянками при чтении,
    /// а не при записи.
    /// </para>
    /// </summary>
    public double AutoStartAboveKmh { get; set; } = 7;
}
