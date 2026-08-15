using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Battery;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Diagnostics;
using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of GotwayAdapter.java 1:1 (decode() + command builders) for the Gotway/Begode
/// protocol family — used by Begode MTen3. Unlike Veteran, this protocol is NOT passive:
/// on connect the wheel is silent until queried with "V" (firmware) / "N" (model name), so
/// the decoder actively writes through <see cref="WriteRequested"/> while it bootstraps.
/// Scope of this port (vertical slice, matches plan §5 depth for Veteran):
///   - Full live-telemetry path: frames 0x00 (speed/voltage/current/temp), 0x01 (BMS
///     voltage/current), 0x02/0x03 (BMS cells), 0x04 (total distance + alarm bits),
///     0x07 (motor current/temp).
///   - NOT ported: frame 0xFF (PID/tuning parameter echoes) and the Alexovik custom-firmware
///     (SmirnoV/Freestyl3r) branches beyond firmware-string detection — stock Begode firmware
///     ("GW" handshake, what MTen3 reports) never takes those branches, so they are stubbed
///     rather than fully translated. Likewise the settings-echo half of frame 0x04 (pedals
///     mode/speed alarms/roll angle/LED mode/light mode/miles) is not pushed back into
///     IWheelConfig — those are UI preference mirrors, not telemetry.
/// </summary>
public sealed partial class GotwayDecoder : IWheelDecoder
{
    private const double RatioGw = 0.875;
    private const int LightModeOff = 0;
    private const int LightModeOn = 1;
    private const int LightModeStrobe = 2;

    private readonly WheelState _state;
    private readonly IWheelConfig _config;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GotwayDecoder> _logger;
    private readonly GotwayUnpacker _unpacker;

    private string _model = "";
    private string _imu = "";
    private string _fw = "";
    private string _fwprot = "";
    private int _smartBmsCells;
    private bool _trueVoltage;
    private bool _trueCurrent;
    private bool _bmsCurrent;
    private bool _truePwm;
    private bool _isReady;
    private long _lastTryTimestamp;
    private int _attempt;
    private int _lightMode = LightModeOff;

    /// <summary>Last wheel-alert text actually logged — lets <see cref="DecodeFrameB"/> throttle
    /// the Warning to alarm-state transitions instead of once per frame while an alarm is active.</summary>
    private string? _lastLoggedAlert;

    public event Action<byte[]>? WriteRequested;
    public event Action<byte[]>? FrameRecognized;

    public GotwayDecoder(WheelState state, IWheelConfig config, TimeProvider timeProvider, ILogger<GotwayDecoder> logger)
    {
        _state = state;
        _config = config;
        _timeProvider = timeProvider;
        _logger = logger;
        _state.WheelType = WheelType.GotWay;
        // GotwayUnpacker is a private implementation detail (never independently DI-resolved), so
        // it shares the owning decoder's typed logger category rather than needing its own ILoggerFactory.
        _unpacker = new GotwayUnpacker(logger);
    }

    public bool IsReady => _isReady && _state.Voltage != 0;

