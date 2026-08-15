using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Ui;

/// <summary>
/// Замок «экран «Данные» без фильтра» (state.md, решено 03.08.2026): показывает всё, что снимок
/// телеметрии отдаёт, а не подмножество. Проход 15.08.2026 добавил недостающее — температуры пакетов
/// BMS, ресурс и ёмкость пакета, поля KingSong/InMotion, ряд ячеек, — и здесь заперт их состав, а не
/// поведение: <c>TelemetryTable</c> живёт в <c>net10.0-android</c>, тесты его не поднимают, ссылки на
/// проект нет и не будет (несовместимый TFM) — единственный доступный замок читает исходник текстом.
/// <para>
/// Каждая новая подпись проверена дважды: код ссылается на ключ ресурса, а ресурс этот ключ
/// определяет, — порознь ни один из двух файлов не соврёт молча (переименовали ключ в одном месте,
/// забыли про другое).
/// </para>
/// </summary>
public class TelemetryTableCompositionTests
{
    private const string TablePath = "WheelTalk.Droid/Telemetry/TelemetryTable.cs";
    private const string StringsPath = "WheelTalk.Droid/Resources/Strings/AppStrings.resx";

    /// <summary>Новые подписи этапа — BMS-температуры и ресурс/ёмкость пакета (заказ владельца,
    /// пп. 1—2), плюс средняя ячейка и номера крайних банок, найденные аудитом снимка (п. 4).</summary>
    private static readonly string[] NewBmsKeys =
    [
        "TelemetryBmsTemp1", "TelemetryBmsTemp2", "TelemetryBmsTemp3", "TelemetryBmsTemp4",
        "TelemetryBmsTemp5", "TelemetryBmsTemp6",
        "TelemetryBmsFullCycles", "TelemetryBmsFactoryCap", "TelemetryBmsRemCap",
        "TelemetryBmsAvgCell", "TelemetryBmsMinCellNum", "TelemetryBmsMaxCellNum",
        "TelemetryBmsSemiVoltage1", "TelemetryBmsSemiVoltage2",
    ];

    /// <summary>Top-level поля снимка, которые никогда не попадали на экран «Данные»: KingSong,
    /// InMotion/InMotion V2 и ряд ячеек (заказ владельца п. 4). Подписи — готовые слова
    /// <c>MetricCatalogue</c>, а не новые дубликаты («одна величина — одно слово»).</summary>
    private static readonly string[] NewTopLevelKeys =
    [
        "TelemetryName", "TelemetryMode", "TelemetrySerial", "TelemetryCellsRow",
        "MetricCpuLoad", "MetricSpeedLimit", "MetricHardwarePwm", "MetricFanStatus",
        "MetricRoll", "MetricImuTemp", "MetricTorque", "MetricMotorPower", "MetricCpuTemp",
        "MetricCurrentLimit",
    ];

    /// <summary>Снимок носит поле, а ни один декодер в него не пишет — вечный прочерк, а не сигнал.
    /// «Без фильтра» — про то, что декодер отдаёт, не про всю структуру <c>SmartBms</c> (сенсей,
    /// 15.08.2026). Список — по grep, не на глаз: ни одно из имён не встречается слева от <c>=</c> в
    /// декодерах.</summary>
    private static readonly string[] DeadBmsFields =
    [
        "Status", "SerialNumber", "VersionNumber", "ActualCap", "ChargeCount", "MfgDateStr",
        "TempMos", "TempMosEnv", "Temp1Env", "Temp2Env", "Humidity1Env", "Humidity2Env",
        "BalanceMap",
    ];

    [Fact]
    public void New_bms_labels_are_wired_in_the_table_and_defined_in_strings()
    {
        string table = RepoFiles.Read(TablePath);
        string strings = RepoFiles.Read(StringsPath);

        foreach (string key in NewBmsKeys)
        {
            Assert.Contains($"AppStrings.{key}", table);
            Assert.Contains($"data name=\"{key}\"", strings);
        }
    }

    [Fact]
    public void New_top_level_labels_are_wired_in_the_table_and_defined_in_strings()
    {
        string table = RepoFiles.Read(TablePath);
        string strings = RepoFiles.Read(StringsPath);

        foreach (string key in NewTopLevelKeys)
        {
            Assert.Contains($"AppStrings.{key}", table);
            Assert.Contains($"data name=\"{key}\"", strings);
        }
    }

    /// <summary>Ни один из мёртвых полей <c>SmartBms</c> не попал в таблицу — их появление там означало
    /// бы придуманный канал вместо честного прочерка.</summary>
    [Fact]
    public void Fields_no_decoder_ever_writes_stay_off_the_screen()
    {
        string table = RepoFiles.Read(TablePath);

        foreach (string field in DeadBmsFields)
        {
            Assert.DoesNotContain($"pack(s).{field}", table);
        }
    }

    /// <summary>Каждое из перечисленных выше «мёртвых» полей и правда мёртвое: ни один декодер ему не
    /// присваивает значение. Замок на замок — если однажды декодер начнёт его писать, этот тест
    /// первым укажет, что поле пора вывести на экран.</summary>
    [Theory]
    [MemberData(nameof(DeadFieldNames))]
    public void Dead_bms_fields_are_never_assigned_by_a_decoder(string field)
    {
        string decoding = Path.Combine(RepoFiles.Root, "WheelTalk.Core", "Decoding");
        var assignments = Directory.EnumerateFiles(decoding, "*.cs")
            .SelectMany(file => File.ReadAllLines(file).Select(line => (file, line)))
            .Where(x => x.line.Contains($".{field} =", StringComparison.Ordinal));

        Assert.Empty(assignments);
    }

    public static IEnumerable<object[]> DeadFieldNames() => DeadBmsFields.Select(f => new object[] { f });
}
