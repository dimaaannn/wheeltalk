using System.Globalization;
using WheelTalk.Core.Contracts;

namespace WheelTalk.Debug;

/// <summary>
/// Appends decoded <see cref="TelemetrySnapshot"/> rows to a CSV file, one row per snapshot —
/// raw fixed-point fields as exposed by the contract (no unit conversion), meant as recorded
/// fixture data for future decoder unit tests, not a human-readable report.
/// </summary>
public sealed class TelemetryCsvWriter : IDisposable
{
    private static readonly string[] Header =
    [
        "TimestampUtc", "SpeedRaw", "VoltageRaw", "CurrentRaw", "PhaseCurrentRaw", "PowerRaw",
        "Pwm", "MaxPwm", "Battery", "TemperatureRaw", "Temperature2Raw", "TopSpeedRaw",
        "WheelDistance", "TotalDistance", "DistanceFromStart", "Angle", "ChargingStatus",
        "SleepTimerSec", "WheelAlarm", "Alert", "Version", "Model", "WheelType",
        "Bms1Voltage", "Bms1MinCell", "Bms1MaxCell", "Bms1CellDiff",
        "Bms2Voltage", "Bms2MinCell", "Bms2MaxCell", "Bms2CellDiff",
    ];

    private readonly StreamWriter _writer;

    public string Path { get; }
    public int RowsWritten { get; private set; }

    public TelemetryCsvWriter(string path)
    {
        Path = path;
        string? dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _writer = new StreamWriter(path, append: false) { AutoFlush = true };
        _writer.WriteLine(string.Join(',', Header));
    }

    public void WriteRow(TelemetrySnapshot s)
    {
        string[] fields =
        [
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            s.SpeedRaw.ToString(CultureInfo.InvariantCulture),
            s.VoltageRaw.ToString(CultureInfo.InvariantCulture),
            s.CurrentRaw.ToString(CultureInfo.InvariantCulture),
            s.PhaseCurrentRaw.ToString(CultureInfo.InvariantCulture),
            s.PowerRaw.ToString(CultureInfo.InvariantCulture),
            s.Pwm.ToString(CultureInfo.InvariantCulture),
            s.MaxPwm.ToString(CultureInfo.InvariantCulture),
            s.Battery.ToString(CultureInfo.InvariantCulture),
            s.TemperatureRaw.ToString(CultureInfo.InvariantCulture),
            s.Temperature2Raw.ToString(CultureInfo.InvariantCulture),
            s.TopSpeedRaw.ToString(CultureInfo.InvariantCulture),
            s.WheelDistance.ToString(CultureInfo.InvariantCulture),
            s.TotalDistance.ToString(CultureInfo.InvariantCulture),
            s.DistanceFromStart.ToString(CultureInfo.InvariantCulture),
            s.Angle.ToString(CultureInfo.InvariantCulture),
            s.ChargingStatus.ToString(CultureInfo.InvariantCulture),
            s.SleepTimerSec.ToString(CultureInfo.InvariantCulture),
            s.WheelAlarm.ToString(CultureInfo.InvariantCulture),
            CsvEscape(s.Alert),
            CsvEscape(s.Version),
            CsvEscape(s.Model),
            s.WheelType.ToString(),
            s.Bms1.Voltage.ToString(CultureInfo.InvariantCulture),
            s.Bms1.MinCell.ToString(CultureInfo.InvariantCulture),
            s.Bms1.MaxCell.ToString(CultureInfo.InvariantCulture),
            s.Bms1.CellDiff.ToString(CultureInfo.InvariantCulture),
            s.Bms2.Voltage.ToString(CultureInfo.InvariantCulture),
            s.Bms2.MinCell.ToString(CultureInfo.InvariantCulture),
            s.Bms2.MaxCell.ToString(CultureInfo.InvariantCulture),
            s.Bms2.CellDiff.ToString(CultureInfo.InvariantCulture),
        ];
        _writer.WriteLine(string.Join(',', fields));
        RowsWritten++;
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    public void Dispose() => _writer.Dispose();
}
