using Microsoft.Data.Sqlite;
using WheelTalk.Core.Metrics;

namespace WheelTalk.Storage;

/// <summary>
/// Читатель истории одной величины для плитки-графика (план 23 §5.6). Отдельно от
/// <see cref="RideExporter"/> намеренно: тот тянет поездку целиком всеми колонками — это про
/// экспорт, а не про плитку шириной в двести точек.
/// <para>
/// <b>Прореживание корзинами, минимум и максимум на корзину.</b> Единственное, что стоит между
/// графиком и полутора миллионами строк: пять отсчётов в секунду за сутки — это четыреста тысяч
/// строк на величину. Брать каждую N-ю нельзя — так теряется пик ШИМ, а он и есть то, ради чего
/// график смотрят.
/// </para>
/// <para>
/// Наружу уходят точки, а не курсор: соединение с SQLite не покидает <c>WheelTalk.Storage</c>
/// (AGENTS.md, «Как писать код здесь»). Работа идёт на пуле — вызывающий ждёт её <c>await</c>'ом с
/// потока отрисовки.
/// </para>
/// </summary>
public sealed class MetricHistoryReader : IMetricHistory
{
    private static readonly IReadOnlyList<MetricPoint> Nothing = [];

    private readonly RideDatabase _database;
    private readonly Func<string> _wheelMac;

    /// <param name="wheelMac">
    /// Чей поток читать. Делегатом, а не строкой: колесо меняется в живом приложении, а читатель
    /// один. Пустой MAC — колеса нет, и истории тоже.
    /// </param>
    public MetricHistoryReader(RideDatabase database, Func<string> wheelMac)
    {
        _database = database;
        _wheelMac = wheelMac;
    }

    public Task<IReadOnlyList<MetricPoint>> ReadAsync(
        string metricId, DateTimeOffset from, DateTimeOffset to, int points, CancellationToken cancel)
    {
        // Величина без колонки — «живьём есть, графика нет» (план 23 §3.2). Пусто, а не исключение:
        // отказ на входе даёт экран, который график по такой величине не предлагает вовсе.
        if (MetricCatalogue.Find(metricId) is not { Column: { } column } metric) return Task.FromResult(Nothing);

        long fromMs = from.ToUnixTimeMilliseconds();
        long toMs = to.ToUnixTimeMilliseconds();
        string mac = _wheelMac();

        if (points <= 0 || toMs <= fromMs || mac.Length == 0) return Task.FromResult(Nothing);

        return Task.Run(() => Read(column, metric.ColumnScale, mac, fromMs, toMs, points, cancel), cancel);
    }

    private IReadOnlyList<MetricPoint> Read(
        string column, double scale, string mac, long fromMs, long toMs, int points, CancellationToken cancel)
    {
        long bucket = Math.Max(1, (toMs - fromMs) / points);

        using var connection = _database.Connect();
        using var command = connection.CreateCommand();

        // Имя колонки подставляется в текст запроса, потому что параметром имя не бывает. Это не
        // дыра: строка приходит из MetricCatalogue — замкнутого списка в коде, — а не снаружи.
        command.CommandText =
            $"""
             SELECT at / $bucket AS box, MIN({column}), MAX({column})
               FROM telemetry
              WHERE wheel_id = (SELECT id FROM wheel WHERE mac = $mac)
                AND at BETWEEN $from AND $to
                AND {column} IS NOT NULL
              GROUP BY box
              ORDER BY box;
             """;
        command.Parameters.AddWithValue("$bucket", bucket);
        command.Parameters.AddWithValue("$mac", mac);
        command.Parameters.AddWithValue("$from", fromMs);
        command.Parameters.AddWithValue("$to", toMs);

        var result = new List<MetricPoint>(points * 2);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancel.ThrowIfCancellationRequested();

            long start = Math.Max(fromMs, reader.GetInt64(0) * bucket);
            double low = reader.GetDouble(1) * scale;
            double high = reader.GetDouble(2) * scale;

            // Минимум в начале корзины, максимум в её середине. Точное время каждой из двух строк
            // база отдать одним запросом не может, а ошибка здесь — половина корзины, то есть
            // полпикселя графика. Порядок по времени при этом сохраняется — рисующему только это и
            // нужно.
            result.Add(new MetricPoint(start, low));
            if (high > low) result.Add(new MetricPoint(start + bucket / 2, high));
        }

        return result;
    }
}
