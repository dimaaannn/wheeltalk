namespace WheelTalk.Core.Logging;

/// <summary>Один сырой дамп в каталоге колеса: где лежит, сколько весит и когда в него писали.</summary>
public readonly record struct DumpFile(string Path, long Bytes, DateTimeOffset WrittenAt);

/// <summary>
/// Потолок суммарного веса сырых дампов (план 11 §4.5). Дамп выключен по умолчанию — но включивший
/// его однажды забудет, а пишется он около мегабайта на десять минут и не ротируется вовсе.
/// <para>
/// <b>Правило одно: сносится самое старое.</b> Свежий дамп не трогается никогда — за ним дамп и
/// включали, — и не трогается тот, в который прямо сейчас пишут: удалить файл из-под пишущей руки
/// значит потерять и его, и то, ради чего человек нажал кнопку.
/// </para>
/// <para>
/// Считает — здесь, удаляет — тот, у кого есть каталог (<c>RawFrameRecorder</c>): правило проверяемо
/// без телефона, а файловая система в замок не заходит.
/// </para>
/// </summary>
public static class RawDumpCap
{
    /// <param name="capBytes">Потолок суммы. Ноль или меньше — потолка нет, не сносим ничего.</param>
    /// <param name="keep">Файл, в который пишут прямо сейчас; <c>null</c> — не пишут ни в какой.</param>
    /// <returns>Что удалять, от самого старого. Пусто — сумма и так под потолком.</returns>
    public static IReadOnlyList<DumpFile> Excess(
        IEnumerable<DumpFile> files, long capBytes, string? keep = null)
    {
        var newestFirst = files.OrderByDescending(file => file.WrittenAt).ToList();
        if (capBytes <= 0 || newestFirst.Count <= 1) return [];

        long total = newestFirst.Sum(file => file.Bytes);
        var doomed = new List<DumpFile>();

        // С хвоста, то есть с самого старого, и никогда не трогая первый — свежий: один дамп
        // тяжелее потолка целиком остаётся на месте, потому что удалять его значит не убраться, а
        // отнять только что записанное.
        for (int index = newestFirst.Count - 1; index >= 1 && total > capBytes; index--)
        {
            var file = newestFirst[index];
            if (file.Path == keep) continue;

            doomed.Add(file);
            total -= file.Bytes;
        }

        return doomed;
    }
}
