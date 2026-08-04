namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Где живёт собранная человеком раскладка. Экран получает хранилище снаружи — тем же порядком,
/// каким получает слова и историю (план 23 §3.4): у боевого приложения это слоистая настройка, у
/// стенда файл, и знать разницу экрану не положено.
/// </summary>
public interface ITileLayoutStore
{
    /// <summary><c>null</c> — сохранённой раскладки нет; экран берёт зашитую.</summary>
    IReadOnlyList<MetricTile>? Load();

    /// <summary>Запомнить раскладку как она есть. Зовётся кнопкой «сохранить», не каждым переносом.</summary>
    void Save(IReadOnlyList<MetricTile> tiles);
}
