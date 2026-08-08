using System.Text;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Battery;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Diagnostics;
using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of InmotionAdapterV2.java 1:1 (decode() + command builders) for the InMotion V2 protocol
/// family (V9/V11/V11y/V12·HS·HT·PRO/V12S/V13·PRO/V14·s·g), the Nordic-UART-based successor to V1.
/// Frame codec is <see cref="InMotionV2Unpacker"/>/<see cref="InMotionV2Message"/> — no RAW logs
/// exist for this protocol anywhere (unlike V1), so <c>InmotionAdapterV2Test.kt</c>'s fixtures are
/// the only ground truth this port has.
/// <para>
/// Active protocol, same shape as V1: a multi-stage bootstrap (car type → serial → versions →
/// current settings → "useless data" → statistics, each stage advancing on the wheel's own
/// response for the first two stages and fire-and-forget for the rest) followed by continuous
/// real-time-data polling, all driven by a 25 ms <see cref="TimeProvider.CreateTimer"/> tick
/// (started once per connection) writing through <see cref="WriteRequested"/> — same queue every
/// other write goes through, no second retry mechanism (matches V1 and the rule in
/// <c>WheelSession</c>'s doc comment).
/// </para>
/// <para>
/// Scope of this port (narrower than V1's, for a specific reason — see below):
///   - Full live-telemetry path for every model family the original supports: six distinct
///     real-time-info layouts (<c>parseRealTimeInfoV11</c> pre-1.4 firmware,
///     <c>...V11_1_4</c> firmware 1.4+, <c>...V12</c> shared by V12 HS/HT/PRO,
///     <c>...V13</c> shared by V13/V13 PRO, <c>...V14</c> shared by V14s/V14g,
///     <c>...V9</c> shared byte-for-byte by V9/V11y/V12S — the original itself has six near-
///     duplicate methods for these three, this port collapses them into one shared method since
///     their field layouts are identical, not an approximation), wheel-type/serial/version
///     handshake, total-distance stats, and the wheel-error bit flags (<c>getError</c>).
///   - NOT ported: the individual field extraction inside the seven <c>parseSettingsVXX</c>
///     methods — each is a large (20-30 field) per-model bitfield whose only original consumer is
///     the same "mirror into AppConfig, nothing reads it back here" pattern already excluded for
///     every other protocol's settings echoes (Gotway's frame 0x04 second half, KingSong's alarm
///     tiers, V1's led/handle-button/speaker-volume/pedal settings). Unlike those, replicating
///     seven near-identical 20+-field bitfield parsers for a slice that already excludes their
///     entire reason to exist is pure bulk with no verification value — none of
///     <c>InmotionAdapterV2Test.kt</c>'s settings-frame fixtures assert a single field value, only
///     that <c>decode()</c> returns <c>false</c> for them, which this port's simplified
///     <see cref="DecodeSettings"/> (recognizes the frame, returns false, extracts nothing) already
///     satisfies byte-for-byte. This is a real, reasoned scope cut, not an oversight — recorded in
///     AGENTS.md's "Отклонения от оригинала".
///   - NOT ported: the per-command "news" broadcast (Android Intent), same as V1 — no such channel
///     exists on this side.
///   - NOT ported: staging <c>BuildXxx</c> commands for the keep-alive timer to pick up
///     (<c>settingCommand</c>/<c>settingCommandReady</c>) — same reasoning as V1: commands send
///     immediately through the normal path, <see cref="Ports.SequentialWriteQueue"/> already gives
///     the delivery guarantee the original's staging approximated.
///   - NOT ported: <c>getMaxSpeed()</c>/<c>getBatteryData()</c>/<c>getDiagnostic()</c> — the first
///     is a settings-page UI helper (no caller in this slice), the latter two build request
///     messages nothing in the original's own keep-alive loop or command surface ever sends.
/// </para>
/// </summary>
public sealed partial class InMotionDecoderV2 : IWheelDecoder, IDisposable
{
    private readonly WheelState _state;
    private readonly IWheelConfig _config;
    private readonly ILogger<InMotionDecoderV2> _logger;
    private readonly InMotionV2Unpacker _unpacker;
    private readonly ITimer _keepAliveTimer;

    private InMotionV2Model _model = InMotionV2Model.Unknown;
    private int _protoVer;
    private int _stateCon;
    private int _updateStep;
    private int _lightSwitchCounter;

    /// <summary>
    /// Процент заряда, названный <b>самим колесом</b> (<c>batLevel</c> из кадра реального времени).
    /// Хранится ради ступени «напряжение с процентом» (план 27 §27.5): ей нужна пара, снятая в один
    /// момент, а <see cref="WheelState.Battery"/> для этого не годится — его мог подменить режим
    /// «свои проценты», посчитанный из напряжения.
    /// </summary>
    private int? _wheelPercent;

    public event Action<byte[]>? WriteRequested;
    public event Action<byte[]>? FrameRecognized;

    public InMotionDecoderV2(WheelState state, IWheelConfig config, TimeProvider timeProvider, ILogger<InMotionDecoderV2> logger)
    {
        _state = state;
        _config = config;
        _logger = logger;
        _state.WheelType = WheelType.InmotionV2;
        // Not independently DI-resolved (always `new`'d here) — shares this decoder's typed logger
        // category, same as InMotionUnpacker/GotwayUnpacker.
        _unpacker = new InMotionV2Unpacker(logger);

        // Port of startKeepAliveTimer (InmotionAdapterV2.java:204-271).
        _keepAliveTimer = timeProvider.CreateTimer(_ => OnKeepAliveTick(), null,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(25));
    }

