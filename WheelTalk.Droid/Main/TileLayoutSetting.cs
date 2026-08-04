using WheelTalk.Core.Settings;
using WheelTalk.Dashboard.Droid.Screen.Tiles;

namespace WheelTalk.Droid.Main;

/// <summary>
/// Раскладка плиток в настройках приложения: JSON одной строкой в существующие слои
/// (план 23 §3.4 — своей таблицы не заводить, атомарность и хранение у слоёв уже есть).
/// <para>
/// Пишется <b>только в общий слой</b>: раскладка одна на приложение, не по колесу (решение
/// владельца 03.08.2026) — набор величин у колёс разный, но плитка молчащей величины рисует
/// прочерк, и держать по раскладке на колесо значило бы собирать каждую заново.
/// </para>
/// </summary>
internal sealed class TileLayoutSetting(LayeredSettings layers) : ITileLayoutStore
{
    private const string Key = "Tiles:Layout";

    public IReadOnlyList<MetricTile>? Load() => TileLayoutJson.Read(layers.Get(Key).Value);

    public void Save(IReadOnlyList<MetricTile> tiles) =>
        layers.Set(Key, TileLayoutJson.Write(tiles), globalOnly: true);
}
