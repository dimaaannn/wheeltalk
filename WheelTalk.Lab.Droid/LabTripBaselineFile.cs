using WheelTalk.Dashboard.Droid.Screen.Tiles;

namespace WheelTalk.Lab.Droid;

/// <summary>
/// Точки отсчёта дистанций на стенде — файлом, как и раскладка (<see cref="LabTileLayoutFile"/>):
/// слоёв настроек у стенда нет, а гейт «сбросил → перезапустил → счёт с нуля, а не с одометра»
/// иначе нечем проверить руками.
/// </summary>
public sealed class LabTripBaselineFile : ITripBaselineStore
{
    private readonly string _path = Path.Combine(LabFiles.Root, "tiles-trips.json");

    public string? Load()
    {
        try
        {
            return File.Exists(_path) ? File.ReadAllText(_path) : null;
        }
        catch (IOException)
        {
            // Недочитанный файл — не повод ронять стенд: точки заведутся заново.
            return null;
        }
    }

    public void Save(string json) => File.WriteAllText(_path, json);
}