    public bool IsReady => _model != InMotionV2Model.Unknown && _protoVer != 0;

    /// <summary>
    /// Ответ идёт через общий каскад (план 27 §27.3). Модель приходит из рукопожатия, и это
    /// измерение, а не догадка: единственную тесную пару рядов внутри марки — V13 (30) против
    /// V14 (32) — разбирает именно она (§27.1а).
    /// </summary>
    public CellCount GetCellsForWheel() => CellCountResolver.Resolve(CellInputs());

    /// <summary>
    /// Всё, что декодер знает о ряде. Считает по этому каскад — здесь только сбор.
    /// <para>
    /// Единственный из пяти протоколов, кто подаёт <see cref="CellCountInputs.WheelPercent"/>:
    /// процент здесь приходит из кадра, а не считается нашей кривой из напряжения. Оттого пара
    /// «напряжение с процентом» тут честная, а у остальных четырёх — заколдованный круг (§27.5).
    /// </para>
    /// </summary>
    internal CellCountInputs CellInputs() => new()
    {
        ConfiguredCells = _config.CellsInSeries,
        ProtocolCells = _model.CellsForWheel(),
        PackVolts = _state.Voltage / 100.0,
        WheelPercent = _wheelPercent,
    };

    /// <summary>
    /// Записать заряд, названный колесом: сперва запомнить его для каскада, потом отдать состоянию.
    /// Порядок обязателен — <see cref="CellInputs"/> читает поле, а напряжение того же кадра уже
    /// легло в состояние строкой выше по каждому разбору.
    /// </summary>
    private void SetWheelBattery(int percent)
    {
        _wheelPercent = percent;
        _state.SetBatteryLevel(percent, CellInputs());
    }

    /// <summary>Port of setModel(Model) (InmotionAdapterV2.java:180-182) — production code calls
    /// this from the wheel-type handshake (<see cref="DecodeMainInfo"/>); the original also uses it
    /// directly from its own test suite to exercise one model's real-time-info layout without
    /// replaying the full handshake first, which is why it is public rather than test-only.</summary>
    public void SetModel(InMotionV2Model model) => _model = model;

    /// <summary>Port of setProto(int) (InmotionAdapterV2.java:183-185) — "for tests" in the
    /// original's own comment; kept public for the same reason as <see cref="SetModel"/>.</summary>
    public void SetProto(int protoVer) => _protoVer = protoVer;

    /// <summary>Port of InmotionAdapterV2.decode(byte[]).</summary>
    public bool Decode(byte[] data)
    {
        using IDisposable? identityScope = BeginIdentityScope();
        LogDecodeInvoked();
        _state.ResetRideTime();

        foreach (byte c in data)
        {
            if (!_unpacker.AddChar(c)) continue;
            _updateStep = 0;

            byte[] buffer = _unpacker.GetBuffer();
            var message = InMotionV2Message.Verify(buffer, _logger);
            if (message is null) continue;

            // Checksum verified — a live wheel, whatever this frame's command turns out to be.
            FrameRecognized?.Invoke(buffer);

            if (message.Flags == (int)InMotionV2Message.Flag.Initial)
            {
                if (message.Command_ == (int)InMotionV2Message.Command.MainInfo)
                {
                    return DecodeMainInfo(message);
                }
                // Diagnostic+turningOff (wheel power-off second stage) not ported — no powerOff()
                // command exists in this port's contract, so turningOff can never become true.
            }
            else if (message.Flags == (int)InMotionV2Message.Flag.Default)
            {
                if (message.Command_ == (int)InMotionV2Message.Command.Settings) return DecodeSettings();
                else if (message.Command_ == (int)InMotionV2Message.Command.Diagnostic) return false;
                else if (message.Command_ == (int)InMotionV2Message.Command.BatteryRealTimeInfo) return false;
                else if (message.Command_ == (int)InMotionV2Message.Command.TotalStats) return DecodeTotalStats(message);
                else if (message.Command_ == (int)InMotionV2Message.Command.RealTimeInfo) return DecodeRealTimeInfo(message);
            }
        }
        return false;
    }

