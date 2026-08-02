using System.Text;
using WheelTalk.Core.Ports;
using WheelTalk.Core.Services;

namespace WheelTalk.Core.Logging;

/// <summary>One BLE notification, as it arrived. No wheel address here: the ring is emptied the
/// moment a different wheel connects, so everything in it belongs to one wheel by construction —
/// carrying the MAC per frame would be storing an answer that cannot vary.</summary>
/// <param name="UtcTicks">UTC timestamp (<see cref="DateTimeOffset.UtcTicks"/>) — kept raw rather
/// than converted to local time here: that conversion happens once, in <see cref="BleFrameTail.FormatSection"/>,
/// not on every notification.</param>
/// <param name="Frame">The notification's own byte array. Not copied — <c>ITransport</c>
/// implementations (see <c>AndroidBleClient.OnCharacteristicChanged</c>) hand a fresh array to every
/// notification, so there is nothing here to alias.</param>
public readonly record struct BleFrameEntry(long UtcTicks, byte[] Frame);

/// <summary>
/// The last <see cref="Capacity"/> BLE notifications, kept for the "Передать отладочную
/// информацию" report — no toggle, no file, no setup: whatever was on the air in the last few
/// minutes before the button was pressed. Exists because turning on the file dump
/// (<see cref="RawFrameRecorder"/>) ahead of time and pulling it over <c>adb</c> is not something a
/// rider on the road can do; this rides along with the report that already exists.
/// <para>
/// Subscribes to <see cref="ITransport.DataReceived"/> for the lifetime of the app — the same
/// transport instance survives every reconnect and every wheel switch (<c>WheelSession</c> rebuilds
/// its decoder per connection, not the transport), so one subscription made at startup is enough.
/// </para>
/// <para>
/// Connecting a <b>different</b> wheel empties the ring. The report is read to work out what one
/// wheel's frames mean, and two wheels' frames in one section is a mix a reader has to unpick
/// before they can start — the previous wheel's tail is not worth that. A reconnect to the same
/// wheel keeps what is there: it is the same wheel and the same conversation.
/// </para>
/// </summary>
public sealed class BleFrameTail
{
    /// <summary>
    /// Пять минут эфира — столько владелец просил держать. Уведомлений в секунду замерено по
    /// дампам в <c>replay/</c>: 23,5 у Sherman L и 14,7 у MTen3 (кадры BLE по 20 байт, не кадры
    /// протокола — InMotion V2 разбирается на четыре уведомления). По верхней границе в 25 в
    /// секунду 8192 уведомления — это 5,5 минуты; ближайшая меньшая степень двойки, 4096, дала бы
    /// меньше трёх.
    /// </summary>
    public const int Capacity = 8192;

    private readonly ICircularBuffer<BleFrameEntry> _ring = new CircularBuffer<BleFrameEntry>(Capacity);
    private readonly WheelSession _session;
    private readonly TimeProvider _timeProvider;

    /// <summary>Колесо, которому принадлежит всё, что сейчас в кольце.</summary>
    private string _mac = "";

    public BleFrameTail(ITransport transport, WheelSession session, TimeProvider timeProvider)
    {
        _session = session;
        _timeProvider = timeProvider;
        transport.DataReceived += OnFrame;
    }

    /// <summary>
    /// Обычный кадр стоит одного сравнения указателей: <c>WheelSession.Address</c> присваивается
    /// раз за сеанс, поэтому у всех кадров одного колеса это буквально та же ссылка, и сравнение
    /// строк посимвольно нужно только в тот единственный кадр, когда ссылка сменилась.
    /// <para>
    /// Пустой адрес (сеанс уже остановлен, а последнее уведомление ещё летело) колесом не
    /// считается: иначе кольцо чистилось бы на каждом отключении — ровно перед тем, как владелец
    /// полезет за отчётом.
    /// </para>
    /// </summary>
    private void OnFrame(byte[] frame)
    {
        string? address = _session.Address;
        if (address is not null && !ReferenceEquals(address, _mac))
        {
            bool sameWheel = string.Equals(address, _mac, StringComparison.Ordinal);
            _mac = address;
            if (!sameWheel) _ring.Clear();
        }

        _ring.Add(new BleFrameEntry(_timeProvider.GetUtcNow().UtcTicks, frame));
    }

    /// <summary>
    /// The report section: empty string if nothing has arrived yet (an empty section is worse than
    /// no section at all — nothing here to explain). Frame lines are <see cref="RawFrameLog.FormatLine"/>
    /// output, unchanged — this section is meant to be copied straight into a <c>RAW_*.csv</c> and
    /// replayed, and a second line format alongside the real one would break that.
    /// </summary>
    public string FormatSection()
    {
        BleFrameEntry[] entries = _ring.Snapshot();
        if (entries.Length == 0) return "";

        var text = new StringBuilder();
        text.Append("----- кадры BLE (кольцо ").Append(Capacity).Append(") -----").Append('\n');
        text.Append("· ").Append(_mac).Append('\n');

        foreach (var entry in entries)
        {
            var local = new DateTimeOffset(entry.UtcTicks, TimeSpan.Zero).ToLocalTime();
            text.Append(RawFrameLog.FormatLine(local, entry.Frame)).Append('\n');
        }
        return text.ToString();
    }
}
