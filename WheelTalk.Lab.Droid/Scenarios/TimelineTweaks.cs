namespace WheelTalk.Lab.Droid.Scenarios;

/// <summary>
/// Правка записи на лету. Нужна ровно затем, зачем её просили: показать поведение, которого в
/// записи нет, не выдумывая при этом всю запись. Умножить ШИМ спокойной поездки на 1,6 — и та же
/// настоящая динамика, те же настоящие дрожания приходят к порогам, до которых на колесе доезжать
/// не хочется.
/// <para>
/// Перенесено из <c>WheelTalk.Lab/Scenarios/TimelineTweaks.cs</c> без изменений.
/// </para>
/// </summary>
public sealed record TimelineTweaks(double PwmGain = 1, double SpeedGain = 1, double TimeScale = 1)
{
    public static readonly TimelineTweaks None = new();

    public bool IsIdentity => PwmGain == 1 && SpeedGain == 1 && TimeScale == 1;

    public Timeline Apply(Timeline timeline)
    {
        if (IsIdentity) return timeline;

        var frames = timeline.Frames
            .Select(frame => new TimelineFrame(
                frame.At / TimeScale,
                frame.Snapshot with
                {
                    Pwm = frame.Snapshot.Pwm * PwmGain,
                    MaxPwm = frame.Snapshot.MaxPwm * PwmGain,
                    SpeedRaw = (int)Math.Round(frame.Snapshot.SpeedRaw * SpeedGain),
                    TopSpeedRaw = (int)Math.Round(frame.Snapshot.TopSpeedRaw * SpeedGain),
                }))
            .ToList();

        return new Timeline(timeline.Title, timeline.Subtitle, frames);
    }

    public string Describe() => IsIdentity
        ? "как записано"
        : $"ШИМ ×{PwmGain:F2}, скорость ×{SpeedGain:F2}, время ×{TimeScale:F2}";
}
