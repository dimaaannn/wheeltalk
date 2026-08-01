using WheelTalk.Core.Contracts;

namespace WheelTalk.Droid.Configuration;

/// <summary>
/// Feeds the app from a recorded raw dump instead of a real wheel. Everything above the transport
/// behaves as it does on a ride, so the screens can be worked on indoors — and in an emulator,
/// which has no Bluetooth radio at all.
/// </summary>
public sealed class ReplayOptions
{
    public const string SectionName = "Replay";

    /// <summary>
    /// Empty — the normal case — means talk to the wheel. Otherwise a RAW_*.csv: a bare file name
    /// is looked up in the folder recordings are written to, so pushing a dump next to them and
    /// naming it here is the whole setup.
    /// </summary>
    public string DumpFile { get; set; } = "";

    /// <summary>
    /// Во сколько раз быстрее записанного проигрывать. Меньше единицы — медленнее; на раскруте до
    /// потолка это единственный способ расслышать стадии тревоги по отдельности, потому что на
    /// настоящей скорости они проходят за пару секунд и сливаются.
    /// </summary>
    public double Speed { get; set; } = 1.0;

    /// <summary>
    /// Ручной выбор протокола для дампа, чей заголовок кадра неоднозначен — сегодня это только
    /// InMotion V1/V2, у обоих кадры <c>AA AA</c>, и по дереву GATT их не развести: у записи дерева
    /// нет вовсе (см. <c>ITransport.ConnectAsync</c>). <c>null</c> (умолчание) означает «решает
    /// <c>AutoDecoder</c> по заголовку кадра», как для всех остальных протоколов, чьи дампы себя
    /// называют сами. См. replay/README.md.
    /// </summary>
    public WheelProtocol? Protocol { get; set; }
}
