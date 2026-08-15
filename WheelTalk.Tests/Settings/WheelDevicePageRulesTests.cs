using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Settings.Device;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Settings;

/// <summary>
/// Замки страницы «настройки колеса» (план 34, шаг 2.2). Описания живут в android-проекте, тестам
/// не референсном (<c>WheelTalk.Tests.csproj</c> — только Core и Storage), поэтому правила о самих
/// описаниях читаются по исходнику — приём <c>ReportedByWheelDisplayTests</c>. Всё, что можно
/// проверить настоящими данными, проверяется ими: снимок собирается из фикстуры Sherman L тем же
/// разбором, что работает в бою.
/// </summary>
public class WheelDevicePageRulesTests
{
    private const string PageFile = "WheelTalk.Droid/Settings/Catalogue/WheelDevicePage.cs";

    /// <summary>Шестнадцать строк: пятнадцать полей страницы 8 (план 34 §1.4) без
    /// <c>maxChargeVolBase</c> — его показ владелец отложил 16.08.2026 до сбора данных о смысле
    /// поля (§12.0) — плюс режим езды старых колёс, который приходит не страницей, а байтом 31
    /// кадра телеметрии (шаг 4.2; <see cref="PedalHardnessByGenerationTests"/>).</summary>
    private const int Rows = 16;

    /// <summary>
    /// (а) <b>Капкан К4.</b> Каждая строка — «сообщено колесом». Забытый признак увёл бы настройку в
    /// слои: она вернулась бы из хранилища при следующем запуске и показалась состоянием колеса,
    /// которого колесо не подтверждало. Замок держит признак не шестнадцать раз, а один — описания
    /// создаются единственной фабрикой, и мимо неё их в файле нет.
    /// </summary>
    [Fact]
    public void Every_row_of_the_page_is_reported_by_the_wheel()
    {
        string build = BuildBody();

        Assert.Equal(Rows, Regex.Matches(build, @"\bRow\(Value,").Count);
        Assert.DoesNotContain("new()", build);
        Assert.DoesNotContain("new SettingDescriptor", build);

        Assert.Contains("ReportedByWheel = true", RowBody());
    }

    /// <summary>
    /// (б) Настройки, которой у колеса нет, на экране не бывает: видимость строки — «снимок знает
    /// это поле». Правило проверено с двух сторон — условием в описании и настоящим кадром, где
    /// одно из пятнадцати полей страницы 8 пришло сентинелом.
    /// </summary>
    [Fact]
    public void A_row_is_hidden_when_the_wheel_kept_silent_about_the_field()
    {
        Assert.Contains("IsVisible = () => value(field).Supported", RowBody());

        var snapshot = ShermanL();

        Assert.False(snapshot[WheelSettingKeys.BrakePressureAlarm].Supported);
        foreach (string key in SnapshotKeys().Where(key => key != WheelSettingKeys.BrakePressureAlarm))
        {
            Assert.True(snapshot[key].Supported, $"{key}: колесо сообщило значение, строка должна быть видна");
        }
    }

    /// <summary>
    /// (в) Значение строки — значение снимка, а не наша копия его. Ключ описания собран из имени
    /// поля протокола, поэтому ключ экрана и ключ снимка разойтись не могут: это одна строка.
    /// </summary>
    [Fact]
    public void The_value_of_a_row_is_read_from_the_snapshot()
    {
        Assert.Contains("Current = () => Text(kind, value(field), decimals)", RowBody());
        Assert.Contains("Key = KeyPrefix + field", RowBody());

        Assert.Equal(Rows, PageKeys().Count);

        var snapshot = ShermanL();
        foreach (string key in SnapshotKeys()) Assert.True(snapshot.Values.ContainsKey(key), $"снимок не знает поля {key}");

        // Единственное значение страницы не из снимка: режим езды приходит кадром телеметрии, и
        // страницы 8 у колёс, которым эта строка нужна, не бывает вовсе (шаг 4.2).
        Assert.DoesNotContain(WheelSettingKeys.RideMode, snapshot.Values.Keys);

        // Столбец «Sherman L» раскладки §1.4 — те самые числа, что увидит человек на реплее.
        Assert.Equal(94, snapshot[WheelSettingKeys.PedalHardness].Value);
        Assert.Equal(200, snapshot[WheelSettingKeys.StopSpeed].Value);
        Assert.Equal(65, snapshot[WheelSettingKeys.MaxChargeVol].Value);
    }