    /// <summary>Port of GotwayAdapter.decode(byte[]).</summary>
    public bool Decode(byte[] data)
    {
        // Identity scope is opened once per call (rather than held in a field across calls) because
        // BLE DataReceived invocations land on different thread-pool threads with a fresh
        // ExecutionContext each time — an AsyncLocal-backed MEL/Serilog scope opened in one call would
        // never actually wrap the next call's log records, and disposing it outside the synchronous
        // frame that opened it is unsupported. See §2.4 of the logging plan.
        using IDisposable? identityScope = BeginIdentityScope();
        LogDecodeInvoked();
        _state.ResetRideTime();
        bool newDataFound = false;

        // IMU is sent at the very beginning, no need to re-check once model/fw are known.
        if (_model.Length == 0 || _fw.Length == 0)
        {
            HandleHandshakeText(data);
        }

        // Parsed once per Decode() call rather than once per completed frame in the loop below —
        // config.GotwayNegative doesn't change mid-call, and an unparsable value now falls back
        // to "0" (abs) instead of throwing partway through the frame loop.
        if (!int.TryParse(_config.GotwayNegative, NumberStyles.Integer, CultureInfo.InvariantCulture, out int gotwayNegative))
        {
            gotwayNegative = 0;
        }

        foreach (byte c in data)
        {
            if (!_unpacker.AddChar(c)) continue;

            byte[] buff = _unpacker.GetBuffer();
            // The unpacker only returns true once header, fixed length and footer all line up —
            // a live wheel, whether or not this particular frame type is decoded below.
            FrameRecognized?.Invoke(buff);
            if (buff.Length <= 19) continue; // frame type always lives at buff[18]; 24-byte frames only

            bool isAlexovikFw = _config.IsAlexovikFW;
            bool useRatio = _config.UseRatio;
            bool useBetterPercents = _config.UseBetterPercents;
            bool autoVoltage = !isAlexovikFw && _config.AutoVoltage;

            byte frameType = buff[18];
            if (frameType == 0x00)
            {
                newDataFound = DecodeFrameA(buff, isAlexovikFw, useRatio, useBetterPercents, autoVoltage, gotwayNegative);
            }
            else if (frameType == 0x01)
            {
                DecodeFrame01(buff, isAlexovikFw, autoVoltage, ref newDataFound);
            }
            else if (frameType == 0x02 || frameType == 0x03)
            {
                DecodeBmsCells(buff);
            }
            else if (frameType == 0x04)
            {
                DecodeFrameB(buff, useRatio, isAlexovikFw);
            }
            else if (frameType == 0x05 || frameType == 0x06)
            {
                DecodeThirdFourthPackFrame(frameType, buff[19]);
            }
            else if (frameType == 0x07)
            {
                DecodeFrame07(buff, isAlexovikFw, gotwayNegative, ref newDataFound);
            }
            // frameType == 0xFF: advanced PID/tuning settings — out of scope for this slice.

            if (newDataFound)
            {
                _state.CalculatePower();
                if (_config.HwPwm || _truePwm) _state.UpdatePwm();
                else _state.CalculatePwm();
            }

            RunHandshakeAttempt();
        }
        return newDataFound;
    }

    private void HandleHandshakeText(byte[] data)
    {
        string dataS = Encoding.ASCII.GetString(data).Trim();
        if (dataS.StartsWith("NAME"))
        {
            _attempt = 1000; // stop retrying
            _model = dataS.Length > 5 ? dataS[5..].Trim() : "";
            _state.SetModel(_model);
            LogHandshake("NAME", _model);
        }
        else if (dataS.StartsWith("GW"))
        {
            _fw = dataS.Length > 2 ? dataS[2..].Trim() : "";
            _state.SetVersion(_fw);
            _fwprot = "Begode";
            _config.HwPwm = false;
            _config.IsAlexovikFW = false;
            _isReady = true;
            _attempt = 0;
            LogHandshake("GW", _fw);
        }
        else if (dataS.StartsWith("JN"))
        {
            _fw = dataS.Length > 2 ? dataS[2..].Trim() : "";
            _state.SetVersion(_fw);
            _fwprot = "ExtremeBull";
            _config.HwPwm = false;
            _config.IsAlexovikFW = false;
            _isReady = true;
            _attempt = 0;
            LogHandshake("JN", _fw);
        }
        else if (dataS.StartsWith("CF"))
        {
            _fw = dataS.Length > 2 ? dataS[2..].Trim() : "";
            _state.SetVersion(_fw);
            _fwprot = "Freestyl3r";
            _config.HwPwm = true;
            _config.IsAlexovikFW = false;
            _isReady = true;
            _attempt = 0;
            LogHandshake("CF", _fw);
        }
        else if (dataS.StartsWith("BF"))
        {
            _fw = dataS.Length > 2 ? dataS[2..].Trim() : "";
            _state.SetVersion(_fw);
            _fwprot = "SV";
            _config.HwPwm = true;
            _config.IsAlexovikFW = true;
            _isReady = true;
            _attempt = 0;
            LogHandshake("BF", _fw);
        }
        else if (dataS.StartsWith("MPU"))
        {
            _imu = dataS.Length >= 7 ? dataS[1..7].Trim() : dataS.Trim();
            LogHandshake("MPU", _imu);
        }
    }

