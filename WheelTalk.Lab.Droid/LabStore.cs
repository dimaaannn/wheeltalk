using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Metrics;
using WheelTalk.Lab.Droid.Scenarios;
using WheelTalk.Storage;

namespace WheelTalk.Lab.Droid;

/// <summary>
/// База стенда: своя, отдельная от боевой, с придуманной покатушкой внутри
/// (<see cref="LabRideHistory"/>). Заведена решением владельца 04.08.2026 — <b>стенд показывает то,
/// что читается из базы</b>, и со своим генератором точек он проверял бы путь, которого в бою нет:
/// график по выдуманному массиву и график по прореженной выборке из SQLite разошлись бы молча.
/// <para>
/// Наружу отсюда идут ровно два узких типа хранилища — <see cref="RideStore"/> на запись и
/// <see cref="IMetricHistory"/> на чтение. Ни соединения, ни SQL в стенде нет и быть не должно
/// (AGENTS.md, «Как писать код здесь»): понадобился запрос — заводится тип в
/// <c>WheelTalk.Storage</c>.
/// </para>
/// <para>
/// BLE и настройки приложения стенду по-прежнему не нужны: пересмотрена одна треть прежнего решения,
/// не всё оно.
/// </para>
/// </summary>
public sealed class LabStore : IAsyncDisposable
{
    /// <summary>
    /// Сколько истории набивается. Три часа — чтобы на графике были и часовой масштаб, и минутный, а
    /// суточный срок хранения (<see cref="StorageOptions.TelemetryRetention"/>) её не съел до
    /// следующего запуска.
    /// </summary>
    public static readonly TimeSpan HistorySpan = TimeSpan.FromHours(3);

    /// <summary>
    /// Стендовые сроки: коммит чаще боевого, потому что набивка гонит пятьдесят тысяч строк подряд, а
    /// не пять в секунду. Срок хранения и порог брошенной поездки — боевые: стенд обязан жить по тем
    /// же правилам, иначе проверяет не то.
    /// </summary>
    private static readonly StorageOptions Timings = new() { CommitInterval = TimeSpan.FromMilliseconds(100) };

    /// <summary>
    /// Сколько отсчётов копится до сброса на диск. Не ради скорости, а ради памяти: очередь
    /// <see cref="RideStore"/> неограниченна, и пятьдесят тысяч снимков разом легли бы в неё все.
    /// </summary>
    private const int FlushEvery = 6000;

    private readonly string _path = Path.Combine(LabFiles.Root, "lab-telemetry.db");

    private RideDatabase _database = null!;
    private RideStore _rides = null!;
    private string _summary = "";

    /// <summary>Читатель истории — тот же самый, каким её читает приложение.</summary>
    public IMetricHistory History { get; private set; } = null!;

    /// <summary>
    /// Открыть базу и, если истории в ней нет, набить. Пустой она бывает при первом запуске и после
    /// того, как срок хранения съел набитое в прошлый раз.
    /// </summary>
    public async Task<string> OpenAsync()
    {
        await Task.Run(Open);

        if (await HasHistoryAsync()) return _summary = $"История на месте: {_path}";

        return await FillAsync(Environment.TickCount);
    }

    /// <summary>
    /// Набить заново — другой покатушкой. Файл сносится целиком, потому что дописать поверх значило
    /// бы положить две истории на одни и те же мгновения; удаление же строк — это запрос к базе, а
    /// запросам место в <c>WheelTalk.Storage</c>, не здесь.
    /// </summary>
    public async Task<string> RefillAsync()
    {
        await _rides.DisposeAsync();
        // Иначе файл исчезнет из каталога, а пул соединений продолжит писать в него же — см.
        // RideDatabase.CloseAllConnections.
        RideDatabase.CloseAllConnections();

        foreach (string suffix in (string[])["", "-wal", "-shm"]) File.Delete(_path + suffix);

        await Task.Run(Open);
        return await FillAsync(Environment.TickCount);
    }

    public async ValueTask DisposeAsync() => await _rides.DisposeAsync();

    private void Open()
    {
        _database = RideDatabase.Open(_path, TimeProvider.System, NullLogger<RideDatabase>.Instance, Timings);
        _rides = new RideStore(_database, TimeProvider.System, Timings, NullLogger<RideStore>.Instance);
        History = new MetricHistoryReader(_database, () => LabRideHistory.Mac);
    }

    private async Task<bool> HasHistoryAsync()
    {
        var points = await History.ReadAsync(
            "speed", DateTimeOffset.Now - Timings.TelemetryRetention, DateTimeOffset.Now, 8, CancellationToken.None);

        return points.Count > 0;
    }

    private async Task<string> FillAsync(int seed)
    {
        int rows = 0;
        double topSpeed = 0;
        double lowVolts = double.MaxValue;
        double highVolts = 0;
        int lowBattery = 100;

        foreach (var (at, snapshot) in LabRideHistory.Generate(DateTimeOffset.Now, HistorySpan, seed))
        {
            _rides.Write(LabRideHistory.Mac, LabRideHistory.Protocol, snapshot, at);

            rows++;
            topSpeed = Math.Max(topSpeed, snapshot.SpeedKmh);
            lowVolts = Math.Min(lowVolts, snapshot.VoltageV);
            highVolts = Math.Max(highVolts, snapshot.VoltageV);
            lowBattery = Math.Min(lowBattery, snapshot.Battery);

            if (rows % FlushEvery == 0) await _rides.FlushAsync();
        }

        await _rides.FlushAsync();

        return _summary = $"История набита: {HistorySpan.TotalHours:0.#} ч, {rows} отсчётов, "
            + $"до {topSpeed:F0} км/ч, {lowVolts:F1}–{highVolts:F1} В, заряд до {lowBattery} %";
    }
}
