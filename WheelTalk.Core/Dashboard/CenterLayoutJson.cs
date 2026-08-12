using System.Text.Json;
using System.Text.Json.Serialization;

namespace WheelTalk.Core.Dashboard;

/// <summary>
/// Состав центра — строкой JSON, тем же порядком, каким хранится раскладка плиток: своей таблицы не
/// заводим, атомарность и слои у настроек уже есть (план 23 §3.4).
/// <para>
/// <b>Имён у строк нет.</b> Плитке имя нужно — по нему хранится её точка отсчёта дистанции; строке
/// центра хранить нечего, она целиком описывается величиной и стороной. Меньше полей — меньше
/// поводов для несовместимости.
/// </para>
/// <para>
/// Читается <b>терпимо</b>: битая строка, незнакомая сторона, пустая величина — это не повод
/// потерять весь состав. Негодная строка выбрасывается, негодный файл читается как «состава нет»,
/// и человек видит умолчание, а не пустой центр.
/// </para>
/// </summary>
public static class CenterLayoutJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Форма записи: величина и сторона, при паре — второе показание теми же двумя полями.</summary>
    private sealed class RowDto
    {
        public string? Metric { get; set; }
        public string? Aspect { get; set; }
        public string? With { get; set; }
        public string? WithAspect { get; set; }
    }

    /// <summary><c>null</c> — сохранённого состава нет либо он не читается; берётся умолчание.</summary>
    public static IReadOnlyList<CenterRow>? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var dto = JsonSerializer.Deserialize<List<RowDto>>(json, Options);
            if (dto is null) return null;

            var rows = dto
                .Where(row => !string.IsNullOrWhiteSpace(row.Metric))
                .Select(row => new CenterRow(
                    new CenterReading(row.Metric!, Aspect(row.Aspect)),
                    string.IsNullOrWhiteSpace(row.With)
                        ? null
                        : new CenterReading(row.With!, Aspect(row.WithAspect))))
                .Take(CenterLayout.MaxRows)
                .ToList();

            return rows;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Write(IEnumerable<CenterRow> rows) => JsonSerializer.Serialize(
        rows.Select(row => new RowDto
        {
            Metric = row.First.Metric,
            Aspect = Word(row.First.Aspect),
            With = row.Second?.Metric,
            WithAspect = row.Second is { } second ? Word(second.Aspect) : null,
        }),
        Options);

    /// <summary>Сторона словом, а не числом: перенумеруй перечисление — и чужой состав прочтётся навыворот.</summary>
    private static string Word(CenterAspect aspect) => aspect switch
    {
        CenterAspect.Max => "max",
        CenterAspect.Min => "min",
        _ => "current",
    };

    private static CenterAspect Aspect(string? word) => word switch
    {
        "max" => CenterAspect.Max,
        "min" => CenterAspect.Min,
        _ => CenterAspect.Current,
    };
}
