using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Battery;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Diagnostics;
using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of InMotionAdapter.java 1:1 (decode() + command builders) for the InMotion V1 protocol
/// family (V5/V8/V10/Glide…). Frames are CAN messages wrapped <c>AA AA … 55 55</c> with <c>0xA5</c>
/// escaping (<see cref="InMotionUnpacker"/>/<see cref="InMotionCanMessage"/>).
/// <para>
/// Active protocol, but unlike Gotway/KingSong the request/response loop isn't just a bootstrap
/// handshake — it runs for the whole connection: a 6-digit password is sent six times before the
/// wheel answers at all, then a poll (fast-info request, or slow-info while the model/serial are
/// still unknown) goes out roughly every step the wheel doesn't itself just answered. The original
/// drives this off a raw <c>java.util.Timer</c> ticking every 25 ms; here it is
/// <see cref="TimeProvider.CreateTimer"/> (testable on virtual time), writing through
/// <see cref="WriteRequested"/> into the same <see cref="Ports.SequentialWriteQueue"/> every other
/// write goes through — no second retry mechanism, per <c>WheelSession</c>'s rule. The timer is a
/// real OS resource this decoder is the only owner of, so it implements <see cref="IDisposable"/>
/// (<see cref="Services.WheelService.Dispose"/> disposes the protocol decoder when it implements it).
/// </para>
/// <para>
/// Scope of this port (vertical slice, matches GotwayDecoder/KingsongDecoder's depth):
///   - Full live-telemetry path: fast-info (speed/voltage/current/temperature/IMU-temperature/
///     angle/roll/mode/total+trip distance/battery), slow-info (serial/model/version), alert text.
///   - NOT ported: the original's per-command "news" broadcast (Calibration/RideMode/RemoteControl/
///     Light/HandleButton/SpeakerVolume acknowledgements formatted into a UI toast via Android
///     Intent) — no such channel exists on this side, and <see cref="Contracts.TelemetrySnapshot"/>
///     has no field for it; these ack frames are still recognized (matching the original's ID
///     switch) but produce no state change, exactly like the original's own fall-through for any
///     other unrecognized id.
///   - NOT ported: staging <c>BuildXxx</c> commands for the keep-alive timer to pick up on its next
///     tick (the original's <c>settingCommand</c>/<c>settingCommandReady</c> fields) — commands are
///     sent immediately through the normal <c>WheelService.SendCommand</c> → transport path instead,
///     same as every other decoder in this port. The original staged them because its raw
///     <c>bluetoothCmd</c> write had no delivery guarantee of its own and leaned on the polling loop
///     to avoid colliding writes; <see cref="Ports.SequentialWriteQueue"/> already solves that
///     properly, so a second, decoder-internal scheduling mechanism would just be the two-retry-
///     mechanisms anti-pattern <c>WheelSession</c>'s own doc comment warns against.
///   - NOT ported: <c>setLedState</c>/<c>setHandleButtonState</c>/<c>updateMaxSpeed</c>/
///     <c>setSpeakerVolume</c>/<c>setPedalTilt</c>/<c>setPedalSensivity</c>/<c>setRideMode</c>/
///     <c>powerOff</c>/<c>wheelSound</c> — <see cref="IWheelDecoder"/>'s command surface has no slot
///     for any of them (only beep/light/pedals-mode/reset-trip/calibrate exist), matching how
///     KingSong's alarm-tier commands were left out for the same structural reason. The slow-info
///     frame's corresponding *read* side (led/handle-button/max-speed/speaker-volume/pedal-hardness/
///     ride-mode) is still parsed — see the field group below — per the plan's owner note: a 1:1
///     port's settings-frame parsing isn't thrown away even where nothing consumes it yet.
///   - NOT ported: <c>getFastData()</c>/<c>getBatteryLevelsdata()</c>/<c>getVersion()</c>/
///     <c>setMode(int)</c> — dead code in the original (declared, never called).
/// </para>
/// </summary>
public sealed partial class InMotionDecoder : IWheelDecoder, IPasswordProtected, IDisposable
{
    private static readonly Dictionary<int, InMotionCanMessage.IdValue> IdByValue =
        Enum.GetValues<InMotionCanMessage.IdValue>().ToDictionary(v => (int)v);

