namespace WheelTalk.Core.Decoding;

/// <summary>
/// Port of InmotionAdapterV2.Model 1:1 (InmotionAdapterV2.java:107-145) — the carType table InMotion
/// V2's wheel-type frame reports a wheel as. Unlike V1's decimal-string-concatenation trick
/// (<see cref="InMotionModels.FindByBytes"/>), V2 just multiplies: <c>series * 10 + type</c>.
/// </summary>
public enum InMotionV2Model
{
    V11, V11Y, V12HS, V12HT, V12PRO, V13, V13PRO, V14g, V14s, V12S, V9,
    Unknown,
}

public static class InMotionV2Models
{
    private static readonly (InMotionV2Model Model, int Id, string Name)[] Table =
    [
        (InMotionV2Model.V11, 61, "Inmotion V11"),
        (InMotionV2Model.V11Y, 62, "Inmotion V11y"),
        (InMotionV2Model.V12HS, 71, "Inmotion V12 HS"),
        (InMotionV2Model.V12HT, 72, "Inmotion V12 HT"),
        (InMotionV2Model.V12PRO, 73, "Inmotion V12 PRO"),
        (InMotionV2Model.V13, 81, "Inmotion V13"),
        (InMotionV2Model.V13PRO, 82, "Inmotion V13 PRO"),
        (InMotionV2Model.V14g, 91, "Inmotion V14 50GB"),
        (InMotionV2Model.V14s, 92, "Inmotion V14 50S"),
        (InMotionV2Model.V12S, 111, "Inmotion V12S"),
        (InMotionV2Model.V9, 121, "Inmotion V9"),
        (InMotionV2Model.Unknown, 0, "Inmotion Unknown"),
    ];

    private static readonly Dictionary<int, InMotionV2Model> ById = Table.ToDictionary(t => t.Id, t => t.Model);
    private static readonly Dictionary<InMotionV2Model, string> Names = Table.ToDictionary(t => t.Model, t => t.Name);

    public static string DisplayName(this InMotionV2Model model) => Names[model];

    /// <summary>Port of Model.findById(int, int) (InmotionAdapterV2.java:137-144).</summary>
    public static InMotionV2Model FindById(int series, int type) =>
        ById.GetValueOrDefault(series * 10 + type, InMotionV2Model.Unknown);

    /// <summary>Port of getCellsForWheel() (InmotionAdapterV2.java:2484-2508).</summary>
    public static int CellsForWheel(this InMotionV2Model model) => model switch
    {
        InMotionV2Model.V12HS or InMotionV2Model.V12HT or InMotionV2Model.V12PRO => 24,
        InMotionV2Model.V13 or InMotionV2Model.V13PRO => 30,
        InMotionV2Model.V14g or InMotionV2Model.V14s => 32,
        _ => 20,
    };
}
