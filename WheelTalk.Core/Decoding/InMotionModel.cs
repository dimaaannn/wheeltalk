namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of InMotionAdapter.Model 1:1 (InMotionAdapter.java:186-264) — the carType table InMotion's
/// slow-info frame reports a wheel as (<see cref="InMotionModels.FindByBytes"/>), each with its own
/// speed-calculation factor (<see cref="InMotionModels.SpeedCalculationFactor"/>) used to convert
/// raw wheel-rotation counters into km/h.
/// </summary>
public enum InMotionModel
{
    R1N, R1S, R1CF, R1AP, R1EX, R1Sample, R1T, R10,
    V3, V3C, V3PRO, V3S,
    R2N, R2S, R2Sample, R2, R2EX,
    R0,
    L6, Lively,
    V5, V5PLUS, V5F, V5D,
    V8, V8F, V8S, Glide3,
    V10S, V10SF, V10, V10F, V10T, V10FT,
    Unknown,
}

/// <summary>Lookup tables and helpers for <see cref="InMotionModel"/> — split out of the enum
/// itself since C# enums cannot carry per-member data the way Java's constructor-per-constant
/// enum does.</summary>
public static class InMotionModels
{
    private static readonly (InMotionModel Model, string Code, double SpeedFactor)[] Table =
    [
        (InMotionModel.R1N, "0", 3812.0), (InMotionModel.R1S, "1", 1000.0),
        (InMotionModel.R1CF, "2", 3812.0), (InMotionModel.R1AP, "3", 3812.0),
        (InMotionModel.R1EX, "4", 3812.0), (InMotionModel.R1Sample, "5", 1000.0),
        (InMotionModel.R1T, "6", 3810.0), (InMotionModel.R10, "7", 3812.0),
        (InMotionModel.V3, "10", 3812.0), (InMotionModel.V3C, "11", 3812.0),
        (InMotionModel.V3PRO, "12", 3812.0), (InMotionModel.V3S, "13", 3812.0),
        (InMotionModel.R2N, "21", 3812.0), (InMotionModel.R2S, "22", 3812.0),
        (InMotionModel.R2Sample, "23", 3812.0), (InMotionModel.R2, "20", 3812.0),
        (InMotionModel.R2EX, "24", 3812.0),
        (InMotionModel.R0, "30", 1000.0),
        (InMotionModel.L6, "60", 3812.0), (InMotionModel.Lively, "61", 3812.0),
        (InMotionModel.V5, "50", 3812.0), (InMotionModel.V5PLUS, "51", 3812.0),
        (InMotionModel.V5F, "52", 3812.0), (InMotionModel.V5D, "53", 3812.0),
        (InMotionModel.V8, "80", 3812.0), (InMotionModel.V8F, "86", 3812.0),
        (InMotionModel.V8S, "87", 3812.0), (InMotionModel.Glide3, "85", 3812.0),
        (InMotionModel.V10S, "100", 3812.0), (InMotionModel.V10SF, "101", 3812.0),
        (InMotionModel.V10, "140", 3812.0), (InMotionModel.V10F, "141", 3812.0),
        (InMotionModel.V10T, "142", 3812.0), (InMotionModel.V10FT, "143", 3812.0),
        (InMotionModel.Unknown, "x", 3812.0),
    ];

    private static readonly Dictionary<string, InMotionModel> ByCode =
        Table.ToDictionary(t => t.Code, t => t.Model);
    private static readonly Dictionary<InMotionModel, (string Code, double SpeedFactor)> ByModel =
        Table.ToDictionary(t => t.Model, t => (t.Code, t.SpeedFactor));

    public static string Code(this InMotionModel model) => ByModel[model].Code;
    public static double SpeedCalculationFactor(this InMotionModel model) => ByModel[model].SpeedFactor;

    /// <summary>Port of Model.belongToInputType(String) — matches the code's single leading digit
    /// (a bare "0" input matches only single-character codes; anything else must match a
    /// two-character code's first digit).</summary>
    public static bool BelongToInputType(this InMotionModel model, string type)
    {
        string code = model.Code();
        return type == "0" ? code.Length == 1 : code.Length == 2 && code[..1] == type;
    }

