using WheelTalk.Core.Battery;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of the subset of WheelData.java's mutable state and derived-calculation logic needed by
/// the wheel decoders (§5.7 of the port plan for Veteran; also backs GotwayDecoder — this state
/// object is shared across both protocol families, not Veteran-specific). Field names mirror the
/// Android mXxx fields; unit conventions (fixed-point 1/100, meters) match 1:1.
///
/// TODO: not thread-safe. Every setter here is called synchronously from whatever thread raises
/// ITransport.DataReceived (the BLE notification callback thread for WindowsBleClient), with no
/// locking. Fine for this single-listener console test port, but a real UI consumer reading
/// WheelState concurrently (or multiple wheels sharing a dispatcher) would need to guard these
/// mutations before building on top of this.
/// </summary>
public sealed class WheelState
{
    public const double MaxCellVoltage = 4.2;

    private readonly IWheelConfig _config;
    private readonly TimeProvider _timeProvider;

    private long _rideStartTime;
    private int _batteryStart = -1;
    private int _batteryLowest = 101;

    public WheelState(IWheelConfig config, TimeProvider timeProvider)
    {
        _config = config;
        _timeProvider = timeProvider;
    }

    public int Speed { get; private set; } // 1/100 km/h
    public int TopSpeed { get; private set; } // 1/100 km/h
    public long WheelDistance { get; private set; } // meters
    public long TotalDistance { get; private set; } // meters
    public long StartTotalDistance { get; private set; } // meters
    public int Temperature { get; private set; }
    public int PhaseCurrent { get; private set; } // 1/100 A
    public int Voltage { get; private set; } // 1/100 V
    public int Current { get; private set; } // 1/100 A
    public int Power { get; private set; } // 1/100 W
    public int Battery { get; private set; }

    /// <summary>
    /// Сколько ячеек в ряду и откуда это известно — то, чем декодер считал заряд в последний раз
    /// (план 27 §27.4). Своего счёта тут нет: считает каскад, состояние лишь хранит ответ вместе с
    /// источником и отдаёт его в снимок.
    /// </summary>
    public CellCount PackCells { get; private set; } = CellCount.Unknown;

    /// <summary>
    /// Тот же каскад <b>без верхней ступени</b> — что приложение сказало бы, не скажи ему человек.
    /// Второго счёта ячеек это не заводит (план 27, «Что сознательно не делаем»): резолвер тот же,
    /// входы те же, пропущен ровно один вход.
    /// <para>
    /// Нужен ровно одному месту — кнопке «рассчитать». Без него она бесполезна как раз тогда, когда
    /// нужна: число задано, человек жмёт кнопку и получает обратно своё же число.
    /// </para>
    /// </summary>
    public CellCount AutoPackCells { get; private set; } = CellCount.Unknown;

    public int ChargingStatus { get; private set; }
    public int SleepTimer { get; private set; }
    public double Angle { get; private set; }
    public double CalculatedPwm { get; private set; } // fraction 0..1
    public double MaxPwm { get; private set; } // fraction 0..1
    public int Output { get; private set; }
    public string Version { get; private set; } = "";
    public string Model { get; private set; } = "";
    public WheelType WheelType { get; set; } = WheelType.Unknown;
    public int Temperature2 { get; private set; }
    public bool WheelAlarm { get; private set; }
    public string Alert { get; private set; } = "";

    // KingSong-only fields (WheelData.mName/mModeStr/mFanStatus/mCpuLoad/mSpeedLimit/mSerialNumber) —
    // Veteran/Gotway never write these, so they stay at their defaults for those protocols.
    public string Name { get; private set; } = "";
    public string ModeStr { get; private set; } = "";
    public int FanStatus { get; private set; }
    public int CpuLoad { get; private set; }
    public double SpeedLimit { get; private set; }
    public string Serial { get; private set; } = "";

    // InMotion-only fields (WheelData.mRoll/mImuTemp) — other protocols never write these, so
    // they stay at their defaults (0) elsewhere. ImuTemp is intentionally unscaled (unlike
    // Temperature/Temperature2, which are 1/100 degrees) — WheelData.getImuTemp() returns the raw
    // value straight from the wire (a signed byte), with no /100 division.
    public double Roll { get; private set; }
    public int ImuTemp { get; private set; }

    // InMotion V2-only fields (WheelData.mTorque/mMotorPower/mCpuTemp/mCurrentLimit) — plain
    // pass-through values, no fixed-point convention of their own (unlike most fields above, the
    // decoder does the scaling before calling the setter, matching WheelData.java's own setters).
    public double Torque { get; private set; }
    public double MotorPower { get; private set; }
    public int CpuTemp { get; private set; }
    public double CurrentLimit { get; private set; }

    public SmartBms Bms1 { get; } = new();
    public SmartBms Bms2 { get; } = new();

    /// <summary>
    /// Настройки, как их сообщило само колесо (Veteran, страница 8). <c>null</c> — такого кадра
    /// ещё не было: у большинства марок его нет вовсе, а у Veteran он приходит раз в 4 секунды.
    /// Снимок заменяется целиком — см. <see cref="Settings.Device.WheelSettingsSnapshot"/>.
    /// </summary>
    public Settings.Device.WheelSettingsSnapshot? WheelSettings { get; private set; }