    /// <summary>
    /// (г) <c>maxChargeVolBase</c> (145) — не настройка, а опора расчёта, и владелец отложил её показ
    /// до сбора данных. Поле в снимке есть, строки на экране нет — замок стережёт именно это.
    /// </summary>
    [Fact]
    public void The_charge_voltage_base_is_read_but_not_shown()
    {
        Assert.True(ShermanL()[WheelSettingKeys.MaxChargeVolBase].Supported);
        Assert.DoesNotContain(WheelSettingKeys.MaxChargeVolBase, PageKeys());
    }

    /// <summary>
    /// (д) Ни одной подписи в коде: экранный текст живёт только в ресурсах, и каждый названный
    /// описанием ключ там есть. Ключ, которого в словаре нет, — пустая строка на экране, и заметить
    /// её пропажу нечем.
    /// </summary>
    [Fact]
    public void No_label_is_written_into_the_code()
    {
        string code = BuildBody() + RowBody();
        string words = RepoFiles.Read("WheelTalk.Droid/Resources/Strings/AppStrings.resx");

        var literals = Regex.Matches(code, "\"([^\"\r\n]*)\"").Select(match => match.Groups[1].Value).ToList();
        Assert.NotEmpty(literals);

        foreach (string literal in literals)
        {
            Assert.DoesNotMatch("[А-Яа-яЁё]", literal);

            if (Regex.IsMatch(literal, "^(Setting|Section|Unit)[A-Za-z]+$"))
            {
                Assert.Contains($"name=\"{literal}\"", words);
            }
        }

        // Подписи, которые видит человек, — по одной на строку плюс подписи вариантов единиц.
        Assert.Equal(Rows + 2, literals.Count(literal => literal.StartsWith("SettingWheelDevice", StringComparison.Ordinal)
            && !literal.EndsWith("Hint", StringComparison.Ordinal)));
    }

    /// <summary>
    /// (е) Диапазоны — из каталога производителя (план 34 §1.4), а не из вида ползунка. Проверяются
    /// те, которые невозможно угадать: они и есть цена ошибки, когда дело дойдёт до записи.
    /// </summary>
    [Fact]
    public void Ranges_and_units_come_from_the_manufacturers_catalogue()
    {
        Assert.Contains("min: 10, max: 120", Row(nameof(WheelSettingKeys.StopSpeed)));
        Assert.Contains("UnitKmh", Row(nameof(WheelSettingKeys.StopSpeed)));
        Assert.Contains("min: 30, max: 100", Row(nameof(WheelSettingKeys.StopPowerRate)));
        Assert.Contains("min: 80, max: 125", Row(nameof(WheelSettingKeys.BrakePressureAlarm)));
        Assert.Contains("max: 120", Row(nameof(WheelSettingKeys.MaxChargeVol)));
        Assert.Contains("UnitVolts", Row(nameof(WheelSettingKeys.MaxChargeVol)));
        Assert.Contains("max: 2", Row(nameof(WheelSettingKeys.Gyro)));

        // Единственное знаковое поле страницы (капкан К1) и единственное с масштабом: колесо
        // пакует десятые доли, справочник говорит о делении на десять дважды (§14.1 и §7), и на
        // экране это от −1,5 до 1,5 %. Масштаб — часть чтения значения, а не толкование смысла.
        Assert.Contains("min: -1.5, max: 1.5", Row(nameof(WheelSettingKeys.Vol)));
        Assert.Contains("unit: \"UnitPercent\", decimals: 1", Row(nameof(WheelSettingKeys.Vol)));
    }

