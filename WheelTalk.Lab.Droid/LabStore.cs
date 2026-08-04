using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Metrics;
using WheelTalk.Lab.Data;
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
    /// Насколько свежей должна доходить история, чтобы её не перекладывали заново. Пять минут — самое
    /// короткое окно графика (<c>TilesLayout.ChartWindows</c>): дошла история до него — дойдёт и до
    /// любого другого.
    /// </summary>
    private static readonly TimeSpan FreshWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Стендовые сроки: коммит <b>реже</b> боевого, потому что набивка гонит пятьдесят тысяч строк
    /// подряд, а не пять в секунду. При коммите раз в 100 мс те же строки ложились двумя с половиной
    /// сотнями транзакций, и на эмуляторе это двадцать пять секунд ожидания — по секунде их
    /// вдесятеро меньше. Срок хранения и порог брошенной поездки остаются боевыми: стенд обязан жить
    /// по тем же правилам, иначе проверяет не то.
    /// </summary>
    private static readonly StorageOptions Timings = new() { CommitInterval = TimeSpan.FromSeconds(1) };

    private readonly string _path = Path.Combine(LabFiles.Root, "lab-telemetry.db");

    private RideDatabase _database = null!;
    private RideStore _rides = null!;
    private string _summary = "";

    /// <summary>Читатель истории — тот же самый, каким её читает приложение.</summary>
    public IMetricHistory History { get; private set; } = null!;

    /// <summary>
    /// Открыть базу и, если история устарела или её нет вовсе, набить заново. Устаревает она просто
    /// от времени: набитая в обед доходит до обеда, а к вечеру график за последние минуты по ней
    /// пуст — и это читается как «графики не работают», хотя работают они правильно.
    /// </summary>
    public async Task<string> OpenAsync()
    {
        await Task.Run(Open);

        if (await EndOfHistoryAsync() is not { } end) return await RefillAsync();
        if (DateTimeOffset.Now - end < FreshWindow) return _summary = $"История на месте: {_path}";

        // Досыпается только разрыв: перекладка всех пятидесяти четырёх тысяч отсчётов идёт двадцать
        // пять секунд, и всё это время график пуст — последние минуты пишутся последними. После
        // короткой отлучки дописать надо минуты, и это доли секунды.
        return await TopUpAsync(end);
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
        return _summary = await LabHistoryFile.FillAsync(_rides, DateTimeOffset.Now, HistorySpan, Environment.TickCount);
    }

    public async ValueTask DisposeAsync() => await _rides.DisposeAsync();

    private void Open()
    {
        _database = RideDatabase.Open(_path, TimeProvider.System, NullLogger<RideDatabase>.Instance, Timings);
        _rides = new RideStore(_database, TimeProvider.System, Timings, NullLogger<RideStore>.Instance);
        History = new MetricHistoryReader(_database, () => LabRideHistory.Mac);
    }

    /// <summary>
    /// Когда кончается набитое. Годность истории мерится не тем, есть ли она вообще, а тем,
    /// <b>доходит ли она до сейчас</b>: набитая утром покатушка попадает в суточный срок хранения и
    /// базу заполняет, но график за последние минуты по ней пуст — а это самое частое окно, с
    /// которым на стенд и смотрят.
    /// <para>
    /// <c>null</c> — истории нет вовсе либо она старше, чем стенд вообще набивает: досыпать не к
    /// чему, надо перекладывать.
    /// </para>
    /// </summary>
    private async Task<DateTimeOffset?> EndOfHistoryAsync()
    {
        var now = DateTimeOffset.Now;
        // Корзина в минуту: точнее знать конец незачем — досыпается он с той же частотой, что и
        // остальная история.
        var points = await History.ReadAsync(
            "speed", now - HistorySpan, now, (int)HistorySpan.TotalMinutes, CancellationToken.None);

        return points.Count == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(points[^1].AtMs);
    }

    /// <summary>Дописать покатушку от конца набитого до сейчас — тем же генератором, что и всю её.</summary>
    private async Task<string> TopUpAsync(DateTimeOffset from)
    {
        var now = DateTimeOffset.Now;
        string added = await LabHistoryFile.FillAsync(_rides, now, now - from, Environment.TickCount);

        return _summary = $"История продолжена на {(now - from).TotalMinutes:F0} мин. {added}";
    }
}