    /// <summary>Port of Message.parseMainData (InmotionAdapterV2.java:552-619).</summary>
    private bool DecodeMainInfo(InMotionV2Message message)
    {
        byte[] data = message.Data;
        if (data.Length >= 1 && data[0] == 0x01 && message.Len >= 6)
        {
            _stateCon += 1;
            int series = data[2];
            int type = data[3];
            _model = InMotionV2Models.FindById(series, type);
            _state.SetModel(_model.DisplayName());
            _state.SetVersion("-");
        }
        else if (data.Length >= 1 && data[0] == 0x02 && message.Len >= 17)
        {
            _stateCon += 1;
            _state.SetSerial(Encoding.ASCII.GetString(data, 1, 16));
        }
        else if (data.Length >= 1 && data[0] == 0x06 && message.Len >= 24)
        {
            _protoVer = 0;
            int driverBoard3 = MathsUtil.ShortFromBytesLE(data, 2);
            int driverBoard2 = data[4];
            int driverBoard1 = data[5];
            string driverBoard = $"{driverBoard1}.{driverBoard2}.{driverBoard3}";

            int mainBoard3 = MathsUtil.ShortFromBytesLE(data, 11);
            int mainBoard2 = data[13];
            int mainBoard1 = data[14];
            string mainBoard = $"{mainBoard1}.{mainBoard2}.{mainBoard3}";

            int ble3 = MathsUtil.ShortFromBytesLE(data, 20);
            int ble2 = data[22];
            int ble1 = data[23];
            string ble = $"{ble1}.{ble2}.{ble3}";

            _state.SetVersion($"Main:{mainBoard} Drv:{driverBoard} BLE:{ble}");
            if (_model == InMotionV2Model.V11)
            {
                _protoVer = mainBoard1 < 2 && mainBoard2 < 4 ? 1 : 2;
            }
        }
        return false;
    }

    /// <summary>Port of the Settings-frame dispatch — simplified, see class doc: recognizes the
    /// frame (matching the original's behavior for every model) without extracting its fields.</summary>
    private bool DecodeSettings()
    {
        LogSettings();
        return false;
    }

    /// <summary>Port of Message.parseTotalStats (InmotionAdapterV2.java:1012-1034), minus the ride-
    /// time/power-on-time string formatting (not surfaced anywhere in this slice).</summary>
    private bool DecodeTotalStats(InMotionV2Message message)
    {
        byte[] data = message.Data;
        if (data.Length < 20) return false;

        LogTotalStats();
        long total = MathsUtil.IntFromBytesLE(data, 0);
        _state.SetTotalDistance(total * 10);
        return false;
    }

    /// <summary>Model-family dispatch for the RealTimeInfo command — the only frame whose byte
    /// layout genuinely differs per model (settings frames don't need this, see class doc).</summary>
    private bool DecodeRealTimeInfo(InMotionV2Message message)
    {
        byte[] data = message.Data;
        return _model switch
        {
            InMotionV2Model.V12HS or InMotionV2Model.V12HT or InMotionV2Model.V12PRO => DecodeRealTimeV12(data),
            InMotionV2Model.V13 or InMotionV2Model.V13PRO => DecodeRealTimeV13(data),
            InMotionV2Model.V14g or InMotionV2Model.V14s => DecodeRealTimeV14(data),
            InMotionV2Model.V11Y or InMotionV2Model.V9 or InMotionV2Model.V12S => DecodeRealTimeV9Family(data),
            _ => _protoVer < 2 ? DecodeRealTimeV11(data) : DecodeRealTimeV11_1_4(data),
        };
    }

    /// <summary>Port of parseRealTimeInfoV11 (InmotionAdapterV2.java:1087-1177) — pre-1.4 firmware.</summary>
    private bool DecodeRealTimeV11(byte[] data)
    {
        LogRealTimeInfo("V11");
        int voltage = MathsUtil.ShortFromBytesLE(data, 0);
        int current = MathsUtil.SignedShortFromBytesLE(data, 2);
        int speed = MathsUtil.SignedShortFromBytesLE(data, 4);
        int torque = MathsUtil.SignedShortFromBytesLE(data, 6);
        int batPower = MathsUtil.SignedShortFromBytesLE(data, 8);
        int motPower = MathsUtil.SignedShortFromBytesLE(data, 10);
        int mileage = MathsUtil.ShortFromBytesLE(data, 12) * 10;
        int batLevel = data[16] & 0x7f;
        int mosTemp = (data[17] & 0xff) + 80 - 256;
        int boardTemp = (data[20] & 0xff) + 80 - 256;
        int pitchAngle = MathsUtil.SignedShortFromBytesLE(data, 22);
        int rollAngle = MathsUtil.SignedShortFromBytesLE(data, 26);
        int dynSpeedLimit = MathsUtil.ShortFromBytesLE(data, 28);
        int dynCurrentLimit = MathsUtil.ShortFromBytesLE(data, 30);
        int cpuTemp = (data[34] & 0xff) + 80 - 256;
        int imuTemp = (data[35] & 0xff) + 80 - 256;
        int pwm = MathsUtil.ShortFromBytesLE(data, 36);

        ApplyRealTimeCore(voltage, current, speed, torque, motPower, batPower, mileage, batLevel,
            mosTemp, boardTemp, pitchAngle, rollAngle, dynSpeedLimit, dynCurrentLimit, cpuTemp, imuTemp, pwm);

        int i = data.Length < 49 ? 36 : 38;
        int motState = (data[i] >> 6) & 0x01;
        int chrgState = (data[i] >> 7) & 0x01;
        int lightState = data[i + 1] & 0x01;
        int liftedState = (data[i + 1] >> 2) & 0x01;
        _state.SetModeStr(BuildModeStr(motState, chrgState, liftedState));
        ApplyLightSwitchDebounce(lightState == 1);
        _state.SetAlert(GetError(data, i + 5));
        return true;
    }