    /// <summary>
    /// Сколько ждать ответа после того, как пароль ушёл все шесть раз. Мера взята с эталонного
    /// дампа оригинала (`RAW_inmotion_V8S.csv`): там от последнего кадра пароля до slow-info
    /// проходит ~150 мс, а весь разговор от подключения до телеметрии укладывается в секунду.
    /// Три секунды — с запасом на порядок, и всё ещё быстрее, чем человек успеет удивиться пустому
    /// экрану.
    /// </summary>
    private static readonly TimeSpan PasswordGrace = TimeSpan.FromSeconds(3);

    private readonly WheelState _state;
    private readonly IWheelConfig _config;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InMotionDecoder> _logger;
    private readonly InMotionUnpacker _unpacker;
    private readonly ITimer _keepAliveTimer;

    private InMotionModel _model = InMotionModel.Unknown;
    private int _passwordSent;
    private bool _needSlowData = true;
    private int _updateStep;

    // Ждём ли ответа на пароль и с какого мгновения. Оба поля принадлежат такту опроса и только
    // ему — потому и без барьеров: заявка снаружи приходит через _restartRequested.
    private bool _answerPending;
    private long _answerDueSince;

    // Пишет поток кадров, читает такт.
    private volatile bool _framesSeen;

    // Пишет такт, читает поток экрана (AwaitingPassword).
    private volatile bool _rejectionReported;

    // Заявка «пароль сменился» от потока экрана. Исполняет её такт: у состояния пароля один
    // хозяин, и это дешевле и понятнее любого замка.
    private int _restartRequested;

    // Parsed from the slow-info frame but not persisted anywhere else yet — no IWheelConfig slot
    // exists for any of these (see class doc). A future wheel-settings-import stage (plan 21 §7
    // q3's owner note) picks them up from here instead of re-deriving the byte layout.
    private bool _ledEnabled;
    private bool _handleButtonDisabled;
    private int _wheelMaxSpeed;
    private int _speakerVolume;
    private int _pedalsAdjustment;
    private bool _rideMode;
    private int _pedalHardness = 100;

    public event Action<byte[]>? WriteRequested;

    public InMotionDecoder(WheelState state, IWheelConfig config, TimeProvider timeProvider, ILogger<InMotionDecoder> logger)
    {
        _state = state;
        _config = config;
        _timeProvider = timeProvider;
        _logger = logger;
        _state.WheelType = WheelType.Inmotion;
        // Not independently DI-resolved (always `new`'d here) — shares this decoder's typed logger
        // category, same as GotwayUnpacker/GotwayDecoder.
        _unpacker = new InMotionUnpacker(logger);

        // Port of startKeepAliveTimer (InMotionAdapter.java:299-344) — started once per connection,
        // same as the original starts it once per adapter instance at connect time.
        _keepAliveTimer = timeProvider.CreateTimer(_ => OnKeepAliveTick(), null,
            TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(25));
    }

    public bool IsReady => _model != InMotionModel.Unknown && _state.Serial.Length > 0;

    /// <inheritdoc />
    /// <remarks>Заявка на новый пароль снимает признак сразу, не дожидаясь такта: пароль уже
    /// сменили, и держать причину до ближайшего такта значило бы мигать ею на экране.</remarks>
    public bool AwaitingPassword =>
        _rejectionReported && Volatile.Read(ref _restartRequested) == 0 && _model == InMotionModel.Unknown;

    /// <summary>
    /// Ответ идёт через общий каскад (план 27 §27.3). Знание протокола здесь короткое: у всех
    /// колёс InMotion первого поколения ряд один — 20 (порт <c>getCellsForWheel()</c>); выше него
    /// только число, заданное человеком (§27.4).
    /// </summary>
    public CellCount GetCellsForWheel() => CellCountResolver.Resolve(CellInputs());

