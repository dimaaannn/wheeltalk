namespace WheelTalk.Core.Contracts;

public enum WheelType
{
    Unknown,
    KingSong,
    /// <summary>Covers both Gotway and Begode branding — same protocol family, WHEEL_TYPE.GOTWAY in Android.</summary>
    GotWay,
    Inmotion,
    InmotionV2,
    Ninebot,
    NinebotZ,
    Veteran,
}