    /// <summary>Frame A — live data (GotwayAdapter.java:119-204).</summary>
    private bool DecodeFrameA(byte[] buff, bool isAlexovikFw, bool useRatio, bool useBetterPercents,
        bool autoVoltage, int gotwayNegative)
    {
        LogFrameA(_model, _fw);
        int voltage = MathsUtil.ShortFromBytesBE(buff, 2);
        int speed = (int)Math.Round(MathsUtil.SignedShortFromBytesBE(buff, 4) * 3.6);
        int distance = 0;
        if (!isAlexovikFw)
        {
            distance = MathsUtil.ShortFromBytesBE(buff, 8);
        }
        else if ((buff[7] & 0x01) == 1)
        {
            int batteryCurrent = MathsUtil.SignedShortFromBytesBE(buff, 8);
            _state.SetCurrent(batteryCurrent);
            _trueCurrent = true;
        }

        int phaseCurrent = MathsUtil.SignedShortFromBytesBE(buff, 10);
        int temperature = !isAlexovikFw
            ? (int)Math.Round((MathsUtil.SignedShortFromBytesBE(buff, 12) / 340.0 + 36.53) * 100) // mpu6050
            : (int)Math.Round((MathsUtil.SignedShortFromBytesBE(buff, 12) / 333.87 + 21.00) * 100); // mpu6500 (Alexovik "trick" byte 16 not ported)

        // NOT real PWM despite the name inherited from the port (plan 35 §9, begode-comparison.md
        // §2.1): the native app puts a settings echo here — angle10/angle5/power3/momm
        // (showbad/HomeSettingActivity.java:1686-1723) — not duty cycle. Real PWM/output lives in
        // frame 0x07 offset [8:9], which MTen3 never sends. Kept 1:1 (still feeds Output below) —
        // it only reaches the rider's gauge when HwPwm/_truePwm is set, which stock firmware
        // never does; CalculatedPwm shown to the rider comes from WheelState.CalculatePwm()'s
        // speed formula instead, untouched by this field. Risk is confined to Alexovik CF/BF
        // firmware (HwPwm=true), already outside this port's declared scope (class doc above).
        int hwPwm = MathsUtil.SignedShortFromBytesBE(buff, 14) * 10;

        if (gotwayNegative == 0)
        {
            speed = Math.Abs(speed);
            phaseCurrent = Math.Abs(phaseCurrent);
            hwPwm = Math.Abs(hwPwm);
        }
        else
        {
            phaseCurrent *= gotwayNegative;
            if (!isAlexovikFw)
            {
                speed *= gotwayNegative;
                hwPwm *= gotwayNegative;
            }
        }

        int battery = CalculateBattery(voltage, useBetterPercents);

        if (useRatio)
        {
            distance = (int)Math.Round(distance * RatioGw);
            speed = (int)Math.Round(speed * RatioGw);
        }
        voltage = (int)Math.Round(GetScaledVoltage(voltage));

        _state.SetSpeed(speed);
        _state.SetTopSpeed(speed);
        _state.SetWheelDistance(distance);
        _state.SetTemperature(temperature);
        _state.SetPhaseCurrent(isAlexovikFw ? phaseCurrent * 10 : phaseCurrent);
        if (!(_trueVoltage && autoVoltage))
        {
            _state.SetVoltage(voltage);
        }
        _state.SetBatteryLevel(battery, CellInputs());
        if (!_truePwm)
        {
            _state.SetOutput(hwPwm);
        }
        if (!isAlexovikFw && (!_trueCurrent || !_bmsCurrent))
        {
            _state.CalculateCurrent();
        }
        return !((_trueVoltage && autoVoltage) || _trueCurrent || _bmsCurrent) || isAlexovikFw;
    }

