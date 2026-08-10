using System.Text.Json;
using System.Text.Json.Serialization;
using WheelTalk.Core.Metrics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Раскладка плиток ↔ JSON (план 23 §3.4): одна строка в одну настройку, свой таблицы нет. Кодек
/// один на оба хранилища — настройку боевого приложения и файл стенда, — чтобы раскладка, собранная
/// на одном, читалась другим без перевода.
/// <para>
/// <b>Формат обязан пережить правки приложения.</b> Поэтому здесь свои DTO со строковыми видами, а
/// не сериализация <see cref="MetricTile"/> как есть: имена рекордов — деталь кода, а формат —
/// контракт с прошлой версией. Правила чтения: незнакомое поле игнорируется, отсутствующее берёт
/// умолчание, плитка с мусором (незнакомый вид, размер вне сетки) отбрасывается молча — тем же
/// правилом, каким адаптер отбрасывает плитку с неизвестной величиной. Незнакомое значение
/// известной опции (сторона прореживания) — не мусор, а чужая новизна: берётся умолчание, плитка
/// живёт.
/// </para>
/// <para>
/// Сериализация через source generator, а не рефлексию: Release собирается с триммингом, и
/// рефлексивный разбор — это предупреждения при сборке сегодня и тихий отказ на устройстве завтра.
/// </para>
/// </summary>
public static partial class TileLayoutJson
{
    public static string Write(IReadOnlyList<MetricTile> tiles) =>
        JsonSerializer.Serialize(
            tiles.Select(ToDto).ToList(), TileLayoutContext.Default.ListTileDto);

    /// <summary><c>null</c> — сохранённого нет либо строка не разобралась целиком; звать умолчание.</summary>
    public static IReadOnlyList<MetricTile>? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        List<TileDto>? read;
        try
        {
            read = JsonSerializer.Deserialize(json, TileLayoutContext.Default.ListTileDto);
        }
        catch (JsonException)
        {
            // Битая строка — не повод ронять экран: раскладка вернётся к зашитой.
            return null;
        }

        if (read is null) return null;

        // Пустой список — не мусор, а «человек убрал все плитки»: после перезапуска он должен
        // увидеть то же пустое поле, а не воскресшую зашитую раскладку.
        var tiles = new List<MetricTile>(read.Count);
        foreach (var dto in read)
        {
            if (ToTile(dto) is { } tile) tiles.Add(tile);
        }