    /// <summary>Всё, что декодер знает о ряде. Считает по этому каскад — здесь только сбор.</summary>
    internal CellCountInputs CellInputs() => new()
    {
        ConfiguredCells = _config.CellsInSeries,
        ProtocolCells = 20,
        PackVolts = _state.Voltage / 100.0,
        // WheelPercent намеренно пуст: у первого поколения InMotion заряд считает наша же кривая из
        // напряжения (BatteryFromVoltage) — колесо процента не шлёт, в отличие от второго поколения.
        // Подать его значило бы делить напряжение на выведенное из него, и ступень подтверждала бы
        // любую догадку, включая неверную (план 27 §27.5).
    };

    /// <summary>Port of InMotionAdapter.decode(byte[]).</summary>
    public bool Decode(byte[] data)
    {
        using IDisposable? identityScope = BeginIdentityScope();
        LogDecodeInvoked();
        _state.ResetRideTime();

        foreach (byte c in data)
        {
            if (!_unpacker.AddChar(c)) continue;

            // Кадр собрался — значит связь живая и молчание колеса именно наше, а не «колеса нет».
            // Без этой отметки выключенное колесо просило бы пароль вместо того, чтобы честно
            // отвалиться по сторожу данных.
            _framesSeen = true;

            // Port of the unpacker's own `updateStep = 0` side effect on frame completion
            // (InMotionAdapter.java:1303) — moved here because our unpacker doesn't know about the
            // decoder's polling cadence (see InMotionUnpacker's class doc).
            _updateStep = 0;

            var message = InMotionCanMessage.Verify(_unpacker.GetBuffer(), _logger);
            if (message is null) continue;

            var idValue = IdByValue.GetValueOrDefault(message.Id, InMotionCanMessage.IdValue.NoOp);

            // GetFastInfo/Alert/GetSlowInfo return from decode() immediately in the original — any
            // remaining bytes in this call's `data` go unexamined. Ported exactly: this loop exits
            // via `return` the same way, not `break`.
            switch (idValue)
            {
                case InMotionCanMessage.IdValue.GetFastInfo:
                    return DecodeFastInfo(message);
                case InMotionCanMessage.IdValue.Alert:
                    return DecodeAlert(message);
                case InMotionCanMessage.IdValue.GetSlowInfo:
                    if (message.IsValid) _needSlowData = false;
                    return DecodeSlowInfo(message);
                case InMotionCanMessage.IdValue.PinCode:
                    _passwordSent = int.MaxValue;
                    break;
            }
        }
        return false;
    }

    /// <summary>Port of CANMessage.parseFastInfoMessage (InMotionAdapter.java:1108-1163).</summary>
    private bool DecodeFastInfo(InMotionCanMessage message)
    {
        if (!message.IsValid) return false;
        byte[] ex = message.ExData!;
        LogFastInfo();

        double angle = MathsUtil.IntFromBytesLE(ex, 0) / 65536.0;
        double roll = MathsUtil.IntFromBytesLE(ex, 72) / 90.0;
        double speed = (MathsUtil.IntFromBytesLE(ex, 12) + MathsUtil.IntFromBytesLE(ex, 16))
            / (_model.SpeedCalculationFactor() * 2.0);
        speed = Math.Abs(speed);
        int voltage = MathsUtil.IntFromBytesLE(ex, 24);
        int current = MathsUtil.IntFromBytesLE(ex, 20);
        int temperature = (sbyte)ex[32];
        int imuTemp = (sbyte)ex[34];
        int battery = BatteryFromVoltage(voltage, _model, _config.UseBetterPercents);

        long totalDistance;
        if (_model.BelongToInputType("1") || _model.BelongToInputType("5")
            || _model is InMotionModel.V8 or InMotionModel.Glide3 or InMotionModel.V10 or InMotionModel.V10F
                or InMotionModel.V10S or InMotionModel.V10SF or InMotionModel.V10T or InMotionModel.V10FT
                or InMotionModel.V8F or InMotionModel.V8S)
        {
            totalDistance = MathsUtil.IntFromBytesLE(ex, 44);
        }
        else if (_model == InMotionModel.R0)
        {
            totalDistance = MathsUtil.LongFromBytesLE(ex, 44);
        }
        else if (_model == InMotionModel.L6)
        {
            totalDistance = MathsUtil.LongFromBytesLE(ex, 44) * 100;
        }
        else
        {
            totalDistance = (long)Math.Round(MathsUtil.LongFromBytesLE(ex, 44) / 5.711016379455429E7);
        }
        long distance = MathsUtil.IntFromBytesLE(ex, 48);

        int workModeInt = MathsUtil.IntFromBytesLE(ex, 60);
        string workMode;
        if (_model is InMotionModel.V8F or InMotionModel.V8S or InMotionModel.V10 or InMotionModel.V10F
            or InMotionModel.V10FT or InMotionModel.V10S or InMotionModel.V10SF or InMotionModel.V10T)
        {
            roll = 0;
            workMode = GetWorkModeString(workModeInt);
        }
        else
        {
            workMode = GetLegacyWorkModeString(workModeInt);
        }

        _state.SetAngle(angle);
        _state.SetRoll(roll);
        _state.SetSpeed((int)(speed * 360.0));
        _state.SetTopSpeed(_state.Speed);
        _state.SetVoltage(voltage);
        _state.SetBatteryLevel(battery, CellInputs());
        _state.SetCurrent(current);
        _state.SetTotalDistance(totalDistance);
        _state.SetWheelDistance(distance);
        _state.SetTemperature(temperature * 100);
        _state.SetImuTemp(imuTemp);
        _state.SetModeStr(workMode);
        _state.CalculatePwm();
        _state.CalculatePower();

        return true;
    }

