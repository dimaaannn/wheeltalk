using WheelTalk.Core.Dashboard;
using WheelTalk.Dashboard.Droid.Screen;

namespace WheelTalk.Lab.Droid;

/// <summary>
/// Состав справочного блока центра на стенде: тот же JSON, что у боевого приложения, но файлом —
/// слоёв настроек у стенда нет, а собранный состав обязан переживать перезапуск и здесь, иначе гейт
/// «собрал → перезапустил → на месте» нечем проверять руками. Панель разницы не видит
/// (<see cref="ICentreLayoutStore"/>) — по образцу <see cref="LabTileLayoutFile"/>.
/// </summary>
public sealed class LabCentreLayoutFile : ICentreLayoutStore
{
    private readonly string _path = Path.Combine(LabFiles.Root, "centre-layout.json");

    public IReadOnlyList<CenterRow>? Load()
    {
        try
        {
            return File.Exists(_path) ? CenterLayoutJson.Read(File.ReadAllText(_path)) : null;
        }
        catch (IOException)
        {
            // Недочитанный файл — не повод ронять стенд: панель начнёт с умолчания.
            return null;
        }
    }

    public void Save(IReadOnlyList<CenterRow> rows) => File.WriteAllText(_path, CenterLayoutJson.Write(rows));

    /// <summary>Сброс к умолчанию — тем же способом, каким сбрасывается раскладка плиток.</summary>
    public void Reset() => File.Delete(_path);
}