    /// <summary>Frame 0x01 — BMS pack voltage/current (GotwayAdapter.java:205-237).</summary>
    private void DecodeFrame01(byte[] buff, bool isAlexovikFw, bool autoVoltage, ref bool newDataFound)
    {
        if (isAlexovikFw)
        {
            // Alexovik-firmware pedals-mode echo — not ported in this slice (stock Begode never hits this).
            return;
        }

        newDataFound = _bmsCurrent || (!_trueCurrent && _trueVoltage && autoVoltage);
        _trueVoltage = true;
        int batVoltage = MathsUtil.ShortFromBytesBE(buff, 6);
        if (autoVoltage) _state.SetVoltage(batVoltage * 10);

        int bmsnum = buff[19] & 0xFF;
        SmartBms bms = bmsnum < 2 ? _state.Bms1 : _state.Bms2;
        int bmsCurrentM = MathsUtil.SignedShortFromBytesBE(buff, 8);
        LogFrame01(bmsnum, batVoltage, bmsCurrentM);
        bms.Current = bmsCurrentM / 10.0;
        if (bmsCurrentM > 0) _bmsCurrent = false;
        if (_bmsCurrent) _state.SetCurrent(bmsCurrentM * 20); // double current, taking into account 2 BMS packs

        if (bmsnum % 2 == 0)
        {
            bms.Temp1 = MathsUtil.SignedShortFromBytesBE(buff, 10);
            bms.Temp2 = MathsUtil.SignedShortFromBytesBE(buff, 12);
            bms.SemiVoltage1 = MathsUtil.SignedShortFromBytesBE(buff, 14) / 10.0;
        }
        else
        {
            bms.Temp3 = MathsUtil.SignedShortFromBytesBE(buff, 10);
            bms.Temp4 = MathsUtil.SignedShortFromBytesBE(buff, 12);
            bms.SemiVoltage2 = MathsUtil.SignedShortFromBytesBE(buff, 14) / 10.0;
        }
    }

    /// <summary>Frames 0x02/0x03 — BMS cell voltages, 8 cells per page (GotwayAdapter.java:238-281).</summary>
    private void DecodeBmsCells(byte[] buff)
    {
        int bmsnum = (buff[18] & 0xFF) - 0x01;
        SmartBms bms = bmsnum == 1 ? _state.Bms1 : _state.Bms2;
        int pNum = buff[19] & 0xFF;
        LogBmsCells(bmsnum, pNum);

        for (int i = 0; i < 8; i++)
        {
            int cellNum = i + pNum * 8;
            double cellVal = MathsUtil.ShortFromBytesBE(buff, (i + 1) * 2) / 1000.0;
            if (cellNum >= bms.Cells.Length) continue; // defensive; Android's array is a fixed 56 too
            bms.Cells[cellNum] = cellVal;
            if (_smartBmsCells <= cellNum && cellVal != 0)
            {
                _smartBmsCells = cellNum + 1;
            }
            else if (_smartBmsCells == cellNum + 1 && bms.CellNum != _smartBmsCells)
            {
                bms.CellNum = _smartBmsCells;
                // wd.reconfigureBMSPage() — Android UI hook, not applicable to a console test port.
            }
        }

        bms.MinCell = bms.Cells[0];
        bms.MaxCell = bms.Cells[0];
        bms.MaxCellNum = 1;
        bms.MinCellNum = 1;
        double totalVolt = 0.0;
        for (int i2 = 0; i2 < _smartBmsCells; i2++)
        {
            double cell = bms.Cells[i2];
            if (cell > 0.0)
            {
                totalVolt += cell;
                if (bms.MaxCell < cell) { bms.MaxCell = cell; bms.MaxCellNum = i2 + 1; }
                if (bms.MinCell > cell) { bms.MinCell = cell; bms.MinCellNum = i2 + 1; }
            }
        }
        bms.CellDiff = bms.MaxCell - bms.MinCell;
        bms.AvgCell = totalVolt / _smartBmsCells; // NaN until the first cell page arrives, matches original
        bms.Voltage = totalVolt;
    }