        return tiles;
    }

    private static TileDto ToDto(MetricTile tile) => new()
    {
        Id = tile.Id,
        Caption = tile.Caption.Length > 0 ? tile.Caption : null,
        Kind = tile.Kind switch
        {
            TileKind.Value => "value",
            TileKind.Chart => "chart",
            TileKind.Extremum => "extremum",
            TileKind.Trip => "trip",
            TileKind.Empty => "empty",
            // Новый вид без строки формата должен упасть у разработчика при первом же сохранении,
            // а не молча записаться числом, которое прошлая версия прочтёт как мусор.
            _ => throw new ArgumentOutOfRangeException(nameof(tile), tile.Kind, "вид без имени в формате"),
        },
        Metric = tile.Kind == TileKind.Empty ? null : tile.MetricId,
        Columns = tile.Size.Columns,
        Rows = tile.Size.Rows,
        Label = tile.ShowLabel,
        HeatBar = tile.ShowHeatBar,
        Decimals = tile.Decimals,
        Chart = tile.Chart is { } chart
            ? new ChartDto
            {
                WindowSeconds = (int)chart.Window.TotalSeconds,
                ShowValue = chart.ShowValue,
                Zoom = chart.Zoom,
                Fill = chart.Fill,
                Axis = chart.Axis,
                Smoothing = chart.Smoothing switch
                {
                    ChartSmoothing.Peaks => "peaks",
                    ChartSmoothing.Dips => "dips",
                    _ => "minmax",
                },
            }
            : null,
        Limits = tile.Limits is { } limits
            ? new LimitsDto { Warn = limits.Warn, Danger = limits.Danger, Falling = !limits.Rising }
            : null,
        // Само накопленное крайнее не хранится намеренно: план 23 §3.2 держит его в памяти, и
        // сохранённый вчерашний максимум сегодня был бы не показанием, а привидением.
        Lowest = tile.Extremum?.Lowest ?? false,
    };

    private static MetricTile? ToTile(TileDto dto)
    {
        // Размер вне сетки — мусор, показать такую плитку нечем. Потолок строк — как у колонок:
        // он защищает экран от битой строки, а не ограничивает набор размеров меню.
        if (dto.Columns < 1 || dto.Columns > TilesLayout.Columns) return null;
        if (dto.Rows < 1 || dto.Rows > TilesLayout.Columns) return null;

        var size = new TileSize(dto.Columns, dto.Rows);

        // Число знаков вне предложенного — та же «чужая новизна», что и незнакомая сторона
        // прореживания: плитка живёт, округление берётся от величины.
        int? decimals = MetricRounding.Chosen(dto.Decimals);

        // Имя плитки и своя подпись — общие для всех видов, поэтому берутся один раз. Пустое имя
        // здесь не рождается: раскладке без имён их раздаёт экран и тут же сохраняет — так они
        // переживут перезапуск вместе с точками отсчёта дистанций.
        string caption = dto.Caption ?? "";
        string id = dto.Id ?? "";

        switch (dto.Kind)
        {
            case "empty":
                return MetricTile.Empty(size) with { Id = id };

            case "value" when dto.Metric is { Length: > 0 }:
                return new MetricTile(dto.Metric, TileKind.Value, size, dto.Label,
                    Limits: ToLimits(dto.Limits), ShowHeatBar: dto.HeatBar, Decimals: decimals,
                    Caption: caption) { Id = id };

            case "chart" when dto.Metric is { Length: > 0 }:
                return new MetricTile(dto.Metric, TileKind.Chart, size, dto.Label,
                    ToChart(dto.Chart), ToLimits(dto.Limits), ShowHeatBar: dto.HeatBar,
                    Decimals: decimals, Caption: caption) { Id = id };

            case "extremum" when dto.Metric is { Length: > 0 }:
                return new MetricTile(dto.Metric, TileKind.Extremum, size, dto.Label,
                    Limits: ToLimits(dto.Limits), Extremum: new TileExtremum(dto.Lowest),
                    ShowHeatBar: dto.HeatBar, Decimals: decimals, Caption: caption) { Id = id };

            case "trip" when dto.Metric is { Length: > 0 }:
                return new MetricTile(dto.Metric, TileKind.Trip, size, dto.Label,
                    Limits: ToLimits(dto.Limits), ShowHeatBar: dto.HeatBar, Decimals: decimals,
                    Caption: caption) { Id = id };

            default:
                // Незнакомый вид или величина без имени: строить нечего.
                return null;
        }
    }

    /// <summary>Свойства графика. Битое окно равносильно их отсутствию: умолчания подставит показ.</summary>
    private static TileChart? ToChart(ChartDto? dto) => dto is { WindowSeconds: > 0 }
        ? new TileChart(TimeSpan.FromSeconds(dto.WindowSeconds), dto.ShowValue, dto.Zoom, dto.Fill, dto.Axis,
            dto.Smoothing switch
            {
                "peaks" => ChartSmoothing.Peaks,
                "dips" => ChartSmoothing.Dips,
                _ => ChartSmoothing.MinMax,
            })
        : null;

    /// <summary>Свои пороги плитки. Не-числа — как отсутствие: пороги возьмутся из настроек тревог.</summary>
    private static TileLimits? ToLimits(LimitsDto? dto) =>
        dto is not null && double.IsFinite(dto.Warn) && double.IsFinite(dto.Danger)
            ? new TileLimits(dto.Warn, dto.Danger, Rising: !dto.Falling)
            : null;

    internal sealed class TileDto
    {
        /// <summary>
        /// Устойчивое имя плитки. Отсутствует у раскладок, собранных до этого поля, — и тогда его
        /// раздаёт экран при чтении. Позицией его не заменить: перетаскивание её меняет, а точка
        /// отсчёта дистанции обязана остаться при своей плитке.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Своя подпись плитки. Пусто либо нет поля — имя величины, как было до этого правила: две
        /// дистанции по одному одометру различает только слово хозяина.
        /// </summary>
        public string? Caption { get; set; }

        public string? Kind { get; set; }
        public string? Metric { get; set; }
        public int Columns { get; set; }
        public int Rows { get; set; }
        public bool Label { get; set; } = true;

        /// <summary>
        /// Полоска жара по низу плитки. <b>Умолчание — включена</b>, и это не вкус: старые
        /// сохранённые раскладки поля не содержат вовсе, а прочитаться обязаны так, как выглядели
        /// до правки.
        /// </summary>
        public bool HeatBar { get; set; } = true;

        /// <summary>
        /// Своё число знаков после запятой. <b>Поле необязательное, и его отсутствие — не ноль, а
        /// «по умолчанию»</b>: старые раскладки поля не содержат вовсе, а ноль тут означал бы
        /// «целыми» и молча огрубил бы им все числа. Отсюда <c>int?</c>, а не <c>int</c> с
        /// умолчанием, — у нуля в этом поле есть свой смысл, и отличить его от «не задано» умеет
        /// только пустота.
        /// </summary>
        public int? Decimals { get; set; }
        public ChartDto? Chart { get; set; }
        public LimitsDto? Limits { get; set; }

        /// <summary>Какой край помнит плитка крайнего значения. У прочих видов не читается.</summary>
        public bool Lowest { get; set; }
    }

    internal sealed class ChartDto
    {
        public int WindowSeconds { get; set; }
        public bool ShowValue { get; set; }
        public bool Zoom { get; set; }
        public bool Fill { get; set; } = true;
        public bool Axis { get; set; } = true;
        public string? Smoothing { get; set; }
    }

    internal sealed class LimitsDto
    {
        public double Warn { get; set; }
        public double Danger { get; set; }
        public bool Falling { get; set; }
    }

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(List<TileDto>))]
    internal sealed partial class TileLayoutContext : JsonSerializerContext;
}
