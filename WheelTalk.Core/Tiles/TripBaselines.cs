using System.Text.Json;
using System.Text.Json.Serialization;

namespace WheelTalk.Core.Tiles;

/// <summary>
/// Точки отсчёта плиток-дистанций: с какого показания одометра каждая из них считает свой путь
/// (решение владельца 10.08.2026). Дистанция — это <c>одометр − точка</c>, и весь смысл вида в том,
/// что точку двигает <b>только человек</b>: ни смена колеса, ни новая поездка, ни перезапуск
/// приложения её не трогают.
/// <para>
/// <b>Ключ двойной — колесо и плитка.</b> Колесо — потому что одометр у каждого свой, и вернувшийся
/// к прежнему колесу продолжает прежний счёт. Плитка — потому что владелец держит их рядом
/// несколько: «с последнего ТО» и «за сегодня» считают от разных точек и сбрасываются порознь.
/// </para>
/// <para>
/// <b>Первая встреча заводит точку сама.</b> Иначе дистанция начиналась бы с полного одометра —
/// числа, которое к сегодняшнему пути отношения не имеет.
/// </para>
/// <para>
/// Хранение — строкой, и кодек здесь же: хозяин экрана (настройки приложения, файл стенда) держит
/// её, не разбирая, — тем же порядком, что и раскладку плиток. Разбор через source generator, а не
/// рефлексию: Release собирается с триммингом.
/// </para>
/// </summary>
public sealed partial class TripBaselines
{
    private readonly Dictionary<(string Wheel, string Tile), double> _points = [];

    /// <summary>
    /// Сколько раз точки менялись. Им хозяин хранилища и узнаёт, что пора записать: сравнил до и
    /// после — разошлось, значит есть что сохранять. Иначе пришлось бы либо писать на каждом кадре,
    /// либо возвращать «изменилось» из показа, где спрашивают совсем о другом.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>
    /// Путь этой плитки на этом колесе. Точки ещё нет — она заводится на нынешнем показании, и
    /// ответ выходит нулевым: счёт начинается здесь и сейчас.
    /// </summary>
    /// <param name="odometerKm">Общий пробег колеса — то, из чего дистанция и считается.</param>
    public double Since(string wheel, string tile, double odometerKm)
    {
        // Одометр, ушедший ниже точки, — это другое колесо под тем же адресом либо сброшенный
        // счётчик самого колеса. Отрицательный путь показывать нечестнее, чем начать заново.
        if (!_points.TryGetValue((wheel, tile), out double point) || odometerKm < point)
        {
            Reset(wheel, tile, odometerKm);
            return 0;
        }

        return odometerKm - point;
    }

    /// <summary>Начать счёт заново — единственное, чем точку двигают. Зовётся кнопкой «Сбросить».</summary>
    public void Reset(string wheel, string tile, double odometerKm)
    {
        _points[(wheel, tile)] = odometerKm;
        Revision++;
    }

    /// <summary>Знает ли эта пара свою точку. Нужно проверке, а показу — нет: он спрашивает <see cref="Since"/>.</summary>
    public bool Knows(string wheel, string tile) => _points.ContainsKey((wheel, tile));

    /// <summary>Сохранённое — либо пустой набор: битая строка не повод терять экран, счёт начнётся заново.</summary>
    public static TripBaselines Read(string? json)
    {
        var baselines = new TripBaselines();
        if (string.IsNullOrWhiteSpace(json)) return baselines;

        List<PointDto>? read;
        try
        {
            read = JsonSerializer.Deserialize(json, TripContext.Default.ListPointDto);
        }
        catch (JsonException)
        {
            return baselines;
        }

        foreach (var point in read ?? [])
        {
            // Запись без колеса или без плитки ничья: приложить её не к чему.
            if (point.Wheel is not { Length: > 0 } wheel || point.Tile is not { Length: > 0 } tile) continue;
            if (!double.IsFinite(point.Km)) continue;

            baselines._points[(wheel, tile)] = point.Km;
        }

        return baselines;
    }

    public string Write() =>
        JsonSerializer.Serialize(
            _points.Select(point => new PointDto
            {
                Wheel = point.Key.Wheel,
                Tile = point.Key.Tile,
                Km = point.Value,
            }).ToList(),
            TripContext.Default.ListPointDto);

    /// <summary>
    /// Запись списком, а не словарём с составным ключом: склеенный ключ пришлось бы разбирать
    /// обратно, а адрес колеса и имя плитки — чужие строки, в которых разделитель однажды окажется.
    /// </summary>
    internal sealed class PointDto
    {
        public string? Wheel { get; set; }
        public string? Tile { get; set; }
        public double Km { get; set; }
    }

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(List<PointDto>))]
    internal sealed partial class TripContext : JsonSerializerContext;
}