    /// <summary>Port of parseRealTimeInfoV11_1_4 (InmotionAdapterV2.java:1179-1270) — firmware 1.4+.</summary>
    private bool DecodeRealTimeV11_1_4(byte[] data)
    {
        LogRealTimeInfo("V11 1.4+");
        int voltage = MathsUtil.ShortFromBytesLE(data, 0);
        int current = MathsUtil.SignedShortFromBytesLE(data, 2);
        int speed = MathsUtil.SignedShortFromBytesLE(data, 4);
        int torque = MathsUtil.SignedShortFromBytesLE(data, 6);
        int pwm = MathsUtil.SignedShortFromBytesLE(data, 8);
        int batPower = MathsUtil.SignedShortFromBytesLE(data, 10);
        int motPower = MathsUtil.SignedShortFromBytesLE(data, 12);
        int pitchAngle = MathsUtil.SignedShortFromBytesLE(data, 16);
        int rollAngle = MathsUtil.SignedShortFromBytesLE(data, 20);
        int mileage = MathsUtil.ShortFromBytesLE(data, 26) * 10;
        int batLevel = MathsUtil.ShortFromBytesLE(data, 28);
        int dynSpeedLimit = MathsUtil.ShortFromBytesLE(data, 34);
        int dynCurrentLimit = MathsUtil.ShortFromBytesLE(data, 36);
        int mosTemp = (data[42] & 0xff) + 80 - 256;
        int boardTemp = (data[45] & 0xff) + 80 - 256;
        int cpuTemp = (data[46] & 0xff) + 80 - 256;
        int imuTemp = (data[47] & 0xff) + 80 - 256;

        _state.SetVoltage(voltage);
        _state.SetTorque(torque / 100.0);
        _state.SetMotorPower(motPower);
        _state.SetCpuTemp(cpuTemp);
        _state.SetImuTemp(imuTemp);
        _state.SetCurrent(current);
        _state.SetSpeed(speed);
        _state.SetCurrentLimit(dynCurrentLimit / 100.0);
        _state.SetSpeedLimit(dynSpeedLimit / 100.0);
        SetWheelBattery((int)Math.Round(batLevel / 100.0));
        _state.SetTemperature(mosTemp * 100);
        _state.SetTemperature2(boardTemp * 100);
        _state.SetOutput(pwm);
        _state.UpdatePwm();
        _state.SetAngle(pitchAngle / 100.0);
        _state.SetRoll(rollAngle / 100.0);
        _state.SetTopSpeed(speed);
        _state.SetPower(batPower * 100);
        _state.SetWheelDistance(mileage);

        int motState = (data[56] >> 6) & 0x01;
        int chrgState = (data[56] >> 7) & 0x01;
        int liftedState = (data[57] >> 2) & 0x01;
        _state.SetModeStr(BuildModeStr(motState, chrgState, liftedState));
        // Light-switch debounce runs here too in the original, but its actual setLightEnabled call
        // is commented out ("bad behaviour") — no observable effect, so not ported (see class doc's
        // general note on not porting inert original code).
        _state.SetAlert(GetError(data, 61));
        return true;
    }

    /// <summary>Port of parseRealTimeInfoV12 (InmotionAdapterV2.java:1273-1362) — V12 HS/HT/PRO.</summary>
    private bool DecodeRealTimeV12(byte[] data)
    {
        LogRealTimeInfo("V12");
        int voltage = MathsUtil.ShortFromBytesLE(data, 0);
        int current = MathsUtil.SignedShortFromBytesLE(data, 2);
        int speed = MathsUtil.SignedShortFromBytesLE(data, 4);
        int torque = MathsUtil.SignedShortFromBytesLE(data, 6);
        int pwm = MathsUtil.SignedShortFromBytesLE(data, 8);
        int batPower = MathsUtil.SignedShortFromBytesLE(data, 10);
        int motPower = MathsUtil.SignedShortFromBytesLE(data, 12);
        int pitchAngle = MathsUtil.SignedShortFromBytesLE(data, 16);
        int rollAngle = MathsUtil.SignedShortFromBytesLE(data, 20);
        int mileage = MathsUtil.ShortFromBytesLE(data, 22) * 10;
        int batLevel = MathsUtil.ShortFromBytesLE(data, 24);
        int dynSpeedLimit = MathsUtil.ShortFromBytesLE(data, 30);
        int dynCurrentLimit = MathsUtil.ShortFromBytesLE(data, 32);
        int mosTemp = (data[40] & 0xff) + 80 - 256;
        int motTemp = (data[41] & 0xff) + 80 - 256;
        int cpuTemp = (data[44] & 0xff) + 80 - 256;
        int imuTemp = (data[45] & 0xff) + 80 - 256;

        _state.SetVoltage(voltage);
        _state.SetTorque(torque / 100.0);
        _state.SetMotorPower(motPower);
        _state.SetCpuTemp(cpuTemp);
        _state.SetImuTemp(imuTemp);
        _state.SetCurrent(current);
        _state.SetSpeed(speed);
        _state.SetCurrentLimit(dynCurrentLimit / 100.0);
        _state.SetSpeedLimit(dynSpeedLimit / 100.0);
        SetWheelBattery((int)Math.Round(batLevel / 100.0));
        _state.SetTemperature(mosTemp * 100);
        _state.SetTemperature2(motTemp * 100);
        _state.SetOutput(pwm);
        _state.UpdatePwm();
        _state.SetAngle(pitchAngle / 100.0);
        _state.SetRoll(rollAngle / 100.0);
        _state.SetTopSpeed(speed);
        _state.SetPower(batPower * 100);
        _state.SetWheelDistance(mileage);

        int motState = (data[54] >> 6) & 0x01;
        int chrgState = (data[54] >> 7) & 0x01;
        int liftedState = (data[55] >> 2) & 0x01;
        _state.SetModeStr(BuildModeStr(motState, chrgState, liftedState));
        // V12 reports separate low/high beam bits (appConfig.setLowBeamEnabled/setHighBeamEnabled
        // in the original) rather than the single LightEnabled every other model uses — no
        // IWheelConfig slot exists for that pair (see class doc), so not ported.
        _state.SetAlert(GetError(data, 59));
        return true;
    }