    /// <summary>
    /// Frames 0x05/0x06 — cell voltages of the third/fourth physical battery pack (C/D), present
    /// on wheels with 3-4 parallel packs (large Begode: EX30, Msuper Pro, RS19). Plan 35 §9: these
    /// used to be entirely absent from the dispatch switch, exactly matching upstream WheelLog
    /// (<c>GotwayAdapter.java</c> has no <c>case 5</c>/<c>case 6</c> either — this is not a port
    /// omission, WheelLog itself never supported them). Cell-voltage byte layout is proven
    /// identical to 0x02/0x03 (begode-comparison.md §1.2, <c>BatteryPackActivity.java:995-1004,
    /// 1211-1249</c>), but <see cref="WheelState"/> only carries two pack slots (Bms1/Bms2), both
    /// already claimed by 0x02 (pack A) and 0x03 (pack B). Folding pack C/D cells into either slot
    /// would interleave three-four physically distinct battery packs' cells into one struct —
    /// worse than not decoding at all, and not something proven by any source read for this task.
    /// So the frame is recognized and logged, not silently dropped, and nothing is guessed into
    /// <c>WheelState</c>. A fourth pack slot is future work, not this fix.
    /// </summary>
    private void DecodeThirdFourthPackFrame(byte frameType, byte page)
    {
        char pack = frameType == 0x05 ? 'C' : 'D';
        LogThirdFourthPackFrame(pack, page);
    }

    /// <summary>Frame 0x04 — total distance + alarm bits (GotwayAdapter.java:282-338, settings-echo half omitted).</summary>
    private void DecodeFrameB(byte[] buff, bool useRatio, bool isAlexovikFw)
    {
        LogFrameB();
        int totalDistance = MathsUtil.GetInt4(buff, 2);
        _state.SetTotalDistance(useRatio ? (long)Math.Round(totalDistance * RatioGw) : totalDistance);

        if (isAlexovikFw) return;

        int alert = buff[14] & 0xFF;
        _state.SetWheelAlarm((alert & 0x01) == 1);

        // Bits 1/2 renamed from the port's original "Speed2"/"Speed1" to the manufacturer's own
        // names (plan 35 §9, owner decision 15.08.2026: manufacturer naming over WheelLog's —
        // begode-comparison.md §2.2, HomeFragment.java:1312-1369). These are hardware-fault bits
        // ("mos"/"gyroscope" in the native app), not a speed-limiting notice — WheelLog mislabeled
        // them, silently downgrading a MOSFET/gyroscope failure to a routine "over speed" line.
        // Not observed on the wire in any of our four MTen3 recordings (mten3-*.csv) — confirmed
        // only by reading the manufacturer's decompiled source, not by a live faulty wheel.
        var alertLine = new StringBuilder();
        if (((alert >> 1) & 0x01) == 1) alertLine.Append("errMosfet ");
        if (((alert >> 2) & 0x01) == 1) alertLine.Append("errGyroscope ");
        if (((alert >> 3) & 0x01) == 1) alertLine.Append("LowVoltage ");
        if (((alert >> 4) & 0x01) == 1) alertLine.Append("OverVoltage ");
        if (((alert >> 5) & 0x01) == 1) alertLine.Append("OverTemperature ");
        if (((alert >> 6) & 0x01) == 1) alertLine.Append("errHallSensors ");
        if (((alert >> 7) & 0x01) == 1) alertLine.Append("TransportMode");
        _state.SetAlert(alertLine.ToString());

        if (alertLine.Length > 0)
        {
            // Throttled to alarm-state transitions — a sustained alarm would otherwise re-log
            // the same Warning on every single decoded frame.
            string alertText = alertLine.ToString();
            if (alertText != _lastLoggedAlert)
            {
                LogWheelAlert(alertText);
                _lastLoggedAlert = alertText;
            }
        }
        else
        {
            _lastLoggedAlert = null;
        }

        // Pedals mode / speed alarms / roll angle / miles / LED mode / light mode / power-off
        // time / tiltback speed are settings *echoes* the Android app mirrors into AppConfig —
        // out of scope for this telemetry-focused slice.
    }

