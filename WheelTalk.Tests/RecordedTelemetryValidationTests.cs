using System.Globalization;

namespace WheelTalk.Tests;

/// <summary>
/// Sanity-checks a real recording (TestHarness.RecordTelemetryCsv, Begode MTen3, 84V/20S pack,
/// see AGENTS.md) against the ranges/invariants we know should hold for that session — catches
/// decoder regressions (e.g. a scaling factor or sign flip) that unit fixtures with hand-picked
/// frames might not exercise. This is validation of recorded output, not a byte-in/value-out
/// fixture — no raw BLE frames were captured alongside it, only decoded snapshots.
/// </summary>
public class RecordedTelemetryValidationTests
{
    private const string FixturePath = "Fixtures/mten3_recorded_20260719.csv";

    private static List<Dictionary<string, string>> LoadRows()
    {
        string[] lines = File.ReadAllLines(FixturePath);
        Assert.True(lines.Length > 1, "Fixture should have a header plus at least one data row.");

        string[] header = lines[0].Split(',');
        var rows = new List<Dictionary<string, string>>();
        foreach (string line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] fields = line.Split(',');
            var row = new Dictionary<string, string>(header.Length);
            for (int i = 0; i < header.Length; i++)
            {
                row[header[i]] = i < fields.Length ? fields[i] : "";
            }
            rows.Add(row);
        }
        return rows;
    }

    private static int Int(Dictionary<string, string> row, string column) =>
        int.Parse(row[column], CultureInfo.InvariantCulture);

    /// <summary>
    /// Тест «в записи есть строки» снят ревизией 12.08.2026: он утверждал ровно то, что уже
    /// проверяет <see cref="LoadRows"/> своим <c>Assert</c> о заголовке со строкой данных, — то есть
    /// стерёг фикстуру, а не декодер, и уронить его не могла никакая правка кода.
    /// </summary>
    [Fact]
    public void Every_row_reports_the_Gotway_wheel_type()
    {
        foreach (var row in LoadRows())
        {
            Assert.Equal("GotWay", row["WheelType"]);
        }
    }

    [Fact]
    public void Every_row_has_a_plausible_voltage_for_an_84V_pack()
    {
        // 20S pack: sane operating range well under full charge (84.0V) and above empty (~60V).
        foreach (var row in LoadRows())
        {
            double voltageV = Int(row, "VoltageRaw") / 100.0;
            Assert.InRange(voltageV, 60.0, 90.0);
        }
    }

    [Fact]
    public void Every_row_has_a_battery_percent_in_0_to_100()
    {
        foreach (var row in LoadRows())
        {
            Assert.InRange(Int(row, "Battery"), 0, 100);
        }
    }

    [Fact]
    public void Every_row_has_a_plausible_temperature()
    {
        foreach (var row in LoadRows())
        {
            int temperatureC = Int(row, "TemperatureRaw") / 100;
            Assert.InRange(temperatureC, -10, 80);
        }
    }

    [Fact]
    public void Speed_and_phase_current_are_non_negative_under_GotwayNegative_zero()
    {
        // appsettings.json's GotwayNegative default ("0" = abs) was active for this recording.
        foreach (var row in LoadRows())
        {
            Assert.True(Int(row, "SpeedRaw") >= 0, "SpeedRaw should be non-negative under GotwayNegative=0");
            Assert.True(Int(row, "PhaseCurrentRaw") >= 0, "PhaseCurrentRaw should be non-negative under GotwayNegative=0");
        }
    }

    [Fact]
    public void Handshake_resolves_model_and_firmware_version_during_the_session()
    {
        var rows = LoadRows();
        Assert.Contains(rows, r => r["Model"] == "Mten3");
        Assert.Contains(rows, r => r["Version"] == "1001001");
    }
}