    /// <summary>Port of parseRealTimeInfoV13 (InmotionAdapterV2.java:1364-1470) — V13/V13 PRO.</summary>
    private bool DecodeRealTimeV13(byte[] data)
    {
        LogRealTimeInfo("V13");
        int voltage = MathsUtil.ShortFromBytesLE(data, 0);
        int current = MathsUtil.SignedShortFromBytesLE(data, 2);
        int pitchAngle = MathsUtil.SignedShortFromBytesLE(data, 6);
        int speed = MathsUtil.SignedShortFromBytesLE(data, 8);
        long mileage = MathsUtil.IntFromBytesRevLE(data, 10);
        int pwm = MathsUtil.SignedShortFromBytesLE(data, 14);
        int batPower = MathsUtil.SignedShortFromBytesLE(data, 16);
        int torque = MathsUtil.SignedShortFromBytesLE(data, 18);
        int motPower = MathsUtil.SignedShortFromBytesLE(data, 22);
        int rollAngle = MathsUtil.SignedShortFromBytesLE(data, 24);
        int batLevel1 = MathsUtil.ShortFromBytesLE(data, 34);
        int batLevel2 = MathsUtil.ShortFromBytesLE(data, 36);
        int dynSpeedLimit = MathsUtil.ShortFromBytesLE(data, 40);
        int dynCurrentLimit = MathsUtil.ShortFromBytesLE(data, 50);
        int mosTemp = (data[58] & 0xff) + 80 - 256;
        int motTemp = (data[59] & 0xff) + 80 - 256;
        int cpuTemp = (data[62] & 0xff) + 80 - 256;
        int imuTemp = (data[63] & 0xff) + 80 - 256;

        _state.SetVoltage(voltage);
        _state.SetTorque(torque / 100.0);
        _state.SetMotorPower(motPower);
        _state.SetCpuTemp(cpuTemp);
        _state.SetImuTemp(imuTemp);
        _state.SetCurrent(current);
        _state.SetSpeed(speed);
        _state.SetCurrentLimit(dynCurrentLimit / 100.0);
        _state.SetSpeedLimit(dynSpeedLimit / 100.0);
        SetWheelBattery((int)Math.Round((batLevel1 + batLevel2) / 200.0));
        _state.SetTemperature(mosTemp * 100);
        _state.SetTemperature2(motTemp * 100);
        _state.SetOutput(pwm);
        _state.UpdatePwm();
        _state.SetAngle(pitchAngle / 100.0);
        _state.SetRoll(rollAngle / 100.0);
        _state.SetTopSpeed(speed);
        _state.SetPower(batPower * 100);
        _state.SetWheelDistance(mileage);

        int motState = (data[74] >> 6) & 0x01;
        int chrgState = (data[74] >> 7) & 0x01;
        int liftedState = (data[75] >> 2) & 0x01;
        int lightState = (data[76] >> 1) & 0x01;
        _state.SetModeStr(BuildModeStr(motState, chrgState, liftedState));
        ApplyLightSwitchDebounce(lightState == 1);
        _state.SetAlert(GetError(data, 76));
        return true;
    }

