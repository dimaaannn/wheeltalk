using WheelTalk.Core.Dashboard;

namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Где живёт состав справочного блока центра. Панель получает хранилище снаружи — тем же порядком,
/// каким его получают плитки (<c>ITileLayoutStore</c>): у боевого приложения это слоистая
/// настройка, у стенда её нет вовсе, и знать разницу панели не положено.
/// </summary>
public interface ICentreLayoutStore
{
    /// <summary><c>null</c> — собранного состава нет; берётся умолчание <see cref="CenterLayout.Default"/>.</summary>
    IReadOnlyList<CenterRow>? Load();

    /// <summary>Запомнить состав. Зовётся на каждую правку в редакторе: отдельной кнопки «сохранить» там нет.</summary>
    void Save(IReadOnlyList<CenterRow> rows);
}
