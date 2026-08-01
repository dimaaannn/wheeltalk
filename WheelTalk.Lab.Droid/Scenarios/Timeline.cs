using WheelTalk.Core.Contracts;

namespace WheelTalk.Lab.Droid.Scenarios;

public sealed record TimelineFrame(TimeSpan At, TelemetrySnapshot Snapshot);

/// <summary>
/// Сценарий, разложенный в массив кадров. Дамп проигрывается не потоком, как в приложении, а
/// именно списком, и это главное отличие стенда от реплея: по списку можно встать на конкретный
/// кадр, отмотать назад и снять один и тот же момент для всех вариантов панели. Сравнение
/// вариантов на разных кадрах не значит ничего.
/// <para>
/// Перенесено из <c>WheelTalk.Lab/Scenarios/Timeline.cs</c> без изменений: платформенного здесь
/// нет ничего, поменялось только пространство имён.
/// </para>
/// </summary>
public sealed class Timeline
{
    public Timeline(string title, string subtitle, IReadOnlyList<TimelineFrame> frames)
    {
        Title = title;
        Subtitle = subtitle;
        Frames = frames.Count > 0
            ? frames
            : [new TimelineFrame(TimeSpan.Zero, new TelemetrySnapshot())];
        Marks = DeriveMarks();
    }

    public string Title { get; }

    /// <summary>Что в сценарии происходит — строка из README нарезки или описание синтетики.</summary>
    public string Subtitle { get; }

    public IReadOnlyList<TimelineFrame> Frames { get; }

    /// <summary>Характерные точки: по ним снимаются статичные картинки.</summary>
    public IReadOnlyList<TimelineMark> Marks { get; }

    public TimeSpan Duration => Frames[^1].At;

    /// <summary>Индекс последнего кадра не позже позиции.</summary>
    public int IndexAt(TimeSpan position)
    {
        int low = 0;
        int high = Frames.Count - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (Frames[middle].At <= position) low = middle;
            else high = middle - 1;
        }
        return low;
    }

    /// <summary>
    /// Точки выводятся из самой записи, а не выписываются руками: тогда они переживают и
    /// модификаторы, и синтетику, и нарезку не приходится размечать заново при каждой правке.
    /// </summary>
    private IReadOnlyList<TimelineMark> DeriveMarks()
    {
        var marks = new List<TimelineMark>();

        int standing = IndexOfFirst(f => Math.Abs(f.Snapshot.SpeedKmh) < 0.5);
        if (standing >= 0) marks.Add(new TimelineMark("стоит", Frames[standing].At));

        int accelerating = IndexOfMax(SpeedGain);
        if (accelerating > 0 && SpeedGain(accelerating) > 1) marks.Add(new TimelineMark("разгон", Frames[accelerating].At));

        int topSpeed = IndexOfMax(i => Math.Abs(Frames[i].Snapshot.SpeedKmh));
        if (Math.Abs(Frames[topSpeed].Snapshot.SpeedKmh) > 1) marks.Add(new TimelineMark("макс скорость", Frames[topSpeed].At));

        int topPwm = IndexOfMax(i => Math.Abs(Frames[i].Snapshot.Pwm));
        if (Math.Abs(Frames[topPwm].Snapshot.Pwm) > 1) marks.Add(new TimelineMark("макс ШИМ", Frames[topPwm].At));

        return marks.OrderBy(m => m.At).ToList();
    }

    private double SpeedGain(int index)
    {
        if (index == 0) return 0;

        double seconds = (Frames[index].At - Frames[index - 1].At).TotalSeconds;
        return seconds <= 0 ? 0 : (Math.Abs(Frames[index].Snapshot.SpeedKmh) - Math.Abs(Frames[index - 1].Snapshot.SpeedKmh)) / seconds;
    }

    private int IndexOfFirst(Func<TimelineFrame, bool> predicate)
    {
        for (int i = 0; i < Frames.Count; i++)
        {
            if (predicate(Frames[i])) return i;
        }
        return -1;
    }

    private int IndexOfMax(Func<int, double> value)
    {
        int best = 0;
        double bestValue = double.MinValue;
        for (int i = 0; i < Frames.Count; i++)
        {
            double candidate = value(i);
            if (candidate <= bestValue) continue;
            bestValue = candidate;
            best = i;
        }
        return best;
    }
}

public sealed record TimelineMark(string Name, TimeSpan At);