    /// <summary>Port of CANMessage.parseAlertInfoMessage (InMotionAdapter.java:1165-1205).</summary>
    private bool DecodeAlert(InMotionCanMessage message)
    {
        byte[] data = message.Data;
        int alertId = (sbyte)data[0];
        double alertValue = ((sbyte)data[3] * 256) | (data[2] & 0xFF);
        double alertValue2 = ((sbyte)data[7] * 256 * 256 * 256) | ((data[6] & 0xFF) * 256 * 256)
            | ((data[5] & 0xFF) * 256) | (data[4] & 0xFF);
        double aSpeed = Math.Abs(alertValue2 / 3812.0 * 3.6);

        var hex = new StringBuilder("[");
        foreach (byte b in data) hex.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        hex.Append(']');

        string fullText = alertId switch
        {
            0x05 => string.Format(CultureInfo.InvariantCulture,
                "Start from tilt angle {0:F2} at speed {1:F2} {2}", alertValue / 100.0, aSpeed, hex),
            0x06 => string.Format(CultureInfo.InvariantCulture,
                "Tiltback at speed {0:F2} at limit {1:F2} {2}", aSpeed, alertValue / 1000.0, hex),
            0x19 => string.Format(CultureInfo.InvariantCulture, "Fall Down {0}", hex),
            0x20 => string.Format(CultureInfo.InvariantCulture,
                "Low battery at voltage {0:F2} {1}", alertValue2 / 100.0, hex),
            0x21 => string.Format(CultureInfo.InvariantCulture,
                "Speed cut-off at speed {0:F2} and something {1:F2} {2}", aSpeed, alertValue / 10.0, hex),
            0x26 => string.Format(CultureInfo.InvariantCulture,
                "High load at speed {0:F2} and current {1:F2} {2}", aSpeed, alertValue / 1000.0, hex),
            0x1d => string.Format(CultureInfo.InvariantCulture,
                "Please repair: bad battery cell found. At voltage {0:F2} {1}", alertValue2 / 100.0, hex),
            _ => string.Format(CultureInfo.InvariantCulture,
                "Unknown Alert {0:F2} {1:F2}, please contact palachzzz, hex {2}", alertValue, alertValue2, hex),
        };

        LogAlert(fullText);
        _state.SetAlert(fullText);
        return true;
    }