    /// <summary>Port of parseRealTimeInfoV14 (InmotionAdapterV2.java:1473-1583) — V14s/V14g.</summary>
    private bool DecodeRealTimeV14(byte[] data)
    {
        LogRealTimeInfo("V14");
        int voltage = MathsUtil.ShortFromBytesLE(data, 0);
        int current = MathsUtil.SignedShortFromBytesLE(data, 2);
        int speed = MathsUtil.SignedShortFromBytesLE(data, 8);
        int torque = MathsUtil.SignedShortFromBytesLE(data, 12);
        int pwm = MathsUtil.SignedShortFromBytesLE(data, 14);
        int batPower = MathsUtil.SignedShortFromBytesLE(data, 16);
        int motPower = MathsUtil.SignedShortFromBytesLE(data, 18);
        int pitchAngle = MathsUtil.SignedShortFromBytesLE(data, 20);
        int rollAngle = MathsUtil.SignedShortFromBytesLE(data, 22);
        int mileage = MathsUtil.ShortFromBytesLE(data, 28) * 10;
        int batLevel1 = MathsUtil.ShortFromBytesLE(data, 34);
        int batLevel2 = MathsUtil.ShortFromBytesLE(data, 36);
        int dynSpeedLimit = MathsUtil.ShortFromBytesLE(data, 40);
        int dynCurrentLimit = MathsUtil.ShortFromBytesLE(data, 50);
        int mosTemp = (data[58] & 0xff) + 80 - 256;
        int motTemp = (data[59] & 0xff) + 80 - 256;
        int cpuTemp = (data[62] & 0xff) + 80 - 256;
        int imuTemp = (data[63] & 0xff) + 80 - 256;

        _state.SetVoltage(voltage);
        _state.SetTorque(torque / 100.0);
        _state.SetMotorPower(motPower);
        _state.SetCpuTemp(cpuTemp);
        _state.SetImuTemp(imuTemp);
        _state.SetCurrent(current);
        _state.SetSpeed(speed);
        _state.SetCurrentLimit(dynCurrentLimit / 100.0);
        _state.SetSpeedLimit(dynSpeedLimit / 100.0);
        SetWheelBattery((int)Math.Round((batLevel1 + batLevel2) / 200.0));
        _state.SetTemperature(mosTemp * 100);
        _state.SetTemperature2(motTemp * 100);
        _state.SetOutput(pwm);
        _state.UpdatePwm();
        _state.SetAngle(pitchAngle / 100.0);
        _state.SetRoll(rollAngle / 100.0);
        _state.SetTopSpeed(speed);
        _state.SetPower(batPower * 100);
        _state.SetWheelDistance(mileage);

        int motState = (data[74] >> 6) & 0x01;
        int chrgState = (data[74] >> 7) & 0x01;
        // V14 reads lifted/light state off data[76], not data[75] like V9/V11y/V12S/V13 — ported
        // exactly, not unified away (see class doc on the V9/V11y/V12S merge being an identity, not
        // an approximation — this one genuinely differs).
        int liftedState = (data[76] >> 2) & 0x01;
        int lightState = (data[76] >> 1) & 0x01;
        _state.SetModeStr(BuildModeStr(motState, chrgState, liftedState));
        ApplyLightSwitchDebounce(lightState == 1);
        _state.SetAlert(GetError(data, 77));
        return true;
    }

    /// <summary>
    /// Port of parseRealTimeInfoV9/V11y/V12S (InmotionAdapterV2.java:1585-1926) — byte-for-byte
    /// identical Java methods in the original (same field offsets, same state-byte positions, same
    /// error offset); collapsed into one shared method here rather than reproducing the duplication.
    /// </summary>
    private bool DecodeRealTimeV9Family(byte[] data)
    {
        LogRealTimeInfo("V9/V11y/V12S");
        int voltage = MathsUtil.ShortFromBytesLE(data, 0);
        int current = MathsUtil.SignedShortFromBytesLE(data, 2);
        int speed = MathsUtil.SignedShortFromBytesLE(data, 8);
        int torque = MathsUtil.SignedShortFromBytesLE(data, 12);
        int pwm = MathsUtil.SignedShortFromBytesLE(data, 14);
        int batPower = MathsUtil.SignedShortFromBytesLE(data, 16);
        int motPower = MathsUtil.SignedShortFromBytesLE(data, 18);
        int pitchAngle = MathsUtil.SignedShortFromBytesLE(data, 20);
        int rollAngle = MathsUtil.SignedShortFromBytesLE(data, 22);
        int mileage = MathsUtil.ShortFromBytesLE(data, 28) * 10;
        int batLevel1 = MathsUtil.ShortFromBytesLE(data, 34);
        int batLevel2 = MathsUtil.ShortFromBytesLE(data, 36);
        int dynSpeedLimit = MathsUtil.ShortFromBytesLE(data, 40);
        int dynCurrentLimit = MathsUtil.ShortFromBytesLE(data, 50);
        int mosTemp = (data[58] & 0xff) + 80 - 256;
        int motTemp = (data[59] & 0xff) + 80 - 256;
        int cpuTemp = (data[62] & 0xff) + 80 - 256;
        int imuTemp = (data[63] & 0xff) + 80 - 256;

        _state.SetVoltage(voltage);
        _state.SetTorque(torque / 100.0);
        _state.SetMotorPower(motPower);
        _state.SetCpuTemp(cpuTemp);
        _state.SetImuTemp(imuTemp);
        _state.SetCurrent(current);
        _state.SetSpeed(speed);
        _state.SetCurrentLimit(dynCurrentLimit / 100.0);
        _state.SetSpeedLimit(dynSpeedLimit / 100.0);
        SetWheelBattery((int)Math.Round((batLevel1 + batLevel2) / 200.0));
        _state.SetTemperature(mosTemp * 100);
        _state.SetTemperature2(motTemp * 100);
        _state.SetOutput(pwm);
        _state.UpdatePwm();
        _state.SetAngle(pitchAngle / 100.0);
        _state.SetRoll(rollAngle / 100.0);
        _state.SetTopSpeed(speed);
        _state.SetPower(batPower * 100);
        _state.SetWheelDistance(mileage);

        int motState = (data[74] >> 6) & 0x01;
        int chrgState = (data[74] >> 7) & 0x01;
        int liftedState = (data[75] >> 2) & 0x01;
        int lightState = (data[76] >> 1) & 0x01;
        _state.SetModeStr(BuildModeStr(motState, chrgState, liftedState));
        ApplyLightSwitchDebounce(lightState == 1);
        _state.SetAlert(GetError(data, 77));
        return true;
    }