    /// <summary>Frame 0x07 — motor current/temperature (GotwayAdapter.java:339-360).</summary>
    private void DecodeFrame07(byte[] buff, bool isAlexovikFw, int gotwayNegative, ref bool newDataFound)
    {
        if (isAlexovikFw) return;

        newDataFound = _trueCurrent && !_bmsCurrent;
        _trueCurrent = true;
        int batteryCurrent = MathsUtil.SignedShortFromBytesBE(buff, 2);
        int motorTemp = MathsUtil.SignedShortFromBytesBE(buff, 6);
        int hwPwmB = MathsUtil.SignedShortFromBytesBE(buff, 8);
        LogFrame07(batteryCurrent, motorTemp, hwPwmB);
        if (Math.Abs(hwPwmB) > 0) _truePwm = true;
        if (_truePwm)
        {
            hwPwmB = gotwayNegative == 0 ? Math.Abs(hwPwmB) : hwPwmB * gotwayNegative * -1;
            _state.SetOutput(hwPwmB * 100);
        }
        if (!_bmsCurrent) _state.SetCurrent(-1 * batteryCurrent);
        _state.SetTemperature2(motorTemp * 100);
    }

    /// <summary>Port of the battery-percent branches (GotwayAdapter.java:158-177).</summary>
    private int CalculateBattery(int voltage, bool useBetterPercents)
    {
        if (useBetterPercents)
        {
            if (voltage > 6680) return 100;
            if (voltage > 5440) return (int)Math.Round((voltage - 5320) / 13.6);
            if (voltage > 5120) return (voltage - 5120) / 36; // integer division, matches original (no rounding)
            return 0;
        }
        if (voltage <= 5290) return 0;
        if (voltage >= 6580) return 100;
        return (voltage - 5290) / 13; // integer division, matches original (no rounding)
    }

    private double GetScaledVoltage(int value) => value * (_config.GotwayVoltage switch
    {
        "0" => 1.0,
        "1" => 1.25,
        "2" => 1.5,
        "3" => 1.7380952380952380952380952380952,
        "4" => 2.0,
        "5" => 2.5,
        "6" => 2.25,
        _ => 1.0,
    });

    /// <summary>
    /// Ответ идёт через общий каскад (план 27 §27.3): своего счёта ячеек у декодера больше нет —
    /// он лишь подаёт наверх то, что знает сам. Порядок ступеней повторяет прежний порядок этого
    /// метода: задал человек — берётся его число (§27.4), иначе умный BMS, иначе настройка вольтажа.
    /// </summary>
    public CellCount GetCellsForWheel() => CellCountResolver.Resolve(CellInputs());

    /// <summary>Всё, что декодер знает о ряде. Считает по этому каскад — здесь только сбор.</summary>
    internal CellCountInputs CellInputs() => new()
    {
        ConfiguredCells = _config.CellsInSeries,
        SmartBmsCells = _smartBmsCells,
        ProtocolCells = CellsFromVoltageSetting(),
        PackVolts = _state.Voltage / 100.0,
        // WheelPercent намеренно пуст: заряд у Gotway считает наша же кривая из напряжения
        // (CalculateBattery), и подать его значило бы делить напряжение на выведенное из него —
        // ступень подтверждала бы любую догадку, включая неверную (план 27 §27.5). Процент годится
        // сюда, только когда его называет само колесо.
    };

    /// <summary>
    /// Port of the settings table in GotwayAdapter.getCellsForWheel() (GotwayAdapter.java:424-436).
    /// Расхождение настройки <c>"3"</c> — 32 ячейки при множителе на 116,8 В, где по напряжению их
    /// 28, — перенесено 1:1 вместе с остальным и здесь не чинится: у людей накоплены поездки с
    /// процентами, посчитанными по 32 (`docs/port-deviations.md`, план 27 §27.3).
    /// </summary>
    private int CellsFromVoltageSetting() => _config.GotwayVoltage switch
    {
        "0" => 16,
        "1" => 20,
        "2" => 24,
        "3" => 32,
        "4" => 32,
        "5" => 40,
        "6" => 36,
        _ => 24,
    };