    /// <summary>Port of CANMessage.parseSlowInfoMessage (InMotionAdapter.java:1208-1262).</summary>
    private bool DecodeSlowInfo(InMotionCanMessage message)
    {
        if (!message.IsValid) return false;
        byte[] ex = message.ExData!;
        LogSlowInfo();

        InMotionModel model = InMotionModels.FindByBytes(ex);
        if (model == InMotionModel.Unknown) model = InMotionModel.V8;

        int v0 = ex[27] & 0xFF;
        int v1 = ex[26] & 0xFF;
        int v2 = ((ex[25] & 0xFF) << 8) | (ex[24] & 0xFF);
        string version = string.Create(CultureInfo.InvariantCulture, $"{v0}.{v1}.{v2}");

        var serial = new StringBuilder();
        for (int j = 0; j < 8; j++) serial.Append(ex[7 - j].ToString("X2", CultureInfo.InvariantCulture));

        bool light = ex[80] == 1;
        bool led = ex.Length > 130 && ex[130] == 1;
        bool handleButtonDisabled = ex.Length > 129 && ex[129] != 1;
        bool rideMode = ex.Length > 132 && ex[132] == 1;
        int pedalHardness = ex.Length > 124 ? (ex[124] - 28) & 0xFF : 100;
        int pedals = (int)Math.Round(MathsUtil.IntFromBytesLE(ex, 56) / 6553.6);
        int maxSpeed = (((ex[61] & 0xFF) << 8) | (ex[60] & 0xFF)) / 1000;
        int speakerVolume = ex.Length > 126 ? (((ex[126] & 0xFF) << 8) | (ex[125] & 0xFF)) / 100 : 0;

        _state.SetSerial(serial.ToString());
        _state.SetModel(model.DisplayName());
        _state.SetVersion(version);
        _config.LightEnabled = light;

        _ledEnabled = led;
        _handleButtonDisabled = handleButtonDisabled;
        _wheelMaxSpeed = maxSpeed;
        _speakerVolume = speakerVolume;
        _pedalsAdjustment = pedals;
        _rideMode = rideMode;
        _pedalHardness = pedalHardness;

        _model = model;
        return false;
    }

    /// <summary>Port of batteryFromVoltage(int, Model) (InMotionAdapter.java:486-560).</summary>
    private static int BatteryFromVoltage(int voltsRaw, InMotionModel model, bool useBetterPercents)
    {
        double volts = voltsRaw / 100.0;
        double batt;

        if (model.BelongToInputType("1") || model == InMotionModel.R0)
        {
            batt = volts >= 82.50 ? 1.0 : volts > 68.0 ? (volts - 68.0) / 14.50 : 0.0;
        }
        else if (model.BelongToInputType("5") || model is InMotionModel.V8 or InMotionModel.Glide3
            or InMotionModel.V8F or InMotionModel.V8S)
        {
            batt = useBetterPercents
                ? volts > 84.00 ? 1.0 : volts > 68.5 ? (volts - 68.5) / 15.5 : 0.0
                : volts > 82.50 ? 1.0 : volts > 68.0 ? (volts - 68.0) / 14.5 : 0.0;
        }
        else if (model is InMotionModel.V10 or InMotionModel.V10F or InMotionModel.V10S
            or InMotionModel.V10SF or InMotionModel.V10T or InMotionModel.V10FT)
        {
            if (useBetterPercents)
            {
                batt = volts > 83.50 ? 1.00
                     : volts > 68.00 ? (volts - 66.50) / 17
                     : volts > 64.00 ? (volts - 64.00) / 45
                     : 0;
            }
            else
            {
                batt = volts > 82.50 ? 1.0 : volts > 68.0 ? (volts - 68.0) / 14.5 : 0.0;
            }
        }
        else if (model.BelongToInputType("6"))
        {
            batt = 0.0;
        }
        else
        {
            batt = volts >= 82.00 ? 1.0
                 : volts > 77.8 ? (volts - 77.8) / 4.2 * 0.2 + 0.8
                 : volts > 74.8 ? (volts - 74.8) / 3.0 * 0.2 + 0.6
                 : volts > 71.8 ? (volts - 71.8) / 3.0 * 0.2 + 0.4
                 : volts > 70.3 ? (volts - 70.3) / 1.5 * 0.2 + 0.2
                 : volts > 68.0 ? (volts - 68.0) / 2.3 * 0.2
                 : 0.0;
        }

        return (int)(batt * 100.0);
    }

