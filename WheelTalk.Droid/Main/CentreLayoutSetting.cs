using WheelTalk.Core.Dashboard;
using WheelTalk.Core.Settings;
using WheelTalk.Dashboard.Droid.Screen;

namespace WheelTalk.Droid.Main;

/// <summary>
/// Состав центра в настройках приложения: JSON одной строкой в существующие слои — тем же порядком,
/// что и раскладка плиток (<see cref="TileLayoutSetting"/>), и по той же причине: своей таблицы не
/// заводим, атомарность и хранение у слоёв уже есть.
/// <para>
/// Пишется <b>только в общий слой</b>. Центр главного экрана — лицо приложения, и человек собирает
/// его один раз под свою манеру ездить, а не под колесо: подключился к другому — панель обязана
/// выглядеть так же. Набор величин у колёс и правда разный, но молчащая величина рисует прочерк, а
/// не ломает состав, — тот же довод, каким общей сделана раскладка плиток (решение владельца
/// 03.08.2026).
/// </para>
/// </summary>
internal sealed class CentreLayoutSetting(LayeredSettings layers) : ICentreLayoutStore
{
    private const string Key = "Centre:Layout";

    public IReadOnlyList<CenterRow>? Load() => CenterLayoutJson.Read(layers.Get(layers.Scope, Key).Value);

    public void Save(IReadOnlyList<CenterRow> rows) =>
        layers.Set(layers.Scope, Key, CenterLayoutJson.Write(rows), SettingLayer.GlobalOnly);
}