    /// <summary>
    /// (ж) Названия — не наши и не переведённые: на экране стоит дословный текст родного
    /// приложения (слово владельца, план 34 §4 и §12.0 п. 5). Райдер знает свои настройки по
    /// родному приложению, и даже точный перевод заставил бы его гадать, что чему отвечает.
    /// Замок читает словарь: у подписей этой страницы нет кириллицы, и каждая находится в
    /// справочнике, откуда снята. Подсказки — наши слова о чужой настройке, они по-русски и под
    /// это правило не идут.
    /// </summary>
    [Fact]
    public void The_labels_are_the_manufacturers_own_words()
    {
        string words = RepoFiles.Read("WheelTalk.Droid/Resources/Strings/AppStrings.resx");
        string reference = RepoFiles.Read("docs/originals-reference-data.md");

        var labels = Regex.Matches(words, "name=\"(SettingWheelDevice\\w+)\"[^>]*><value>([^<]*)<")
            .Where(match => !match.Groups[1].Value.EndsWith("Hint", StringComparison.Ordinal))
            .Select(match => match.Groups[2].Value)
            .ToList();

        // Пятнадцать подписей строк и две подписи вариантов единиц.
        Assert.Equal(Rows + 2, labels.Count);

        foreach (string label in labels)
        {
            Assert.DoesNotMatch("[А-Яа-яЁё]", label);

            // Две поправки на форму записи, обе названные: в справочнике «|» экранирована
            // разметкой таблицы, а у одной подписи исправлена опечатка самого родного приложения —
            // «assistt» с лишней «t» (§14.1, правка по слову владельца 16.08.2026).
            string inReference = label.Replace("|", @"\|").Replace("deceleration assist", "deceleration assistt");
            Assert.Contains(inReference, reference);
        }
    }

    private static string BuildBody() => RepoFiles.MethodBody(
        RepoFiles.Read(PageFile), "public static IReadOnlyList<SettingDescriptor> Build(");

    private static string RowBody() => RepoFiles.MethodBody(
        RepoFiles.Read(PageFile), "private static SettingDescriptor Row(");

    /// <summary>Кусок исходника одной строки — от её поля до следующего вызова фабрики.</summary>
    private static string Row(string field)
    {
        string build = BuildBody();
        int at = build.IndexOf($"WheelSettingKeys.{field},", StringComparison.Ordinal);
        Assert.True(at > 0, $"на странице нет строки для {field}");

        int next = build.IndexOf("Row(Value,", at, StringComparison.Ordinal);
        return next < 0 ? build[at..] : build[at..next];
    }

    /// <summary>Поля страницы 8: всё, что страница показывает, кроме режима езды, — он не оттуда.</summary>
    private static IEnumerable<string> SnapshotKeys() =>
        PageKeys().Where(key => key != WheelSettingKeys.RideMode);

    /// <summary>Поля протокола, которые страница показывает, — именами из <see cref="WheelSettingKeys"/>.</summary>
    private static IReadOnlyList<string> PageKeys() =>
        Regex.Matches(BuildBody(), @"WheelSettingKeys\.(\w+)")
            .Select(match => (string)typeof(WheelSettingKeys)
                .GetField(match.Groups[1].Value)!
                .GetValue(null)!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Настоящий снимок: первый кадр страницы 8 из записи Sherman L 28.07.2026.</summary>
    private static WheelSettingsSnapshot ShermanL()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "shermanl_raw_ride_20260728.csv");

        var unpacker = new VeteranUnpacker(NullLogger<VeteranDecoder>.Instance);
        foreach (string line in File.ReadLines(path))
        {
            int comma = line.IndexOf(',');
            if (comma < 0) continue;

            foreach (byte b in Convert.FromHexString(line[(comma + 1)..].Trim()))
            {
                if (!unpacker.AddChar(b)) continue;

                byte[] frame = unpacker.GetBuffer();
                if (frame.Length > 46 && frame[46] == VeteranSettingsPage.PageNumber
                    && VeteranSettingsPage.Parse(frame, DateTimeOffset.UnixEpoch) is { } snapshot)
                {
                    return snapshot;
                }
            }
        }

        throw new InvalidOperationException("В фикстуре нет ни одного кадра страницы настроек.");
    }
}