    /// <summary>Port of getLegacyWorkModeString(int) (InMotionAdapter.java:562-591).</summary>
    private static string GetLegacyWorkModeString(int value) => (value & 0xF) switch
    {
        0 => "Idle",
        1 => "Drive",
        2 => "Zero",
        3 => "LargeAngle",
        4 => "Check",
        5 => "Lock",
        6 => "Error",
        7 => "Carry",
        8 => "RemoteControl",
        9 => "Shutdown",
        10 => "pomStop",
        12 => "Unlock",
        _ => "Unknown",
    };

    /// <summary>Port of getWorkModeString(int) (InMotionAdapter.java:593-614).</summary>
    private static string GetWorkModeString(int value)
    {
        int hValue = value >> 4;
        string result = hValue switch
        {
            1 => "Shutdown",
            2 => "Drive",
            3 => "Charging",
            _ => string.Create(CultureInfo.InvariantCulture, $"Unknown code {hValue}"),
        };
        if ((value & 0xF) == 1) result += " - Engine off";
        return result;
    }

    /// <summary>Port of the keep-alive timer's TimerTask.run() (InMotionAdapter.java:300-339), minus
    /// the write-failure fast-retry (<c>updateStep = 5</c>) — see class doc.</summary>
    private void OnKeepAliveTick()
    {
        if (Interlocked.Exchange(ref _restartRequested, 0) == 1) ApplyRestart();

        if (_updateStep == 0)
        {
            if (_passwordSent < 6)
            {
                RequestWrite(InMotionCanMessage.GetPassword(_config.InMotionPassword).WriteBuffer());
                _passwordSent++;

                // Ждать начинаем с ПЕРВОЙ отправки, а не с шестой. Шестой может не случиться
                // вовсе: колесо отвечает на пароль кадром PinCode, а тот обнуляет счётчик
                // смыслом (_passwordSent = int.MaxValue в Decode) — и ветка, в которой стоял
                // прежний взвод срока, больше не берётся ни разу. То есть на колесе, которое
                // отвечает, вопрос о пароле не поднялся бы никогда — ровно там, где он и нужен.
                if (!_answerPending)
                {
                    _answerPending = true;
                    _answerDueSince = _timeProvider.GetTimestamp();
                }
            }
            else if (_model == InMotionModel.Unknown || _needSlowData)
            {
                RequestWrite(InMotionCanMessage.GetSlowData().WriteBuffer());
            }
            else
            {
                RequestWrite(InMotionCanMessage.StandardMessage().WriteBuffer());
            }
        }
        _updateStep = (_updateStep + 1) % 10;

        CheckPasswordAnswer();
    }

    /// <summary>
    /// Пустило ли колесо. Признак допуска один — разобранный slow-info: пока модель неизвестна,
    /// колесо с нами не разговаривает. Ответ на сам кадр пароля (<c>PinCode</c>) признаком не
    /// считается: он приходит и до того, как колесо решит, и оригинал по нему всего лишь
    /// перестаёт слать пароль.
    /// </summary>
    private void CheckPasswordAnswer()
    {
        if (!_answerPending) return;

        // Заявка подана, но исполнится со следующего такта: этот успел пройти Interlocked.Exchange
        // раньше нажатия. Отказ отсюда был бы отказом по СТАРОМУ паролю, поднятым ровно в тот миг,
        // когда человек ввёл новый, — и настоящий отказ через три секунды он бы уже не отличил.
        if (Volatile.Read(ref _restartRequested) == 1) return;

        if (_model != InMotionModel.Unknown)
        {
            _answerPending = false; // пустило — больше не спрашиваем
            return;
        }

        if (_rejectionReported || !_framesSeen) return;
        if (_timeProvider.GetElapsedTime(_answerDueSince) < PasswordGrace) return;

        _rejectionReported = true;
        LogPasswordRejected();
    }

    /// <summary>
    /// <see cref="IPasswordProtected.RestartAuthentication"/> — своё, у оригинала такого пути нет
    /// (см. интерфейс). Зовётся из потока экрана и потому <b>только оставляет заявку</b>: само
    /// состояние правит такт, единственный его хозяин.
    /// </summary>
    public void RestartAuthentication() => Interlocked.Exchange(ref _restartRequested, 1);

