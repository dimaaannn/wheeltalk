using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Battery;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Diagnostics;
using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of KingsongAdapter.java 1:1 (decode() + command builders) for the KingSong protocol
/// family. Frames are 20 bytes, header <c>AA 55</c>, type byte at [16] — each BLE notification
/// already is one complete frame (no unpacker needed, unlike Gotway/Veteran): the plan's phase 0.3
/// keeps MTU at 23 bytes precisely because KingSong's base frames fit in one notification.
/// <para>
/// Like Gotway/Begode this protocol is active, and the wheel says nothing at all until it is asked:
/// the request/response loop that drives it lives in two places outside the adapter in the original,
/// and both are folded in here via <see cref="WriteRequested"/> — the same event Gotway's "V"/"N"
/// handshake uses — because this port keeps all active-protocol behavior inside the decoder rather
/// than split across a BLE-service layer that doesn't exist on this side.
/// <list type="bullet">
///   <item><c>MainActivity.kt:387</c> — the first word is the app's: on CONNECTED, with the family
///   already known from the GATT tree, it asks for the name without waiting for a single frame.
///   Here that is <see cref="BootstrapDelay"/>, a one-shot timer started on construction (the same
///   seam <c>InMotionDecoder</c>'s keep-alive uses: an event raised in the constructor would fire
///   before <c>WheelService</c> has subscribed to it).</item>
///   <item><c>BluetoothService.kt:280-287</c> — after <b>every</b> notification on FFE1: name still
///   unknown, ask for it (0x9B); name known, serial not, ask for that (0x63). Note where it sits:
///   <i>outside</i> <c>KingsongAdapter.decode()</c>, so the adapter's own <c>data.length >= 20</c>
///   and <c>AA 55</c> guards never gate it. <see cref="Decode"/> keeps that: the guards decide the
///   return value, not whether the wheel gets asked.</item>
/// </list>
/// Both halves were needed on a live KS-16S (03.08.2026): it answered a bare subscription with
/// nine bytes of <c>AT+ULKTE</c> every 2,4 с and nothing else, forever.
/// </para>
/// Scope of this port (vertical slice, matches GotwayDecoder's depth):
///   - Full live-telemetry path: frame 0xA9 (voltage/speed/distance/current/temperature/mode),
///     0xB9 (wheel distance/top speed/fan/charging/motor temperature), 0xBB (name/model/version),
///     0xB3 (serial number), 0xF5 (cpu load/output — feeds <see cref="WheelState.UpdatePwm"/>),
///     0xF6 (speed limit).
///   - NOT ported: BMS frames (0xF1/0xF2 cell/temperature pages, 0xD0 extended F-series page,
///     0xE1/0xE2 serial, 0xE5/0xE6 firmware) — a second <see cref="SmartBms"/> path this slice
///     doesn't need. <see cref="Decode"/> recognizes them only by falling through to <c>false</c>,
///     matching the original's own fall-through for exactly the same frame types.
///   - NOT ported: writing alarm-tier speeds and max-speed (command 0x85, and the echo-request
///     follow-up that re-sends frame 0xA4 back as type 0x98) — owner decision (plan 21 §7 q3): the
///     wheel beeps on its own thresholds, set from the stock app, so command 0x85 is never built
///     at all. Reading frame 0xA4/0xB5 IS kept (<see cref="DecodeAlarmAndMaxSpeed"/>) per the same
///     decision's owner note: a 1:1 port's settings-frame parsing isn't thrown away, it just isn't
///     persisted to <see cref="WheelState"/> or any setting yet — a future wheel-settings-import
///     stage picks it up from there instead of re-deriving the byte layout.
///   - NOT ported: the raw BLE-advertised name (<c>WheelData.mBtName</c>) branch of the 84V-wheel
///     check — this slice has no wiring from the transport layer into the decoder for that name.
///     The model-name and "ROCKW"-prefix branches (both driven by decoded frame content) are kept.
///   - NOT ported: the KS-18L kilometer-scale correction (<c>set18Lkm</c>) — a UI-toggled
///     preference that defaults to "off" (the original's <c>m18Lkm</c> starts <c>true</c>, and the
///     correction only applies when it's <c>false</c>) with no UI knob in this slice, so the port
///     always skips it, matching the shipped default.
/// </summary>
public sealed partial class KingsongDecoder : IWheelDecoder, IDisposable
{
    private const int LightModeOff = 0;
    private const int LightModeOn = 1;
    private const int LightModeStrobe = 2;