    public static InMotionModel FindById(string id) => ByCode.GetValueOrDefault(id, InMotionModel.Unknown);

    /// <summary>
    /// Port of Model.findByBytes(byte[]) (InMotionAdapter.java:253-263). Not a char-code trick —
    /// Java's <c>StringBuilder.append(byte)</c> widens to <c>int</c> and appends its decimal digits,
    /// so this concatenates the *decimal values* of two payload bytes: code "143" (V10FT) comes from
    /// [107]=14 and [104]=3, i.e. <c>"14" + "3"</c>. [107] is a two-digit "tens" prefix, included only
    /// when positive (single-digit codes carry it as 0, or as a negative byte for values ≥ 128 — the
    /// original's signed-byte comparison, kept via the <c>sbyte</c> cast below, since a value ≥ 128
    /// would otherwise never occur in a valid carType anyway).
    /// </summary>
    public static InMotionModel FindByBytes(byte[] data)
    {
        if (data.Length < 108) return InMotionModel.Unknown;

        var id = new System.Text.StringBuilder();
        if ((sbyte)data[107] > 0) id.Append(data[107]);
        id.Append(data[104]);
        return FindById(id.ToString());
    }

    /// <summary>Port of getModelString(Model) (InMotionAdapter.java:616-689) — display name shown
    /// as <c>TelemetrySnapshot.Model</c>.</summary>
    public static string DisplayName(this InMotionModel model) => model switch
    {
        InMotionModel.R1N => "Inmotion R1N",
        InMotionModel.R1S => "Inmotion R1S",
        InMotionModel.R1CF => "Inmotion R1CF",
        InMotionModel.R1AP => "Inmotion R1AP",
        InMotionModel.R1EX => "Inmotion R1EX",
        InMotionModel.R1Sample => "Inmotion R1Sample",
        InMotionModel.R1T => "Inmotion R1T",
        InMotionModel.R10 => "Inmotion R10",
        InMotionModel.V3 => "Inmotion V3",
        InMotionModel.V3C => "Inmotion V3C",
        InMotionModel.V3PRO => "Inmotion V3PRO",
        InMotionModel.V3S => "Inmotion V3S",
        InMotionModel.R2N => "Inmotion R2N",
        InMotionModel.R2S => "Inmotion R2S",
        InMotionModel.R2Sample => "Inmotion R2Sample",
        InMotionModel.R2 => "Inmotion R2",
        InMotionModel.R2EX => "Inmotion R2EX",
        InMotionModel.R0 => "Inmotion R0",
        InMotionModel.L6 => "Inmotion L6",
        InMotionModel.Lively => "Inmotion Lively",
        InMotionModel.V5 => "Inmotion V5",
        InMotionModel.V5PLUS => "Inmotion V5PLUS",
        InMotionModel.V5F => "Inmotion V5F",
        InMotionModel.V5D => "Inmotion V5D",
        InMotionModel.V8 => "Inmotion V8",
        InMotionModel.Glide3 => "Solowheel Glide 3",
        InMotionModel.V8F => "Inmotion V8F",
        InMotionModel.V8S => "Inmotion V8S",
        InMotionModel.V10S => "Inmotion V10S",
        InMotionModel.V10SF => "Inmotion V10SF",
        InMotionModel.V10 => "Inmotion V10",
        InMotionModel.V10F => "Inmotion V10F",
        InMotionModel.V10T => "Inmotion V10T",
        InMotionModel.V10FT => "Inmotion V10FT",
        _ => "Unknown",
    };

    /// <summary>Port of getWheelModesWheel() (InMotionAdapter.java:171-184) — true for the newer
    /// wheels that understand the dedicated beep command; older wheels play a sound instead
    /// (<see cref="InMotionDecoder.BuildWheelBeep"/>).</summary>
    public static bool HasWheelModesWheel(this InMotionModel model) => model is
        InMotionModel.V8F or InMotionModel.V8S or InMotionModel.V10S or InMotionModel.V10SF or
        InMotionModel.V10T or InMotionModel.V10 or InMotionModel.V10F or InMotionModel.V10FT;
}