    /// <summary>Shared write-out for the fields V11's two layouts compute identically (V12/V13/V14/
    /// V9-family each have their own subtly different field set, so they don't use this).</summary>
    private void ApplyRealTimeCore(int voltage, int current, int speed, int torque, int motPower, int batPower,
        int mileage, int batLevel, int mosTemp, int boardTemp, int pitchAngle, int rollAngle,
        int dynSpeedLimit, int dynCurrentLimit, int cpuTemp, int imuTemp, int pwm)
    {
        _state.SetVoltage(voltage);
        _state.SetTorque(torque / 100.0);
        _state.SetMotorPower(motPower);
        _state.SetCpuTemp(cpuTemp);
        _state.SetImuTemp(imuTemp);
        _state.SetCurrent(current);
        _state.SetSpeed(speed);
        _state.SetCurrentLimit(dynCurrentLimit / 100.0);
        _state.SetSpeedLimit(dynSpeedLimit / 100.0);
        SetWheelBattery(batLevel);
        _state.SetTemperature(mosTemp * 100);
        _state.SetTemperature2(boardTemp * 100);
        _state.SetOutput(pwm);
        _state.UpdatePwm();
        _state.SetAngle(pitchAngle / 100.0);
        _state.SetRoll(rollAngle / 100.0);
        _state.SetTopSpeed(speed);
        _state.SetPower(batPower * 100);
        _state.SetWheelDistance(mileage);
    }

    private static string BuildModeStr(int motState, int chrgState, int liftedState)
    {
        var mode = new StringBuilder();
        if (motState == 1) mode.Append("Active");
        if (chrgState == 1) mode.Append(" Charging");
        if (liftedState == 1) mode.Append(" Lifted");
        return mode.ToString();
    }

    /// <summary>Port of the debounced light-state mirror every real-time parser except V11_1_4 (its
    /// call is commented out in the original) and V12 (uses low/high beam instead) makes. Guards
    /// against a single noisy bit flip toggling the reported state — only commits after four
    /// consecutive frames disagree with the currently-known value.</summary>
    private void ApplyLightSwitchDebounce(bool wireLightOn)
    {
        if (_config.LightEnabled != wireLightOn)
        {
            if (_lightSwitchCounter > 3)
            {
                _config.LightEnabled = wireLightOn;
                _lightSwitchCounter = 0;
            }
            else
            {
                _lightSwitchCounter += 1;
            }
        }
        else
        {
            _lightSwitchCounter = 0;
        }
    }

    /// <summary>Port of Message.getError(int) (InmotionAdapterV2.java:1036-1085).</summary>
    private static string GetError(byte[] data, int i)
    {
        var error = new StringBuilder();
        void Flag(int byteIndex, int bit, string name)
        {
            if (((data[byteIndex] >> bit) & 0x01) == 1) error.Append(name).Append(' ');
        }

        Flag(i, 0, "err_iPhaseSensorState");
        Flag(i, 1, "err_iBusSensorState");
        Flag(i, 2, "err_motorHallState");
        Flag(i, 3, "err_batteryState");
        Flag(i, 4, "err_imuSensorState");
        Flag(i, 5, "err_controllerCom1State");
        Flag(i, 6, "err_controllerCom2State");
        Flag(i, 7, "err_bleCom1State");
        Flag(i + 1, 0, "err_bleCom2State");
        Flag(i + 1, 1, "err_mosTempSensorState");
        Flag(i + 1, 2, "err_motorTempSensorState");
        Flag(i + 1, 3, "err_batteryTempSensorState");
        Flag(i + 1, 4, "err_boardTempSensorState");
        Flag(i + 1, 5, "err_fanState");
        Flag(i + 1, 6, "err_rtcState");
        Flag(i + 1, 7, "err_externalRomState");
        Flag(i + 2, 0, "err_vBusSensorState");
        Flag(i + 2, 1, "err_vBatterySensorState");
        Flag(i + 2, 2, "err_canNotPowerOffState");
        Flag(i + 2, 3, "err_notKnown1");
        Flag(i + 3, 0, "err_underVoltageState");
        Flag(i + 3, 1, "err_overVoltageState");
        if (((data[i + 3] >> 2) & 0x03) > 0) error.Append("err_overBusCurrentState-").Append((data[i + 3] >> 2) & 0x03).Append(' ');
        if (((data[i + 3] >> 4) & 0x03) > 0) error.Append("err_lowBatteryState-").Append((data[i + 3] >> 4) & 0x03).Append(' ');
        Flag(i + 3, 6, "err_mosTempState");
        Flag(i + 3, 7, "err_motorTempState");
        Flag(i + 4, 0, "err_batteryTempState");
        Flag(i + 4, 1, "err_overBoardTempState");
        Flag(i + 4, 2, "err_overSpeedState");
        Flag(i + 4, 3, "err_outputSaturationState");
        Flag(i + 4, 4, "err_motorSpinState");
        Flag(i + 4, 5, "err_motorBlockState");
        Flag(i + 4, 6, "err_postureState");
        Flag(i + 4, 7, "err_riskBehaviourState");
        Flag(i + 5, 0, "err_motorNoLoadState");
        Flag(i + 5, 1, "err_noSelfTestState");
        Flag(i + 5, 2, "err_compatibilityState");
        Flag(i + 5, 3, "err_powerKeyLongPressState");
        Flag(i + 5, 4, "err_forceDfuState");
        Flag(i + 5, 5, "err_deviceLockState");
        Flag(i + 5, 6, "err_cpuOverTempState");
        Flag(i + 5, 7, "err_imuOverTempState");
        Flag(i + 6, 1, "err_hwCompatibilityState");
        Flag(i + 6, 2, "err_fanLowSpeedState");
        Flag(i + 6, 3, "err_notKnown2");

        return error.ToString();
    }

