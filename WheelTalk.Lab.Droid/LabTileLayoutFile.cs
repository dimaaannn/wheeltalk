using WheelTalk.Dashboard.Droid.Screen.Tiles;

namespace WheelTalk.Lab.Droid;

/// <summary>
/// Раскладка плиток стенда: тот же JSON, что у боевого приложения, но файлом — слоёв настроек у
/// стенда нет, а раскладка обязана переживать перезапуск и здесь, иначе гейт «собрал → перезапустил
/// → на месте» нечем проверять руками. Экран разницы не видит (<see cref="ITileLayoutStore"/>).
/// </summary>
public sealed class LabTileLayoutFile : ITileLayoutStore
{
    private readonly string _path = Path.Combine(LabFiles.Root, "tiles-layout.json");

    public IReadOnlyList<MetricTile>? Load()
    {
        try
        {
            return File.Exists(_path) ? TileLayoutJson.Read(File.ReadAllText(_path)) : null;
        }
        catch (IOException)
        {
            // Недочитанный файл — не повод ронять стенд: экран начнёт с зашитой раскладки.
            return null;
        }
    }

    public void Save(IReadOnlyList<MetricTile> tiles) => File.WriteAllText(_path, TileLayoutJson.Write(tiles));

    /// <summary>Сброс к зашитой раскладке — команда <c>--es layout reset</c>.</summary>
    public void Reset() => File.Delete(_path);
}
