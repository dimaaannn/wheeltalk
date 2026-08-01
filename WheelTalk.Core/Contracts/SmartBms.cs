namespace WheelTalk.Core.Contracts;

/// <summary>
/// Port of SmartBms.kt 1:1 — mutable BMS sub-state (Sherman L reports two independent packs).
/// </summary>
public sealed class SmartBms
{
    public string SerialNumber { get; set; } = "";
    public string VersionNumber { get; set; } = "";
    public int FactoryCap { get; set; }
    public int ActualCap { get; set; }
    public int FullCycles { get; set; }
    public int ChargeCount { get; set; }
    public string MfgDateStr { get; set; } = "";
    public int Status { get; set; }
    public int RemCap { get; set; }
    public int RemPerc { get; set; }
    public double Current { get; set; }
    public double Voltage { get; set; }
    public double SemiVoltage1 { get; set; }
    public double SemiVoltage2 { get; set; }
    public double Temp1 { get; set; }
    public double Temp2 { get; set; }
    public double Temp3 { get; set; }
    public double Temp4 { get; set; }
    public double Temp5 { get; set; }
    public double Temp6 { get; set; }
    public double TempMos { get; set; }
    public double TempMosEnv { get; set; }
    public double Temp1Env { get; set; }
    public double Temp2Env { get; set; }
    public double Humidity1Env { get; set; }
    public double Humidity2Env { get; set; }
    public int BalanceMap { get; set; }
    public int Health { get; set; }
    public double MinCell { get; set; }
    public double MaxCell { get; set; }
    public double CellDiff { get; set; }
    public double AvgCell { get; set; }
    public int MinCellNum { get; set; }
    public int MaxCellNum { get; set; }
    public int CellNum { get; set; }
    public double[] Cells { get; private set; } = new double[56];

    /// <summary>
    /// Сколько банок в <see cref="Cells"/> — настоящих. Массив заполняется блоками фиксированной
    /// длины, и последний блок заезжает за конец пакета: у 36-баночного Sherman L записываются 42
    /// значения, а в шести лишних оказываются байты температур — 33,365 «вольта» на банке 41.
    /// Оригинал живёт с тем же переполнением и просто читает первые <c>getCellsForWheel()</c>;
    /// здесь это число названо, чтобы читать его мог не только тот, кто заполнял.
    /// </summary>
    public int CellCount { get; set; }

    public SmartBms()
    {
        Reset();
    }

    public void Reset()
    {
        SerialNumber = "";
        VersionNumber = "";
        FactoryCap = 0;
        ActualCap = 0;
        FullCycles = 0;
        ChargeCount = 0;
        MfgDateStr = "";
        Status = 0;
        RemCap = 0;
        RemPerc = 0;
        Current = 0.0;
        Voltage = 0.0;
        SemiVoltage1 = 0.0;
        SemiVoltage2 = 0.0;
        Temp1 = 0.0;
        Temp2 = 0.0;
        Temp3 = 0.0;
        Temp4 = 0.0;
        Temp5 = 0.0;
        Temp6 = 0.0;
        TempMos = 0.0;
        TempMosEnv = 0.0;
        Temp1Env = 0.0;
        Temp2Env = 0.0;
        Humidity1Env = 0.0;
        Humidity2Env = 0.0;
        BalanceMap = 0;
        Health = 0;
        MinCell = 0.0;
        MaxCell = 0.0;
        CellDiff = 0.0;
        MinCellNum = 0;
        MaxCellNum = 0;
        CellNum = 0;
        Cells = new double[56];
        CellCount = 0;
    }
}
