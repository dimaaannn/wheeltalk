using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Battery;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Diagnostics;
using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of VeteranAdapter.java 1:1 (decode() + command builders). Fills a <see cref="WheelState"/>
/// from raw Veteran/Sherman-L BLE frames. Sherman L reports protocol version 6 (_protocolVersion == 6).
/// </summary>
public sealed partial class VeteranDecoder : IWheelDecoder
{
    private const int WaitingTimeMs = 100;

    /// <summary>
    /// Байт режима езды в кадре телеметрии. Отсчёт — от начала кадра вместе с заголовком, тот же,
    /// что у остальных полей <see cref="Decode"/>.
    /// <para>
    /// Читается <b>один байт, а не 16-битное слово с 30</b>, как у оригинала
    /// (<c>VeteranAdapter.java:51</c>): байт 30 — старшая часть трёхбайтового кода версии
    /// (<c>BtManager.java:372</c>, там же он склеивается с 28 и 29), и распаковщик держит его
    /// равным 0x00 или 0x07 (<c>VeteranUnpacker.cs:51</c>). Слово с 30 смешивает версию с режимом
    /// и потому бессмысленно — у оригинала оно и не используется никем.
    /// </para>
    /// <para>
    /// Что байт значит — известно наполовину, поэтому наверх он идёт сырым. Родное приложение
    /// зовёт его <c>rideMode</c> (<c>BtManager.java:377</c>) и читает двояко, по модели колеса
    /// (<c>SetRideModeActivity.java:70-78</c>): у колеса с тремя положениями это 1/2/3
    /// («Soft»/«Medium»/«Strong», <c>HomepageFragment.java:324</c>), у колеса с плавной шкалой —
    /// то же значение со смещением. Наши записи вторую половину не подтверждают: Sherman L шлёт
    /// <c>0x80</c> во всех 597 кадрах поездки 28.07.2026, а плавную жёсткость сообщает страницей 8
    /// (94). Толкование — работа этапа 4 плана 34, не этого чтения.
    /// </para>
    /// </summary>
    private const int RideModeIndex = 31;

    private readonly WheelState _state;
    private readonly IWheelConfig _config;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VeteranDecoder> _logger;
    private readonly VeteranUnpacker _unpacker;
    private long _timeOld;
    private int _protocolVersion;

    /// <summary>Never raised — Veteran is a purely passive protocol (no handshake, no keep-alive).</summary>
#pragma warning disable CS0067
    public event Action<byte[]>? WriteRequested;
#pragma warning restore CS0067

    public event Action<byte[]>? FrameRecognized;

    public VeteranDecoder(WheelState state, IWheelConfig config, TimeProvider timeProvider, ILogger<VeteranDecoder> logger)
    {
        _state = state;
        _config = config;
        _timeProvider = timeProvider;
        _logger = logger;
        _state.WheelType = WheelType.Veteran;
        // VeteranUnpacker is a private implementation detail (never independently DI-resolved) —
        // shares the owning decoder's typed logger category rather than needing its own ILogger<VeteranUnpacker>.
        _unpacker = new VeteranUnpacker(logger);
    }

    public bool IsReady => _state.Voltage != 0 && _protocolVersion != 0;

    /// <summary>Port of VeteranAdapter.decode(byte[]).</summary>
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

        long timeNew = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        if (timeNew - _timeOld > WaitingTimeMs) // need to reset unpacker state in case of packet loss
        {
            _unpacker.Reset();
        }
        _timeOld = timeNew;

        // Parsed once per Decode() call rather than once per completed frame in the loop below —
        // config.GotwayNegative doesn't change mid-call, and an unparsable value now falls back
        // to "0" (abs) instead of throwing partway through the frame loop.
        if (!int.TryParse(_config.GotwayNegative, NumberStyles.Integer, CultureInfo.InvariantCulture, out int veteranNegative))
        {
            veteranNegative = 0;
        }

