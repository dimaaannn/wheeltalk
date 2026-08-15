namespace WheelTalk.Core.Decoding;

/// <summary>
/// Каким способом колесо LeaperKim принимает настройку педалей. Поколения различаются не украшением
/// экрана, а самой командой: у старых это три именованных положения (<c>SETh</c>/<c>SETm</c>/<c>SETs</c>,
/// порт <c>VeteranDecoder.BuildUpdatePedalsMode</c>), у новых — плавная шкала 0..100 на опкоде 15
/// (<c>PedalSoftnessSettingActivity.java:37</c>).
/// </summary>
public enum PedalGeneration
{
    /// <summary>Не знаем — и потому не шлём ни одну из двух команд (см. <see cref="PedalGenerations"/>).</summary>
    Unknown,

    /// <summary>Три положения: мягко / средне / жёстко.</summary>
    ThreePosition,

    /// <summary>Плавная шкала 0..100.</summary>
    Continuous,
}

/// <summary>
/// Таблица поколений по версии протокола — <c>docs/veteran-commands-import-plan.md</c> §5.3.
/// <para>
/// <b>Почему по версии протокола, а не по ответу колеса.</b> Настоящий признак у производителя —
/// сентинел <c>pedalHardness == 128</c> во входящем кадре настроек (<c>ControlActivity.java:390-395</c>,
/// разбор — <c>leaperkim-official-app.md</c> §5.2). Этот кадр мы пока не разбираем ни строкой, и до
/// того, как разберём, единственное знание о колесе — версия протокола из телеметрии
/// (<c>VeteranDecoder.cs:117</c>), та же, по которой уже называется модель (<c>:357-372</c>).
/// </para>
/// <para>
/// <b>Почему пять известных моделей всё равно <see cref="PedalGeneration.Unknown"/>.</b> Каталог
/// производителя (<c>Util.CAR_DATA_JSON</c>, сборка 59) перечисляет <c>continuousSoftHardSet</c>
/// ровно для семи моделей; Oryx (8), Lynx S (9) и три Nosfet (42, 43, 44) в нём отсутствуют вовсе
/// (<c>leaperkim-official-app.md</c> §5.1). Имя модели мы знаем, а способ настройки педалей — нет,
/// и догадка по соседней модели тут не «ничего не сделает»: она запишет байт в незнакомый регистр
/// прошивки. Не показать доступную настройку — неудобство, послать чужую команду — испорченная
/// поездка.
/// </para>
/// </summary>
public static class PedalGenerations
{
    /// <summary>
    /// Версия протокола → поколение педалей. Границы взяты из каталога производителя и совпадают с
    /// нашей же таблицей моделей (<c>VeteranDecoder.GetModel</c>), потому и записаны теми же
    /// числами: расхождение таблиц читатель обязан видеть сразу.
    /// </summary>
    public static PedalGeneration FromProtocolVersion(int protocolVersion) => protocolVersion switch
    {
        // Sherman (0 и 1), Abrams, Sherman S, Patton — `continuousSoftHardSet: false` у производителя.
        <= 4 => PedalGeneration.ThreePosition,

        // Lynx, Sherman L, Patton S — `continuousSoftHardSet: true`.
        5 or 6 or 7 => PedalGeneration.Continuous,

        // Oryx (8), Lynx S (9), Nosfet Apex/Aero/Aeon (42/43/44) — каталога на них нет; сюда же
        // падает и всякая версия, которой мы вовсе не знаем: молчание источника и молчание колеса
        // для этого решения — одно и то же.
        _ => PedalGeneration.Unknown,
    };
}