    /// <summary>Port of the keep-alive timer's TimerTask.run() (InmotionAdapterV2.java:207-267),
    /// minus the write-failure fast-retry and settings-command staging — see class doc.</summary>
    private void OnKeepAliveTick()
    {
        if (_updateStep == 0)
        {
            switch (_stateCon)
            {
                case 0:
                    RequestWrite(InMotionV2Message.GetCarType().WriteBuffer());
                    break;
                case 1:
                    RequestWrite(InMotionV2Message.GetSerialNumber().WriteBuffer());
                    break;
                case 2:
                    RequestWrite(InMotionV2Message.GetVersions().WriteBuffer());
                    _stateCon += 1;
                    break;
                case 3:
                    RequestWrite(InMotionV2Message.GetCurrentSettings().WriteBuffer());
                    _stateCon += 1;
                    break;
                case 4:
                    RequestWrite(InMotionV2Message.GetUselessData().WriteBuffer());
                    _stateCon += 1;
                    break;
                case 5:
                    RequestWrite(InMotionV2Message.GetStatistics().WriteBuffer());
                    _stateCon += 1;
                    break;
                default:
                    RequestWrite(InMotionV2Message.GetRealTimeData().WriteBuffer());
                    _stateCon = 5;
                    break;
            }
        }
        _updateStep = (_updateStep + 1) % 10;
    }

    private void RequestWrite(byte[] bytes) => WriteRequested?.Invoke(bytes);

    public void Dispose() => _keepAliveTimer.Dispose();

    // --- Commands ---

    /// <summary>Port of wheelBeep() (InmotionAdapterV2.java:274-281).</summary>
    public byte[] BuildWheelBeep()
    {
        bool useBeep = _model is InMotionV2Model.V13 or InMotionV2Model.V13PRO or InMotionV2Model.V14g
            or InMotionV2Model.V14s or InMotionV2Model.V11Y;
        return (useBeep ? InMotionV2Message.PlayBeep(0x02) : InMotionV2Message.PlaySound(0x18, IsLegacyV11)).WriteBuffer();
    }

    /// <summary>
    /// V12's dual low/high-beam light isn't ported (see class doc) — every model in this slice uses
    /// the single on/off contract, matching V1's precedent.
    /// </summary>
    public byte[] BuildSetLightState(bool enabled)
    {
        _config.LightEnabled = enabled;
        return InMotionV2Message.SetLight(enabled, IsLegacyV11).WriteBuffer();
    }

    /// <summary>Port of switchFlashlight()'s non-V12 branch (InmotionAdapterV2.java:299-303).</summary>
    public byte[] BuildSwitchFlashlight() => BuildSetLightState(!_config.LightEnabled);

    /// <summary>No pedals-mode concept in InmotionAdapterV2 either — same reasoning as V1.</summary>
    public byte[]? BuildUpdatePedalsMode(int mode) => null;

    /// <summary>No resetTrip() in InmotionAdapterV2 — BaseAdapter doesn't declare that hook either.</summary>
    public byte[]? BuildResetTrip() => null;

    /// <summary>Port of wheelCalibration() (InmotionAdapterV2.java:372-379).</summary>
    public byte[]? BuildCalibrate() =>
        (_model == InMotionV2Model.V11 && IsLegacyV11 ? InMotionV2Message.WheelCalibration() : InMotionV2Message.WheelCalibrationTurn()).WriteBuffer();

    private bool IsLegacyV11 => _model == InMotionV2Model.V11 && _protoVer < 2;

    private IDisposable? BeginIdentityScope()
    {
        if (_model == InMotionV2Model.Unknown) return null;

        return _logger.BeginScope(new Dictionary<string, object?>
        {
            ["WheelType"] = _state.WheelType,
            ["Model"] = _state.Model,
            ["Version"] = _state.Version,
        });
    }

    [LoggerMessage(EventId = LogEvents.Decoding.DecodeInvokedId, EventName = LogEvents.Decoding.DecodeInvokedName,
        Level = LogLevel.Trace, Message = "Decode InMotion V2")]
    private partial void LogDecodeInvoked();

    [LoggerMessage(EventId = LogEvents.Decoding.ImV2RealTimeInfoId, EventName = LogEvents.Decoding.ImV2RealTimeInfoName,
        Level = LogLevel.Debug, Message = "InMotion V2 real-time-info frame found ({Family})")]
    private partial void LogRealTimeInfo(string family);

    [LoggerMessage(EventId = LogEvents.Decoding.ImV2SettingsId, EventName = LogEvents.Decoding.ImV2SettingsName,
        Level = LogLevel.Debug, Message = "InMotion V2 settings frame found (not parsed — see class doc)")]
    private partial void LogSettings();

    [LoggerMessage(EventId = LogEvents.Decoding.ImV2TotalStatsId, EventName = LogEvents.Decoding.ImV2TotalStatsName,
        Level = LogLevel.Debug, Message = "InMotion V2 total-stats frame found")]
    private partial void LogTotalStats();
}