    public void SetWheelSettings(Settings.Device.WheelSettingsSnapshot settings) => WheelSettings = settings;

    /// <summary>
    /// Режим езды, как его прислало колесо в байте 31 кадра телеметрии (Veteran). Байт сырой:
    /// смысл его зависит от модели, и толковать его здесь было бы догадкой — что известно и что
    /// нет, расписано у <see cref="VeteranDecoder"/>. <c>null</c> — кадра телеметрии ещё не было
    /// (у прочих марок его не будет вовсе).
    /// <para>
    /// Живёт здесь, а не в <see cref="WheelSettings"/>, потому что источник другой: снимок
    /// настроек — это одна страница 8 одного мгновения (раз в 4 секунды), а этот байт приходит с
    /// каждым кадром телеметрии. Подмешать его в снимок значило бы показать состояние, которого у
    /// колеса в один момент не было.
    /// </para>
    /// <para>
    /// С <see cref="ModeStr"/> не путать: то — режим KingSong, число другой шкалы и другой марки.
    /// </para>
    /// </summary>
    public byte? RideModeRaw { get; private set; }

    public void SetRideModeRaw(byte rideMode) => RideModeRaw = rideMode;

    public void ResetRideTime()
    {
        if (_rideStartTime == 0)
        {
            _rideStartTime = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        }
    }

    public void SetSpeed(int speed) => Speed = speed;

    public void SetTopSpeed(int topSpeed)
    {
        if (topSpeed > TopSpeed) TopSpeed = topSpeed;
    }

    public void SetWheelDistance(long distance) => WheelDistance = distance;

    public void SetTotalDistance(long totalDistance)
    {
        if (StartTotalDistance == 0 && TotalDistance != 0)
        {
            StartTotalDistance = TotalDistance;
        }
        TotalDistance = totalDistance;
    }

    public long DistanceFromStart => TotalDistance - StartTotalDistance;

    /// <summary>
    /// Точка отсчёта «от старта», унаследованная от прежнего состояния. Нужна потому, что здесь
    /// состояние строится заново на каждую попытку подключения
    /// (<c>WheelSession.BuildService</c>), а у оригинала <c>WheelData</c> живёт всё время работы
    /// приложения: без переноса «от старта» обнулялся бы на каждом автопереподключении посреди
    /// поездки. Ноль означает «отсчёта ещё не было» — его выставит первый ненулевой одометр
    /// (<see cref="SetTotalDistance"/>).
    /// </summary>
    public void SetStartTotalDistance(long startTotalDistance) => StartTotalDistance = startTotalDistance;

    public void SetTemperature(int value) => Temperature = value;

    public void SetPhaseCurrent(int value) => PhaseCurrent = value;

    public void SetVoltage(int voltage) => Voltage = voltage;

    public void SetChargingStatus(int charging) => ChargingStatus = charging;

    public void SetSleepTimer(int sleepSec) => SleepTimer = sleepSec;

    public void SetAngle(double angle) => Angle = angle;

    public void SetOutput(int value) => Output = value;

    public void SetVersion(string value) => Version = value;

    public void SetModel(string model) => Model = model;

    public void SetTemperature2(int value) => Temperature2 = value;

    public void SetWheelAlarm(bool value) => WheelAlarm = value;

    /// <summary>
    /// Строка тревог колеса. <b>Обрезается здесь</b> — на нашем шве, а не в декодере: оригинал
    /// собирает её словами с хвостовым пробелом (<c>"errMosfet "</c>, <c>GotwayDecoder</c> кадр
    /// 0x04), и в журнал поездки уезжало <c>"errMosfet "</c> вместо <c>"errMosfet"</c> (план 11
    /// §5.6). Декодеры —
    /// построчный порт, править их нельзя; <see cref="SetAlert"/> же — единственная дверь, через
    /// которую тревога любого протокола входит в состояние, и одна обрезка тут закрывает вопрос
    /// сразу у всех.
    /// </summary>
    public void SetAlert(string value) => Alert = value.Trim();

    /// <summary>Port of WheelData.setCurrent(int) minus max-current tracking (not part of this slice).</summary>
    public void SetCurrent(int value) => Current = value;

    public void SetName(string value) => Name = value;

    public void SetModeStr(string value) => ModeStr = value;

    public void SetFanStatus(int value) => FanStatus = value;

    public void SetCpuLoad(int value) => CpuLoad = value;

    public void SetSpeedLimit(double value) => SpeedLimit = value;

    public void SetSerial(string value) => Serial = value;

    public void SetRoll(double value) => Roll = value;

    public void SetImuTemp(int value) => ImuTemp = value;

    public void SetTorque(double value) => Torque = value;

    public void SetMotorPower(double value) => MotorPower = value;

    public void SetCpuTemp(int value) => CpuTemp = value;

    public void SetCurrentLimit(double value) => CurrentLimit = value;

