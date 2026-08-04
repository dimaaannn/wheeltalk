using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Storage;

namespace WheelTalk.Lab.Data;

/// <summary>
/// Запись придуманной покатушки (<see cref="LabRideHistory"/>) в базу — общая для двух мест, откуда
/// она нужна: стенд набивает свою базу на телефоне, а Windows-консоль готовит такой же файл на
/// машине, чтобы его положили на устройство готовым.
/// <para>
/// <b>Зачем готовым.</b> Сутки записи — это четыреста тридцать тысяч отсчётов; на эмуляторе они
/// пишутся минутами, и всё это время графики пусты. На машине тот же файл делается за секунды и
/// кладётся <c>adb push</c>, а стенд получает объём, с каким работает боевое приложение.
/// </para>
/// </summary>
public static class LabHistoryFile
{
    /// <summary>
    /// Сколько отсчётов копится до сброса на диск. Не ради скорости, а ради памяти: очередь
    /// <see cref="RideStore"/> неограниченна, и сотни тысяч снимков разом легли бы в неё все.
    /// </summary>
    private const int FlushEvery = 6000;

    /// <summary>Набить покатушку в открытую базу.</summary>
    /// <param name="endsAt">Каким мгновением кончается набиваемое.</param>
    /// <param name="span">Сколько истории набить. Меньше полной — это досыпка хвоста.</param>
    public static async Task<string> FillAsync(RideStore store, DateTimeOffset endsAt, TimeSpan span, int seed)
    {
        int rows = 0;
        double topSpeed = 0;
        double lowVolts = double.MaxValue;
        double highVolts = 0;
        int lowBattery = 100;

        foreach (var (at, snapshot) in LabRideHistory.Generate(endsAt, span, seed))
        {
            store.Write(LabRideHistory.Mac, LabRideHistory.Protocol, snapshot, at);

            rows++;
            topSpeed = Math.Max(topSpeed, snapshot.SpeedKmh);
            lowVolts = Math.Min(lowVolts, snapshot.VoltageV);
            highVolts = Math.Max(highVolts, snapshot.VoltageV);
            lowBattery = Math.Min(lowBattery, snapshot.Battery);

            if (rows % FlushEvery == 0) await store.FlushAsync();
        }

        await store.FlushAsync();

        return $"Набито {span.TotalHours:0.#} ч, {rows} отсчётов, до {topSpeed:F0} км/ч, "
            + $"{lowVolts:F1}–{highVolts:F1} В, заряд до {lowBattery} %";
    }

    /// <summary>
    /// Сделать файл базы с нуля. Существующий сносится целиком: дописать поверх значило бы положить
    /// две истории на одни и те же мгновения.
    /// </summary>
    public static async Task<string> CreateAsync(string path, TimeSpan span, StorageOptions timings)
    {
        foreach (string suffix in (string[])["", "-wal", "-shm"]) File.Delete(path + suffix);

        var database = RideDatabase.Open(path, TimeProvider.System, NullLogger<RideDatabase>.Instance, timings);
        var store = new RideStore(database, TimeProvider.System, timings, NullLogger<RideStore>.Instance);

        try
        {
            return await FillAsync(store, DateTimeOffset.Now, span, Environment.TickCount);
        }
        finally
        {
            await store.DisposeAsync();
            // Иначе файл останется под пулом соединений, и скопировать его на устройство получится
            // не всегда — см. RideDatabase.CloseAllConnections.
            RideDatabase.CloseAllConnections();
        }
    }
}