    /// <summary>Исполнение заявки — уже в такте.</summary>
    private void ApplyRestart()
    {
        _passwordSent = 0;
        _answerPending = false;
        _rejectionReported = false;
        _needSlowData = true;
        // Такт шлёт только при _updateStep == 0, а он сбрасывается любым входящим кадром — новый
        // пароль уйдёт в ближайшие ~100 мс сам.
        LogPasswordRetry();
    }

    private void RequestWrite(byte[] bytes) => WriteRequested?.Invoke(bytes);

    public void Dispose() => _keepAliveTimer.Dispose();

    // --- Commands ---

    /// <summary>Port of wheelBeep() (InMotionAdapter.java:413-418) — newer wheels get the dedicated
    /// beep command, older ones (V8/V5F…) play a sound instead.</summary>
    public byte[] BuildWheelBeep() =>
        (_model.HasWheelModesWheel() ? InMotionCanMessage.WheelBeep() : InMotionCanMessage.PlaySound(4)).WriteBuffer();

    public byte[] BuildSetLightState(bool enabled)
    {
        _config.LightEnabled = enabled;
        return InMotionCanMessage.SetLight(enabled).WriteBuffer();
    }

    /// <summary>Port of switchFlashlight() (InMotionAdapter.java:346-351).</summary>
    public byte[] BuildSwitchFlashlight() => BuildSetLightState(!_config.LightEnabled);

    /// <summary>No pedals-mode concept in InMotionAdapter — its pedal settings (tilt/sensitivity/
    /// ride mode) don't map onto our single-integer contract, and none of them is exposed by it.</summary>
    public byte[]? BuildUpdatePedalsMode(int mode) => null;

    /// <summary>No resetTrip() in InMotionAdapter — BaseAdapter doesn't declare that hook either.</summary>
    public byte[]? BuildResetTrip() => null;

    /// <summary>Port of wheelCalibration() (InMotionAdapter.java:408-411).</summary>
    public byte[]? BuildCalibrate() => InMotionCanMessage.WheelCalibration().WriteBuffer();

    private IDisposable? BeginIdentityScope()
    {
        if (_model == InMotionModel.Unknown) return null;

        return _logger.BeginScope(new Dictionary<string, object?>
        {
            ["WheelType"] = _state.WheelType,
            ["Model"] = _state.Model,
            ["Version"] = _state.Version,
        });
    }

    [LoggerMessage(EventId = LogEvents.Decoding.DecodeInvokedId, EventName = LogEvents.Decoding.DecodeInvokedName,
        Level = LogLevel.Trace, Message = "Decode InMotion")]
    private partial void LogDecodeInvoked();

    [LoggerMessage(EventId = LogEvents.Decoding.ImFastInfoId, EventName = LogEvents.Decoding.ImFastInfoName,
        Level = LogLevel.Debug, Message = "InMotion fast-info frame found")]
    private partial void LogFastInfo();

    [LoggerMessage(EventId = LogEvents.Decoding.ImSlowInfoId, EventName = LogEvents.Decoding.ImSlowInfoName,
        Level = LogLevel.Debug, Message = "InMotion slow-info frame found")]
    private partial void LogSlowInfo();

    [LoggerMessage(EventId = LogEvents.Decoding.ImAlertId, EventName = LogEvents.Decoding.ImAlertName,
        Level = LogLevel.Warning, Message = "InMotion alert: {Alert}")]
    private partial void LogAlert(string alert);

    [LoggerMessage(EventId = LogEvents.Decoding.ImPasswordRejectedId, EventName = LogEvents.Decoding.ImPasswordRejectedName,
        // Без числа отправок: колесо, ответившее на пароль кадром PinCode, обнуляет счётчик
        // смыслом, и «шесть раз» врало бы ровно в том случае, ради которого строка и заведена.
        Level = LogLevel.Warning, Message = "Im.PasswordRejected — пароль отправлен, кадры идут, колесо не представилось")]
    private partial void LogPasswordRejected();

    [LoggerMessage(EventId = LogEvents.Decoding.ImPasswordRetryId, EventName = LogEvents.Decoding.ImPasswordRetryName,
        Level = LogLevel.Information, Message = "Im.PasswordRetry — пробуем новый пароль без переподключения")]
    private partial void LogPasswordRetry();
}