    /// <summary>
    /// Пауза перед первым запросом имени. Нужна не колесу, а порядку сборки: подписчик
    /// <see cref="WriteRequested"/> появляется уже после конструктора (<c>WheelService</c>), и
    /// событие, поднятое в нём самом, не услышал бы никто. Значение — то же, что у стартовой
    /// задержки опроса <c>InMotionDecoder</c>.
    /// </summary>
    private static readonly TimeSpan BootstrapDelay = TimeSpan.FromMilliseconds(200);

    private static readonly string[] Wheels84V =
        ["KS-18L", "KS-16X", "KS-16XF", "RW", "KS-18LH", "KS-18LY", "KS-S18", "KS-S16", "KS-S16P"];

    private readonly WheelState _state;
    private readonly IWheelConfig _config;
    private readonly ILogger<KingsongDecoder> _logger;
    private readonly ITimer _bootstrapTimer;

    private int _lightMode = LightModeOff;

    // Alarm-tier speeds and max speed (frame 0xA4/0xB5) — parsed but deliberately not persisted
    // anywhere this phase (see DecodeAlarmAndMaxSpeed).
    private int _ksAlarm1Speed;
    private int _ksAlarm2Speed;
    private int _ksAlarm3Speed;
    private int _wheelMaxSpeed;

    public event Action<byte[]>? WriteRequested;

    public KingsongDecoder(WheelState state, IWheelConfig config, TimeProvider timeProvider, ILogger<KingsongDecoder> logger)
    {
        _state = state;
        _config = config;
        _logger = logger;
        _state.WheelType = WheelType.KingSong;

        // Port of MainActivity.kt:387 — заговорить первым, не дожидаясь кадра (см. doc класса).
        // Одноразовый: дальше недостающее спрашивается по уведомлениям, как в оригинале.
        _bootstrapTimer = timeProvider.CreateTimer(_ => RequestMissingIdentity(), null,
            BootstrapDelay, Timeout.InfiniteTimeSpan);
    }

    public void Dispose() => _bootstrapTimer.Dispose();

    public bool IsReady => _state.Model.Length > 0 && _state.Voltage != 0;

    /// <summary>Port of KingsongAdapter.decode(byte[]).</summary>
    public bool Decode(byte[] data)
    {
        using IDisposable? identityScope = BeginIdentityScope();
        LogDecodeInvoked();
        _state.ResetRideTime();

        bool newDataFound = IsWheelFrame(data) && DecodeFrame(data);

        // BluetoothService.kt:282-286's post-notification bootstrap, relocated into the decoder
        // (see class doc). Стоит после разбора и вне проверок кадра — ровно как в оригинале, где
        // оно вне decode(): уведомление, которое не разобрать, всё равно повод спросить. Пока это
        // стояло за проверкой длины, живой KS-16S не получал ни одного запроса — его девятибайтные
        // AT+ULKTE до неё не доходили.
        RequestMissingIdentity();

        return newDataFound;
    }

    private static bool IsWheelFrame(byte[] data) =>
        data.Length >= 20 && data[0] == 0xAA && data[1] == 0x55;

    private bool DecodeFrame(byte[] data) => data[16] switch
    {
        0xA9 => DecodeLiveData(data),
        0xB9 => DecodeDistanceTimeFan(data),
        0xBB => DecodeName(data),
        0xB3 => DecodeSerial(data),
        0xF5 => DecodeCpuLoad(data),
        0xF6 => DecodeSpeedLimit(data),
        0xA4 or 0xB5 => DecodeAlarmAndMaxSpeed(data),
        // BMS pages (0xF1/0xF2/0xD0/0xE1/0xE2/0xE5/0xE6) and anything else fall through to
        // false, exactly like the original — it never returns true for these either.
        _ => false,
    };