    /// <summary>Port of the "V"/"N" handshake polling loop (GotwayAdapter.java:395-422), run once per completed frame.</summary>
    private void RunHandshakeAttempt()
    {
        if (_attempt < 50)
        {
            long nowTimestamp = _timeProvider.GetTimestamp();
            if (_timeProvider.GetElapsedTime(_lastTryTimestamp) > TimeSpan.FromMilliseconds(40))
            {
                if (_fw.Length == 0) SendCommand("V", "", 0);
                else if (_model.Length == 0) SendCommand("N", "", 0);
                _attempt += 1;
                _lastTryTimestamp = nowTimestamp;
            }
        }
        else
        {
            if (_model.Length == 0)
            {
                _model = _fwprot.Length == 0 ? "Begode" : _fwprot;
                _state.SetVersion(_model); // mirrors GotwayAdapter.java:415 (likely upstream setModel/setVersion mix-up, preserved 1:1)
                LogHandshake("Fallback-Model", _model);
            }
            else if (_fw.Length == 0)
            {
                _fw = "-";
                _state.SetVersion(_fw);
                _config.HwPwm = false;
                _isReady = true;
                LogHandshake("Fallback-Fw", _fw);
            }
        }
    }

    /// <summary>Opens the identity <see cref="ILogger.BeginScope{TState}"/> for the current
    /// <see cref="Decode"/> call, using whatever model/fw the handshake has already resolved by the
    /// time this call started. Returns null (no scope) until both are known — see the remark on
    /// <see cref="Decode"/> for why this is per-call rather than a long-lived field.</summary>
    private IDisposable? BeginIdentityScope()
    {
        if (_model.Length == 0 && _fw.Length == 0) return null;

        return _logger.BeginScope(new Dictionary<string, object?>
        {
            ["WheelType"] = _state.WheelType,
            ["Model"] = _model,
            ["Fw"] = _fw,
        });
    }

    // --- Commands ---

    public byte[] BuildWheelBeep() => Encoding.ASCII.GetBytes("b");

    public byte[] BuildSetLightState(bool enabled)
    {
        // Как у Veteran: эха от колеса нет, единственный источник правды про свет —
        // IWheelConfig.LightEnabled, пишется при построении команды.
        _config.LightEnabled = enabled;
        _lightMode = enabled ? LightModeOn : LightModeOff;
        return BuildLightModeCommand(_lightMode);
    }

    public byte[] BuildSwitchFlashlight()
    {
        _lightMode = _lightMode + 1 > LightModeStrobe ? LightModeOff : _lightMode + 1;
        return BuildLightModeCommand(_lightMode);
    }

    private byte[] BuildLightModeCommand(int mode)
    {
        string command = mode switch
        {
            LightModeOn => "Q",
            LightModeStrobe => "T",
            _ => "E",
        };
        DelayedSend(Encoding.ASCII.GetBytes("b"), 100); // setLightMode() always follows up via sendCommand()'s default "b" + 100ms
        return Encoding.ASCII.GetBytes(command);
    }

    public byte[]? BuildUpdatePedalsMode(int pedalsMode)
    {
        string? command = pedalsMode switch { 0 => "h", 1 => "f", 2 => "s", 3 => "i", _ => null };
        if (command is null) return null;
        DelayedSend(Encoding.ASCII.GetBytes("b"), 100);
        return Encoding.ASCII.GetBytes(command);
    }

    /// <summary>No resetTrip() in GotwayAdapter — BaseAdapter doesn't declare that hook either.</summary>
    public byte[]? BuildResetTrip() => null;

    public byte[]? BuildCalibrate()
    {
        DelayedSend(Encoding.ASCII.GetBytes("y"), 300);
        return Encoding.ASCII.GetBytes("c");
    }

    private void DelayedSend(byte[] bytes, int delayMs)
    {
        _ = DelayedSendAsync(bytes, delayMs);
    }

