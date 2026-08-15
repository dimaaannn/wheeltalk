namespace WheelTalk.Core.Decoding;

/// <summary>Тяжесть флага диагностики — прямо из таблицы производителя, не выведена по имени.</summary>
internal enum InMotionDiagnosticSeverity
{
    Warning,
    Error,
}

/// <summary>Один битовый флаг подкоманды диагностики InMotion (см. <see cref="InMotionP6Diagnostics"/>).</summary>
internal readonly record struct InMotionDiagnosticFlag(string Category, string Title, InMotionDiagnosticSeverity Severity);

/// <summary>
/// Раскладка подкоманды диагностики InMotion — 45 битовых флагов, общих для всей линейки V2 (не
/// только P6). Источник: <c>docs/originals-reference-data.md</c> §6, разбор LoEUC по <c>kh0.f</c>
/// (<c>kh0.java:16</c>), тип записи <c>v30(categoryEn, categoryRu, titleEn, titleRu, severity)</c>;
/// severity <c>d</c> → Error, <c>n</c> → Warning (<c>iy1.java:13,15</c>). Данные, не код: индекс
/// массива — это <c>i</c> из раскладки бита (<c>byteIndex = i/8, bitIndex = i%8</c>), менять порядок
/// строк нельзя.
/// </summary>
internal static class InMotionP6DiagnosticFlags
{
    public static readonly IReadOnlyList<InMotionDiagnosticFlag> All =
    [
        new("Driver board", "Phase current sensor fault", InMotionDiagnosticSeverity.Error),
        new("Driver board", "Bus current sensor fault", InMotionDiagnosticSeverity.Error),
        new("Motor", "Left Hall sensor fault", InMotionDiagnosticSeverity.Error),
        new("Motor", "Right Hall sensor fault", InMotionDiagnosticSeverity.Error),
        new("Battery", "Battery fault", InMotionDiagnosticSeverity.Error),
        new("Driver board", "IMU sensor fault", InMotionDiagnosticSeverity.Error),
        new("Communication", "Driver board communication fault 1", InMotionDiagnosticSeverity.Error),
        new("Communication", "Driver board communication fault 2", InMotionDiagnosticSeverity.Error),
        new("Communication", "HMIC communication fault 1", InMotionDiagnosticSeverity.Error),
        new("Communication", "HMIC communication fault 2", InMotionDiagnosticSeverity.Error),
        new("Driver board", "MOS temperature sensor fault", InMotionDiagnosticSeverity.Error),
        new("Motor", "Motor temperature sensor fault", InMotionDiagnosticSeverity.Error),
        new("Driver board", "Board hot-area sensor fault", InMotionDiagnosticSeverity.Error),
        new("Cooling", "Fan fault", InMotionDiagnosticSeverity.Error),
        new("HMIC", "HMIC RTC fault", InMotionDiagnosticSeverity.Error),
        new("HMIC", "HMIC flash fault", InMotionDiagnosticSeverity.Error),
        new("Driver board", "Bus voltage sensor fault", InMotionDiagnosticSeverity.Error),
        new("Battery", "Battery voltage sensor fault", InMotionDiagnosticSeverity.Error),
        new("Battery", "Battery cannot power off", InMotionDiagnosticSeverity.Error),
        new("Battery", "Battery cannot charge", InMotionDiagnosticSeverity.Error),
        new("Battery", "Critically low battery", InMotionDiagnosticSeverity.Warning),
        new("Battery", "Battery overvoltage", InMotionDiagnosticSeverity.Warning),
        new("Driver board", "Overcurrent", InMotionDiagnosticSeverity.Warning),
        new("Battery", "Low battery", InMotionDiagnosticSeverity.Warning),
        new("Battery", "Additional battery fault", InMotionDiagnosticSeverity.Error),
        new("Motor", "Motor overtemperature", InMotionDiagnosticSeverity.Warning),
        new("Temperature", "Vehicle overtemperature", InMotionDiagnosticSeverity.Warning),
        new("Driver board", "CPU overtemperature", InMotionDiagnosticSeverity.Warning),
        new("Driver board", "IMU overtemperature", InMotionDiagnosticSeverity.Warning),
        new("Safety", "Locked because of a safety issue", InMotionDiagnosticSeverity.Warning),
        new("Safety", "Overspeed", InMotionDiagnosticSeverity.Warning),
        new("Motor", "Unexpected motor spin", InMotionDiagnosticSeverity.Warning),
        new("Motor", "Motor blocked", InMotionDiagnosticSeverity.Warning),
        new("Safety", "Fall detected", InMotionDiagnosticSeverity.Warning),
        new("Safety", "Risky riding behavior", InMotionDiagnosticSeverity.Warning),
        new("Motor", "Motor no-load protection", InMotionDiagnosticSeverity.Warning),
        new("Safety", "Required self-check not passed", InMotionDiagnosticSeverity.Warning),
        new("Controls", "Power key held too long", InMotionDiagnosticSeverity.Warning),
        new("Battery", "Some batteries are not enabled", InMotionDiagnosticSeverity.Warning),
        new("Battery", "Battery calibration required", InMotionDiagnosticSeverity.Warning),
        new("Compatibility", "Software incompatible", InMotionDiagnosticSeverity.Warning),
        new("Firmware", "Functions limited by incomplete firmware update", InMotionDiagnosticSeverity.Warning),
        new("Safety", "Remote lock active", InMotionDiagnosticSeverity.Warning),
        new("Compatibility", "Hardware incompatible", InMotionDiagnosticSeverity.Warning),
        new("Cooling", "Fan speed too low", InMotionDiagnosticSeverity.Warning),
    ];
}