        bool newDataFound = false;
        foreach (byte c in data)
        {
            if (!_unpacker.AddChar(c)) continue;

            byte[] buff = _unpacker.GetBuffer();
            // AddChar only returns true past a passed CRC (where the frame carries one) — a live
            // wheel, whether or not the fields below turn out to make sense.
            FrameRecognized?.Invoke(buff);
            bool useBetterPercents = _config.UseBetterPercents;

            int voltage = MathsUtil.ShortFromBytesBE(buff, 4);
            int speed = MathsUtil.SignedShortFromBytesBE(buff, 6) * 10;
            int distance = MathsUtil.IntFromBytesRevBE(buff, 8);
            int totalDistance = MathsUtil.IntFromBytesRevBE(buff, 12);
            int phaseCurrent = MathsUtil.SignedShortFromBytesBE(buff, 16) * 10;
            int temperature = MathsUtil.SignedShortFromBytesBE(buff, 18);
            int autoOffSec = MathsUtil.ShortFromBytesBE(buff, 20);
            int chargeMode = MathsUtil.ShortFromBytesBE(buff, 22);
            int ver = MathsUtil.ShortFromBytesBE(buff, 28);
            _protocolVersion = ver / 1000;

            // From protocol version 2 on the wheel reports its real duty cycle in the packet
            // (byte 34), and that value must win over the one derived from speed and voltage —
            // Sherman L is one of these. The original sets the same flag in VeteranAdapter.getVer(),
            // a getter the Android UI happens to poll; here it is done where the version is read,
            // so nothing depends on somebody asking.
            if (_protocolVersion >= 2) _config.HwPwm = true;

            string version = string.Format(CultureInfo.InvariantCulture, "{0:D3}.{1:D1}.{2:D2}",
                ver / 1000, ver % 1000 / 100, ver % 100);
            int pitchAngle = MathsUtil.SignedShortFromBytesBE(buff, 32);
            int hwPwm = MathsUtil.ShortFromBytesBE(buff, 34);
            byte rideMode = buff[RideModeIndex];

            DecodeSmartBms(buff);

            int battery = CalculateBattery(voltage, useBetterPercents);

            if (veteranNegative == 0)
            {
                speed = Math.Abs(speed);
                phaseCurrent = Math.Abs(phaseCurrent);
            }
            else
            {
                speed *= veteranNegative;
                phaseCurrent *= veteranNegative;
            }

            _state.SetVersion(version);
            // Поколение уходит наверх числом, а не только именем модели: у формы посылки и у
            // набора настроек оно спрашивается числом (план 34 §6, шаг 4.3).
            _state.SetProtocolVersion(_protocolVersion);
            _state.SetSpeed(speed);
            _state.SetTopSpeed(speed);
            _state.SetWheelDistance(distance);
            _state.SetTotalDistance(totalDistance);
            _state.SetTemperature(temperature);
            _state.SetPhaseCurrent(phaseCurrent);
            _state.SetVoltage(voltage);
            _state.SetBatteryLevel(battery, CellInputs());
            _state.SetChargingStatus(chargeMode);
            _state.SetSleepTimer(autoOffSec);
            _state.SetAngle(pitchAngle / 100.0);
            _state.SetRideModeRaw(rideMode);
            if (_config.HwPwm)
            {
                _state.SetOutput(hwPwm);
                _state.UpdatePwm();
            }
            else
            {
                _state.CalculatePwm();
            }
            _state.CalculateCurrent();
            _state.CalculatePower();
            string model = GetModel();
            _state.SetModel(model);
            newDataFound = true;
        }
        return newDataFound;
    }

    /// <summary>Opens the identity <see cref="ILogger.BeginScope{TState}"/> for the current
    /// <see cref="Decode"/> call, using whatever model/version/protocolVersion the previous frame
    /// (if any) already established on <see cref="_state"/>. Returns null (no scope) before the
    /// first frame has been decoded — see the remark on <see cref="Decode"/> for why this is
    /// per-call rather than a long-lived field.</summary>
    private IDisposable? BeginIdentityScope()
    {
        if (_state.Model.Length == 0) return null;

        return _logger.BeginScope(new Dictionary<string, object?>
        {
            ["WheelType"] = _state.WheelType,
            ["Model"] = _state.Model,
            ["Version"] = _state.Version,
            ["ProtocolVersion"] = _protocolVersion,
        });
    }

    /// <summary>SmartBMS part of decode() (_protocolVersion &gt;= 5: Lynx = 5; Sherman L = 6) — VeteranAdapter.java:56-128.</summary>
    private void DecodeSmartBms(byte[] buff)
    {
        if (_protocolVersion < 5 || buff.Length <= 46) return;

        int pnum = (sbyte)buff[46];
        int bmsnum = pnum < 4 ? 1 : 2;
        SmartBms bms = bmsnum == 1 ? _state.Bms1 : _state.Bms2;

        if (pnum == 0 || pnum == 4)
        {
            if (buff.Length > 72)
            {
                _state.Bms1.Current = MathsUtil.SignedShortFromBytesBE(buff, 69) / 100.0;
                _state.Bms2.Current = MathsUtil.SignedShortFromBytesBE(buff, 71) / 100.0;
            }
        }
        else if (pnum == 1 || pnum == 5)
        {
            for (int i = 0; i < 15; i++)
            {
                int cell = MathsUtil.SignedShortFromBytesBE(buff, 53 + i * 2);
                bms.Cells[i] = cell / 1000.0;
            }
        }
        else if (pnum == 2 || pnum == 6)
        {
            for (int i = 0; i < 15; i++)
            {
                int cell = MathsUtil.ShortFromBytesBE(buff, 53 + i * 2);
                bms.Cells[i + 15] = cell / 1000.0;
            }
        }
        else if (pnum == 3 || pnum == 7)
        {
            for (int i = 0; i < 12; i++)
            {
                int offset = 59 + i * 2;
                if (offset < buff.Length) // for old wheels the length may be shorter
                {
                    int cell = MathsUtil.ShortFromBytesBE(buff, 59 + i * 2);
                    bms.Cells[i + 30] = cell / 1000.0;
                }
            }
            bms.Temp1 = MathsUtil.SignedShortFromBytesBE(buff, 47) / 100.0;
            bms.Temp2 = MathsUtil.SignedShortFromBytesBE(buff, 49) / 100.0;
            bms.Temp3 = MathsUtil.SignedShortFromBytesBE(buff, 51) / 100.0;
            bms.Temp4 = MathsUtil.SignedShortFromBytesBE(buff, 53) / 100.0;
            bms.Temp5 = MathsUtil.SignedShortFromBytesBE(buff, 55) / 100.0;
            bms.Temp6 = MathsUtil.SignedShortFromBytesBE(buff, 57) / 100.0;

            bms.MinCell = bms.Cells[0];
            bms.MaxCell = bms.Cells[0];
            bms.MaxCellNum = 1;
            bms.MinCellNum = 1;
            double totalVolt = 0.0;

            // Не больше, чем банок в массиве. Ряд теперь приходит и от человека (план 27 §27.4), а
            // 60S — законный ряд, какого в массиве на 56 мест просто нет: без ограничения кадр BMS
            // ронял бы приложение посреди поездки. До §27.4 сюда попадала только версия протокола,
            // максимум 42, и до конца массива было далеко.
            //
            // Ограниченное число едет и в CellCount: иначе среднее поделилось бы на то, чего не
            // считали.
            int cellsForWheel = Math.Min(GetCellsForWheel().Cells, bms.Cells.Length);
            bms.CellCount = cellsForWheel;
            for (int i = 0; i < cellsForWheel; i++)
            {
                double cell = bms.Cells[i];
                totalVolt += cell;
                if (cell > 0.0)
                {
                    if (bms.MaxCell < cell)
                    {
                        bms.MaxCell = cell;
                        bms.MaxCellNum = i + 1;
                    }
                    if (bms.MinCell > cell)
                    {
                        bms.MinCell = cell;
                        bms.MinCellNum = i + 1;
                    }
                }
            }
            bms.CellDiff = bms.MaxCell - bms.MinCell;
            bms.Voltage = totalVolt;

            // Ноль в делителе законен: каскад вправе ответить «ряда не знаю», и тогда цикл выше не
            // сделал ни одного шага. Оригинал делит не глядя, но у него ряд всегда приходил из
            // таблицы; у нас деление дало бы NaN, а NaN на шкале читается не как ошибка, а как
            // пустота — и разбираются в такой пустоте долго.
            bms.AvgCell = cellsForWheel > 0 ? totalVolt / cellsForWheel : 0;
        }
        else if (pnum == VeteranSettingsPage.PageNumber)
        {
            // Здесь у оригинала расписка «new packet, not yet recognized» — страница настроек,
            // которую разбирает только родное приложение производителя. Ветка заполняет расписку и
            // не трогает ни одной из веток выше (план 34 §3, шаг 1.6).
            var settings = VeteranSettingsPage.Parse(buff, _timeProvider.GetUtcNow());
            if (settings is not null) _state.SetWheelSettings(settings);
        }
    }

    /// <summary>Port of the battery-percent branches (VeteranAdapter.java:132-213).</summary>
    private int CalculateBattery(int voltage, bool useBetterPercents)
    {
        if (_protocolVersion < 4) // Sherman, Abrams, Sherman S
        {
            if (useBetterPercents)
            {
                if (voltage > 10020) return 100;
                if (voltage > 8160) return (int)Math.Round((voltage - 8070) / 19.5);
                if (voltage > 7935) return (int)Math.Round((voltage - 7935) / 48.75);
                return 0;
            }
            if (voltage <= 7935) return 0;
            if (voltage >= 9870) return 100;
            return (int)Math.Round((voltage - 7935) / 19.5);
        }
        if (_protocolVersion == 4 || _protocolVersion == 7 || _protocolVersion == 43) // Patton, Patton S, Nosfet Aero
        {
            if (useBetterPercents)
            {
                if (voltage > 12525) return 100;
                if (voltage > 10200) return (int)Math.Round((voltage - 9975) / 25.5);
                if (voltage > 9600) return (int)Math.Round((voltage - 9600) / 67.5);
                return 0;
            }
            if (voltage <= 9918) return 0;
            if (voltage >= 12337) return 100;
            return (int)Math.Round((voltage - 9918) / 24.2);
        }
        if (_protocolVersion == 5 || _protocolVersion == 6 || _protocolVersion == 9 || _protocolVersion == 42 || _protocolVersion == 44) // Lynx, Lynx S, Sherman L, Nosfet Apex, Nosfet Aeon
        {
            if (useBetterPercents)
            {
                if (voltage > 15030) return 100;
                if (voltage > 12240) return (int)Math.Round((voltage - 11970) / 30.6);
                if (voltage > 11520) return (int)Math.Round((double)((voltage - 11520) / 81));
                return 0;
            }
            if (voltage <= 11902) return 0;
            if (voltage >= 14805) return 100;
            return (int)Math.Round((voltage - 11902) / 29.03);
        }
        if (_protocolVersion == 8) // Oryx
        {
            if (useBetterPercents)
            {
                if (voltage > 17535) return 100;
                if (voltage > 14280) return (int)Math.Round((voltage - 14123) / 34.125);
                if (voltage > 13886) return (int)Math.Round((voltage - 13886) / 85.3125);
                return 0;
            }
            if (voltage <= 13886) return 0;
            if (voltage >= 17272) return 100;
            return (int)Math.Round((voltage - 13886) / 34.125);
        }
        return 1; // for new wheels, set 1% by default
    }

    private string GetModel() => _protocolVersion switch
    {
        <= 1 => "Sherman",
        2 => "Abrams",
        3 => "Sherman S",
        4 => "Patton",
        5 => "Lynx",
        6 => "Sherman L",
        7 => "Patton S",
        8 => "Oryx",
        9 => "Lynx S",
        42 => "Nosfet Apex",
        43 => "Nosfet Aero",
        44 => "Nosfet Aeon",
        _ => "Unknown",
    };

    /// <summary>
    /// Ответ идёт через общий каскад (план 27 §27.3): декодер подаёт наверх то, что знает сам, —
    /// ряд по версии протокола и число, заданное человеком (§27.4), — а ответ выдаёт резолвер.
    /// </summary>
    public CellCount GetCellsForWheel() => CellCountResolver.Resolve(CellInputs());

    /// <summary>
    /// Всё, что декодер знает о ряде. Считает по этому каскад — здесь только сбор.
    /// <para>
    /// Ступень умного BMS тут пуста, и это решение владельца (08.08.2026): банки Ветеран шлёт, а
    /// счёт их не называет, и вывести счёт из самих банок нельзя — хвостовые места пакета держат
    /// значения <b>внутри</b> облака живых банок (замер в плане 27 §27.4). Ряд называет версия
    /// протокола, и называет верно.
    /// </para>
    /// </summary>
    internal CellCountInputs CellInputs() => new()
    {
        ConfiguredCells = _config.CellsInSeries,
        ProtocolCells = CellsFromProtocolVersion(),
        PackVolts = _state.Voltage / 100.0,
        // WheelPercent намеренно пуст: заряд у Ветерана считает наша же кривая из напряжения
        // (CalculateBattery), и подать его значило бы делить напряжение на выведенное из него —
        // ступень подтверждала бы любую догадку, включая неверную (план 27 §27.5). Процент годится
        // сюда, только когда его называет само колесо.
    };

    private int CellsFromProtocolVersion() => _protocolVersion switch
    {
        4 or 7 or 43 => 30,
        8 => 42,
        >= 5 => 36,
        _ => 24,
    };

    // --- Commands (VeteranAdapter.java:255-336) ---

    public byte[] BuildResetTrip() => Encoding.ASCII.GetBytes("CLEARMETER");

    public byte[]? BuildUpdatePedalsMode(int pedalsMode) => pedalsMode switch
    {
        0 => Encoding.ASCII.GetBytes("SETh"),
        1 => Encoding.ASCII.GetBytes("SETm"),
        2 => Encoding.ASCII.GetBytes("SETs"),
        _ => null,
    };

    // Состояние света колесо назад не сообщает, поэтому единственный источник правды —
    // IWheelConfig.LightEnabled, и пишется он здесь, при построении команды (по нему же
    // тумблер в шторке решает, что слать следующим).
    public byte[] BuildSetLightState(bool enabled)
    {
        _config.LightEnabled = enabled;
        return Encoding.ASCII.GetBytes(enabled ? "SetLightON" : "SetLightOFF");
    }

    public byte[] BuildSwitchFlashlight() => BuildSetLightState(!_config.LightEnabled);

    public byte[] BuildWheelBeep() => _protocolVersion < 3
        ? Encoding.ASCII.GetBytes("b")
        : new byte[] { 0x4c, 0x6b, 0x41, 0x70, 0x0e, 0x00, 0x80, 0x80, 0x80, 0x01, 0xca, 0x87, 0xe6, 0x6f };

    /// <summary>No wheelCalibration() override in VeteranAdapter — BaseAdapter default is a no-op.</summary>
    public byte[]? BuildCalibrate() => null;

    [LoggerMessage(EventId = LogEvents.Decoding.DecodeInvokedId, EventName = LogEvents.Decoding.DecodeInvokedName,
        Level = LogLevel.Trace, Message = "Decode Veteran")]
    private partial void LogDecodeInvoked();
}