    private async Task DelayedSendAsync(byte[] bytes, int delayMs)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), _timeProvider);
            RequestWrite(bytes);
        }
        catch (Exception ex)
        {
            LogDelayedSendFailed(ex);
        }
    }

    /// <summary>
    /// Only raises the request — it cannot log a confirmed Cmd.Sent, because it has no way to know
    /// whether the write actually landed. That confirmation exists only where <see cref="WriteRequested"/>
    /// is consumed (<c>WheelService.WriteSafe</c>, which awaits the transport), so that is where the
    /// event is logged now. Logging it here — before the transport had even been asked — is exactly
    /// the bug roadmap "Пункт 9" describes for user commands, just for the decoder's own handshake
    /// polling and two-step follow-ups instead.
    /// </summary>
    private void RequestWrite(byte[] bytes) => WriteRequested?.Invoke(bytes);

    private void SendCommand(string primary, string delayed = "b", int timerMs = 100)
    {
        RequestWrite(Encoding.ASCII.GetBytes(primary));
        if (timerMs > 0 && delayed.Length > 0)
        {
            DelayedSend(Encoding.ASCII.GetBytes(delayed), timerMs);
        }
    }

    [LoggerMessage(EventId = LogEvents.Decoding.DecodeInvokedId, EventName = LogEvents.Decoding.DecodeInvokedName,
        Level = LogLevel.Trace, Message = "Decode Gotway/Begode")]
    private partial void LogDecodeInvoked();

    [LoggerMessage(EventId = LogEvents.Decoding.FrameAId, EventName = LogEvents.Decoding.FrameAName,
        Level = LogLevel.Debug, Message = "Begode frame A found (live data). Model {Model} FW {Fw}")]
    private partial void LogFrameA(string model, string fw);

    [LoggerMessage(EventId = LogEvents.Decoding.FrameBId, EventName = LogEvents.Decoding.FrameBName,
        Level = LogLevel.Debug, Message = "Begode frame B found (total distance and flags)")]
    private partial void LogFrameB();

    [LoggerMessage(EventId = LogEvents.Decoding.Frame01Id, EventName = LogEvents.Decoding.Frame01Name,
        Level = LogLevel.Debug, Message = "Begode frame 01 found (BMS voltage/current). Bms#{BmsNum} Voltage={Voltage} Current={Current}")]
    private partial void LogFrame01(int bmsNum, int voltage, int current);

    [LoggerMessage(EventId = LogEvents.Decoding.Frame07Id, EventName = LogEvents.Decoding.Frame07Name,
        Level = LogLevel.Debug, Message = "Begode frame 07 found (motor current/temperature). Current={Current} MotorTemp={MotorTemp} Pwm={Pwm}")]
    private partial void LogFrame07(int current, int motorTemp, int pwm);

    [LoggerMessage(EventId = LogEvents.Decoding.BmsCellsId, EventName = LogEvents.Decoding.BmsCellsName,
        Level = LogLevel.Debug, Message = "Begode BMS cells frame. Bms#{BmsNum} Page={Page}")]
    private partial void LogBmsCells(int bmsNum, int page);

    [LoggerMessage(EventId = LogEvents.Decoding.ThirdFourthPackFrameId, EventName = LogEvents.Decoding.ThirdFourthPackFrameName,
        Level = LogLevel.Debug, Message = "Begode pack {Pack} cells frame (type 0x05/0x06) recognized, page {Page} — not decoded, no free BMS slot")]
    private partial void LogThirdFourthPackFrame(char pack, int page);

    [LoggerMessage(EventId = LogEvents.Decoding.HandshakeId, EventName = LogEvents.Decoding.HandshakeName,
        Level = LogLevel.Debug, Message = "Handshake {Kind} recognized: {Value}")]
    private partial void LogHandshake(string kind, string value);

    [LoggerMessage(EventId = LogEvents.Decoding.WheelAlertId, EventName = LogEvents.Decoding.WheelAlertName,
        Level = LogLevel.Warning, Message = "Wheel alert: {Alert}")]
    private partial void LogWheelAlert(string alert);

    [LoggerMessage(EventId = LogEvents.Service.DelayedSendFailedId, EventName = LogEvents.Service.DelayedSendFailedName,
        Level = LogLevel.Error, Message = "Delayed send failed")]
    private partial void LogDelayedSendFailed(Exception ex);
}