    /// <summary>Port of WheelData.setPower(int) minus max-power tracking (not part of this slice —
    /// nothing reads a max-power field). Used by InMotion V2, which reports power directly rather
    /// than deriving it via <see cref="CalculatePower"/> like every other protocol here.</summary>
    public void SetPower(int value) => Power = value;

    public void SetMaxPwm(double currentPwm)
    {
        if (currentPwm > MaxPwm && currentPwm > 0) MaxPwm = currentPwm;
    }

    /// <summary>
    /// «Сброс максимумов» с панели (quick-commands-design.md §3, аналог оригинального miReset):
    /// обнуляет только пиковые показания — следующий кадр начинает копить их заново, а не
    /// возвращает старые. На запись поездки в базу не влияет, она сюда не заглядывает.
    /// </summary>
    public void ResetPeaks()
    {
        MaxPwm = 0;
        TopSpeed = 0;
    }

    /// <summary>Port of WheelData.updatePwm() — used when HwPwm reporting is enabled.</summary>
    public void UpdatePwm()
    {
        CalculatedPwm = Output / 10000.0;
        SetMaxPwm(CalculatedPwm);
    }

    /// <summary>Port of WheelData.calculatePwm() — derives PWM from speed/voltage when no HW PWM field exists.</summary>
    public void CalculatePwm()
    {
        double rotationSpeed = _config.RotationSpeed / 10d;
        double rotationVoltage = _config.RotationVoltage / 10d;
        double powerFactor = _config.PowerFactor / 100d;
        CalculatedPwm = Speed / (rotationSpeed / rotationVoltage * Voltage * powerFactor);
        SetMaxPwm(CalculatedPwm);
    }

    /// <summary>Port of WheelData.calculateCurrent().</summary>
    public void CalculateCurrent() => Current = (int)Math.Round(CalculatedPwm * PhaseCurrent);

    /// <summary>Port of WheelData.calculatePower().</summary>
    public void CalculatePower() => Power = (int)Math.Round(Current / 100.0 * Voltage);

    private double GetMaxVoltageForWheel(int cellsForWheel) => MaxCellVoltage * cellsForWheel;

    private double GetVoltageTiltbackForWheel(int cellsForWheel) => _config.CellVoltageTiltback / 100d * cellsForWheel;

    /// <summary>
    /// Port of WheelData.setBatteryLevel(int) including the custom-percents branch.
    /// <para>
    /// Принимаются <b>входы</b> каскада, а не готовое число: отсюда получаются оба ответа — тот,
    /// которым считается заряд, и тот, что приложение дало бы без человека. Считать их порознь в
    /// каждом декодере значило бы пять раз написать одно и то же и однажды разойтись (§27.4).
    /// </para>
    /// </summary>
    public void SetBatteryLevel(int battery, CellCountInputs cellInputs)
    {
        PackCells = CellCountResolver.Resolve(cellInputs);
        AutoPackCells = CellCountResolver.Resolve(cellInputs with { ConfiguredCells = null });
        int cellsForWheel = PackCells.Cells;

        if (_config.CustomPercents)
        {
            double maxVoltage = GetMaxVoltageForWheel(cellsForWheel);
            double minVoltage = GetVoltageTiltbackForWheel(cellsForWheel);
            double voltagePercentStep = (maxVoltage - minVoltage) / 100.0;
            if (voltagePercentStep != 0)
            {
                battery = MathsUtil.Clamp((int)((Voltage / 100.0 - minVoltage) / voltagePercentStep), 0, 100);
            }
        }
        _batteryLowest = Math.Min(_batteryLowest, battery);
        if (_batteryStart == -1) _batteryStart = battery;
        Battery = battery;
    }

    public TelemetrySnapshot ToSnapshot() => new()
    {
        SpeedRaw = Speed,
        VoltageRaw = Voltage,
        CurrentRaw = Current,
        PhaseCurrentRaw = PhaseCurrent,
        PowerRaw = Power,
        Pwm = CalculatedPwm * 100.0,
        MaxPwm = MaxPwm * 100.0,
        Battery = Battery,
        PackCells = PackCells,
        AutoPackCells = AutoPackCells,
        TemperatureRaw = Temperature,
        Temperature2Raw = Temperature2,
        TopSpeedRaw = TopSpeed,
        WheelDistance = WheelDistance,
        TotalDistance = TotalDistance,
        DistanceFromStart = DistanceFromStart,
        Angle = Angle,
        ChargingStatus = ChargingStatus,
        SleepTimerSec = SleepTimer,
        WheelAlarm = WheelAlarm,
        Alert = Alert,
        Version = Version,
        Model = Model,
        WheelType = WheelType,
        Bms1 = Bms1,
        Bms2 = Bms2,
        WheelSettings = WheelSettings,
        RideModeRaw = RideModeRaw,
        Name = Name,
        ModeStr = ModeStr,
        FanStatus = FanStatus,
        CpuLoad = CpuLoad,
        SpeedLimit = SpeedLimit,
        Serial = Serial,
        OutputRaw = Output,
        Roll = Roll,
        ImuTemp = ImuTemp,
        Torque = Torque,
        MotorPower = MotorPower,
        CpuTemp = CpuTemp,
        CurrentLimit = CurrentLimit,
    };
}
