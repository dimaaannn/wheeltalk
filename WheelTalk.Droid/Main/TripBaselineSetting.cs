using WheelTalk.Core.Settings;
using WheelTalk.Dashboard.Droid.Screen.Tiles;

namespace WheelTalk.Droid.Main;

/// <summary>
/// Точки отсчёта плиток-дистанций — строкой в тех же слоях, что и раскладка
/// (<see cref="TileLayoutSetting"/>): своей таблицы под десяток чисел не заводим, а атомарность и
/// хранение у слоёв уже есть.
/// <para>
/// <b>В общий слой, а не в слой колеса</b>, хотя точки и заведены по колёсам. Колесо здесь внутри
/// самой записи, и это не мелочь: слой колеса подменяется при переключении, а дистанции обязаны
/// пережить смену и вернуться прежними, когда вернутся к прежнему колесу (решение владельца
/// 10.08.2026).
/// </para>
/// <para>
/// Настройкой это не становится: в каталоге настроек ключа нет, человеку он не показывается и не
/// правится — слои тут просто хранилище строк.
/// </para>
/// </summary>
internal sealed class TripBaselineSetting(LayeredSettings layers) : ITripBaselineStore
{
    private const string Key = "Tiles:TripPoints";

    public string? Load() => layers.Get(layers.Scope, Key).Value;

    public void Save(string json) => layers.Set(layers.Scope, Key, json, SettingLayer.GlobalOnly);
}