    /// <summary>Спросить то из опознания, чего ещё нет: сначала имя (0x9B), потом серийник (0x63).</summary>
    private void RequestMissingIdentity()
    {
        if (_state.Name.Length == 0) RequestWrite(0x9B);
        else if (_state.Serial.Length == 0) RequestWrite(0x63);
    }

    /// <summary>Frame 0xA9 — live data (KingsongAdapter.java:36-179).</summary>
    private bool DecodeLiveData(byte[] buff)
    {
        LogLiveData();
        int voltage = MathsUtil.GetInt2R(buff, 2);
        _state.SetVoltage(voltage);
        _state.SetSpeed(MathsUtil.GetInt2R(buff, 4));
        _state.SetTotalDistance(MathsUtil.GetInt4R(buff, 6));
        // set18Lkm's KS-18L correction is not ported (see class doc) — it only applies when a UI
        // toggle we don't have flips m18Lkm to false, and the shipped default leaves it skipped.

        int current = (buff[10] & 0xFF) + ((sbyte)buff[11] << 8);
        _state.SetCurrent(current);
        _state.CalculatePower();
        _state.SetTemperature(MathsUtil.GetInt2R(buff, 12));

        if ((buff[15] & 0xFF) == 0xE0)
        {
            _state.SetModeStr(((sbyte)buff[14]).ToString(CultureInfo.InvariantCulture));
        }

        _state.SetBatteryLevel(CalculateBattery(voltage, _config.UseBetterPercents), CellInputs());
        return true;
    }

    /// <summary>Frame 0xB9 — distance/time/fan data (KingsongAdapter.java:180-187).</summary>
    private bool DecodeDistanceTimeFan(byte[] buff)
    {
        LogDistanceTimeFan();
        _state.SetWheelDistance(MathsUtil.GetInt4R(buff, 2));
        _state.SetTopSpeed(MathsUtil.GetInt2R(buff, 8));
        _state.SetFanStatus((sbyte)buff[12]);
        _state.SetChargingStatus((sbyte)buff[13]);
        _state.SetTemperature2(MathsUtil.GetInt2R(buff, 14));
        return false;
    }

    /// <summary>Frame 0xBB — name and model/version (KingsongAdapter.java:188-210).</summary>
    private bool DecodeName(byte[] buff)
    {
        int end = 0;
        while (end < 14 && buff[end + 2] != 0) end++;

        string name = JavaTrim(Encoding.ASCII.GetString(buff, 2, end));
        _state.SetName(name);
        LogHandshake("Name", name);

        string[] parts = name.Split('-');
        var model = new StringBuilder();
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (i != 0) model.Append('-');
            model.Append(parts[i]);
        }
        _state.SetModel(model.ToString());

