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
    /// Потолок суммарного веса сырых дампов в каталоге колеса, МБ (план 11 §4.5). При превышении
    /// сносится самое старое — правило и его границы в <c>RawDumpCap</c>.
    /// <para>
    /// <b>Двести</b>: около мегабайта на десять минут — это тридцать с лишним часов записи, то есть
    /// сезон забытого включённым дампа, и при этом доля гигабайта на телефоне. Ноль — потолка нет
    /// (соглашение оригинала «ноль выключает»), и это отладочная лазейка, а не режим.
    /// </para>
    /// <para>
    /// Живёт здесь, а не в <c>StorageOptions</c>: там описано, как <c>RideStore</c> тратит время и
    /// диск <b>под базу</b>, а дамп — не база и не поездка, это файл журнала рядом с тем самым
    /// выключателем, которым его включают. Один предмет — один объект настроек.
    /// </para>
    /// </summary>
    public int RawDumpCapMb { get; set; } = 200;

    /// <summary>
    /// Когда писать поток телеметрии — см. <see cref="Configuration.TelemetryRecording"/>. Кнопка
    /// «Запись» от этого меняет смысл: в <see cref="TelemetryRecording.Always"/> она только
    /// размечает поток («отсюда покатушка»), в остальных остаётся тем, чем была.
    /// </summary>
    public TelemetryRecording TelemetryRecording { get; set; } = TelemetryRecording.Always;

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