        if (int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rawVersion))
        {
            _state.SetVersion((rawVersion / 100.0).ToString("F2", CultureInfo.InvariantCulture));
        }
        return false;
    }

    /// <summary>Frame 0xB3 — serial number (KingsongAdapter.java:211-218).</summary>
    private bool DecodeSerial(byte[] buff)
    {
        var serialBytes = new byte[18];
        Array.Copy(buff, 2, serialBytes, 0, 14);
        Array.Copy(buff, 17, serialBytes, 14, 3);
        serialBytes[17] = 0;
        string serial = Encoding.ASCII.GetString(serialBytes);
        _state.SetSerial(serial);
        LogHandshake("Serial", serial);
        // updateKSAlarmAndSpeed() is not called here — the original's follow-up write is the
        // excluded 0x85/0x98 alarm/max-speed path (see class doc).
        return false;
    }

    /// <summary>Frame 0xF5 — CPU load and output (KingsongAdapter.java:219-223).</summary>
    private bool DecodeCpuLoad(byte[] buff)
    {
        LogCpuLoad();
        _state.SetCpuLoad((sbyte)buff[14]);
        _state.SetOutput((sbyte)buff[15] * 100);
        _state.UpdatePwm();
        return false;
    }

    /// <summary>Frame 0xF6 — speed limit (KingsongAdapter.java:224-227).</summary>
    private bool DecodeSpeedLimit(byte[] buff)
    {
        double speedLimit = MathsUtil.GetInt2R(buff, 2) / 100.0;
        LogSpeedLimit(speedLimit);
        _state.SetSpeedLimit(speedLimit);
        return false;
    }

    /// <summary>
    /// Frame 0xA4/0xB5 — alarm-tier speeds and max speed (KingsongAdapter.java:228-242). Parsed
    /// into fields (not thrown away — plan 21's owner note: a 1:1 port's settings-frame parsing
    /// stays, for the future wheel-settings-import stage, even where nothing consumes it yet) but
    /// deliberately not persisted anywhere this phase: no <c>WheelSettings</c> field exists for
    /// these (owner decision, plan 21 §7 q3 — the wheel beeps on its own thresholds, set from the
    /// stock app), and the write-back path is excluded entirely — command 0x85 is never built, and
    /// the original's echo-request follow-up (repeating the frame back with type 0x98) isn't sent.
    /// </summary>
    private bool DecodeAlarmAndMaxSpeed(byte[] buff)
    {
        _wheelMaxSpeed = buff[10] & 0xFF;
        _ksAlarm3Speed = buff[8] & 0xFF;
        _ksAlarm2Speed = buff[6] & 0xFF;
        _ksAlarm1Speed = buff[4] & 0xFF;
        return true;
    }

    /// <summary>Port of the per-model battery-percent branches (KingsongAdapter.java:53-177).</summary>
    private int CalculateBattery(int voltage, bool useBetterPercents)
    {
        if (Is84VWheel())
        {
            if (useBetterPercents)
            {
                if (voltage > 8350) return 100;
                if (voltage > 6800) return (voltage - 6650) / 17;
                if (voltage > 6400) return (voltage - 6400) / 45;
                return 0;
            }
            if (voltage < 6250) return 0;
            if (voltage >= 8250) return 100;
            return (voltage - 6250) / 20;
        }
        if (Is126VWheel())
        {
            if (useBetterPercents)
            {
                if (voltage > 12525) return 100;
                if (voltage > 10200) return (int)Math.Round((voltage - 9975) / 25.5);
                if (voltage > 9600) return (int)Math.Round((voltage - 9600) / 67.5);
                return 0;
            }
            if (voltage < 9375) return 0;
            if (voltage >= 12375) return 100;
            return (voltage - 9375) / 30;
        }
        if (Is151VWheel())
        {
            if (useBetterPercents)
            {
                if (voltage > 15030) return 100;
                if (voltage > 12240) return (int)Math.Round((voltage - 11970) / 30.6);
                if (voltage > 11520) return (int)Math.Round((voltage - 11520) / 81.0);
                return 0;
            }
            if (voltage < 11250) return 0;
            if (voltage >= 14850) return 100;
            return (voltage - 11250) / 36;
        }
        if (Is176VWheel())
        {
            if (useBetterPercents)
            {
                if (voltage > 17535) return 100;
                if (voltage > 14280) return (int)Math.Round((voltage - 13965) / 35.7);
                if (voltage > 13440) return (int)Math.Round((voltage - 13440) / 94.5);
                return 0;
            }
            if (voltage < 13125) return 0;
            if (voltage >= 17325) return 100;
            return (voltage - 13125) / 42;
        }
        if (Is100VWheel())
        {
            if (useBetterPercents)
            {
                if (voltage > 10020) return 100;
                if (voltage > 8160) return (int)Math.Round((voltage - 7980) / 20.4);
                if (voltage > 7680) return (int)Math.Round((voltage - 7680) / 54.0);
                return 0;
            }
            if (voltage < 7500) return 0;
            if (voltage >= 9900) return 100;
            return (voltage - 7500) / 24;
        }

        if (useBetterPercents)
        {
            if (voltage > 6680) return 100;
            if (voltage > 5440) return (int)Math.Round((voltage - 5320) / 13.6);
            if (voltage > 5120) return (voltage - 5120) / 36;
            return 0;
        }
        if (voltage < 5000) return 0;
        if (voltage >= 6600) return 100;
        return (voltage - 5000) / 16;
    }

    /// <summary>
    /// The original also matches <c>WheelData.mBtName == "RW"</c> — not ported, see class doc.
    /// </summary>
    private bool Is84VWheel() =>
        Array.IndexOf(Wheels84V, _state.Model) >= 0 || _state.Name.StartsWith("ROCKW", StringComparison.Ordinal);

    private bool Is126VWheel() => _state.Model is "KS-S20" or "KS-S22";
    private bool Is176VWheel() => _state.Model == "KS-F22P";
    private bool Is151VWheel() => _state.Model == "KS-F18P";
    private bool Is100VWheel() => _state.Model == "KS-S19";

    /// <summary>
    /// Ответ идёт через общий каскад (план 27 §27.3): декодер подаёт наверх ряд, узнанный по имени
    /// модели, и число, заданное человеком (§27.4), а ответ выдаёт резолвер.
    /// </summary>
    public CellCount GetCellsForWheel() => CellCountResolver.Resolve(CellInputs());

    /// <summary>Всё, что декодер знает о ряде. Считает по этому каскад — здесь только сбор.</summary>
    private CellCountInputs CellInputs() => new()
    {
        ConfiguredCells = _config.CellsInSeries,
        ProtocolCells = CellsFromModel(),
        PackVolts = _state.Voltage / 100.0,
        // WheelPercent намеренно пуст: заряд у KingSong считает наша же кривая из напряжения
        // (CalculateBattery), и подать его значило бы делить напряжение на выведенное из него —
        // ступень подтверждала бы любую догадку, включая неверную (план 27 §27.5). Процент годится
        // сюда, только когда его называет само колесо.
    };

    /// <summary>Port of KingsongAdapter.getCellsForWheel() (KingsongAdapter.java:494-503).</summary>
    private int CellsFromModel()
    {
        if (Is84VWheel()) return 20;
        if (Is100VWheel()) return 24;
        if (Is126VWheel()) return 30;
        if (Is151VWheel()) return 36;
        if (Is176VWheel()) return 42;
        return 16;
    }

    private IDisposable? BeginIdentityScope()
    {
        if (_state.Model.Length == 0) return null;

        return _logger.BeginScope(new Dictionary<string, object?>
        {
            ["WheelType"] = _state.WheelType,
            ["Model"] = _state.Model,
            ["Version"] = _state.Version,
        });
    }

    // --- Commands ---

    public byte[] BuildWheelBeep() => EmptyRequest(0x88);

    /// <summary>
    /// KingsongAdapter never overrides <c>setLightState</c> — KS only exposes a 3-way light MODE
    /// (off/on/strobe) via <c>setLightMode</c>, the same command <see cref="BuildSwitchFlashlight"/>
    /// cycles through. This maps our on/off contract onto that mode command (mode 1 = on, mode 0 =
    /// off) — the same adaptation <c>GotwayDecoder</c> makes for its own light-mode cycle.
    /// </summary>
    public byte[] BuildSetLightState(bool enabled)
    {
        _config.LightEnabled = enabled;
        _lightMode = enabled ? LightModeOn : LightModeOff;
        return BuildLightModeCommand(_lightMode);
    }

    /// <summary>Port of KingsongAdapter.switchFlashlight() (KingsongAdapter.java:448-455).</summary>
    public byte[] BuildSwitchFlashlight()
    {
        _lightMode = _lightMode + 1 > LightModeStrobe ? LightModeOff : _lightMode + 1;
        return BuildLightModeCommand(_lightMode);
    }

    /// <summary>Port of KingsongAdapter.setLightMode(int) (KingsongAdapter.java:457-464).</summary>
    private byte[] BuildLightModeCommand(int lightMode)
    {
        byte[] data = EmptyRequest(0x73);
        data[2] = (byte)(lightMode + 0x12);
        data[3] = 0x01;
        return data;
    }

    /// <summary>Port of KingsongAdapter.updatePedalsMode(int) (KingsongAdapter.java:430-438).</summary>
    public byte[]? BuildUpdatePedalsMode(int pedalsMode)
    {
        byte[] data = EmptyRequest(0x87);
        data[2] = (byte)pedalsMode;
        data[3] = 0xE0;
        data[17] = 0x15;
        return data;
    }

    /// <summary>No resetTrip() in KingsongAdapter — BaseAdapter doesn't declare that hook either.</summary>
    public byte[]? BuildResetTrip() => null;

    /// <summary>Port of KingsongAdapter.wheelCalibration() (KingsongAdapter.java:440-445).</summary>
    public byte[]? BuildCalibrate() => EmptyRequest(0x89);

    /// <summary>
    /// Java's <c>String.trim()</c> strips any char &lt;= U+0020 (control bytes included), unlike
    /// C#'s <c>string.Trim()</c>, which only strips Unicode whitespace — the name frame can carry
    /// a leading control byte (KingsongAdapterTest.kt's "decode Name and Model data" fixture does),
    /// so this needs the Java semantics to match the original's <c>new String(...).trim()</c>.
    /// </summary>
    private static string JavaTrim(string value)
    {
        int start = 0, end = value.Length;
        while (start < end && value[start] <= ' ') start++;
        while (end > start && value[end - 1] <= ' ') end--;
        return value[start..end];
    }

    /// <summary>Port of KingsongAdapter.getEmptyRequest() (KingsongAdapter.java:572-574), with the
    /// type byte ([16]) set in one place instead of by every caller.</summary>
    private static byte[] EmptyRequest(byte type) =>
        [0xAA, 0x55, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, type, 0x14, 0x5A, 0x5A];

    private void RequestWrite(byte type) => WriteRequested?.Invoke(EmptyRequest(type));

    [LoggerMessage(EventId = LogEvents.Decoding.DecodeInvokedId, EventName = LogEvents.Decoding.DecodeInvokedName,
        Level = LogLevel.Trace, Message = "Decode KingSong")]
    private partial void LogDecodeInvoked();

    [LoggerMessage(EventId = LogEvents.Decoding.KsLiveDataId, EventName = LogEvents.Decoding.KsLiveDataName,
        Level = LogLevel.Debug, Message = "KingSong live data frame found")]
    private partial void LogLiveData();

    [LoggerMessage(EventId = LogEvents.Decoding.KsDistanceTimeFanId, EventName = LogEvents.Decoding.KsDistanceTimeFanName,
        Level = LogLevel.Debug, Message = "KingSong distance/time/fan frame found")]
    private partial void LogDistanceTimeFan();

    [LoggerMessage(EventId = LogEvents.Decoding.KsCpuLoadId, EventName = LogEvents.Decoding.KsCpuLoadName,
        Level = LogLevel.Debug, Message = "KingSong cpu load frame found")]
    private partial void LogCpuLoad();

    [LoggerMessage(EventId = LogEvents.Decoding.KsSpeedLimitId, EventName = LogEvents.Decoding.KsSpeedLimitName,
        Level = LogLevel.Debug, Message = "KingSong speed limit frame found. SpeedLimit={SpeedLimit}")]
    private partial void LogSpeedLimit(double speedLimit);

    [LoggerMessage(EventId = LogEvents.Decoding.HandshakeId, EventName = LogEvents.Decoding.HandshakeName,
        Level = LogLevel.Debug, Message = "Handshake {Kind} recognized: {Value}")]
    private partial void LogHandshake(string kind, string value);
}
