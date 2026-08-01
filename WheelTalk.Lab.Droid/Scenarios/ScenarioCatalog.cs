using WheelTalk.Core.Contracts;

namespace WheelTalk.Lab.Droid.Scenarios;

/// <summary>
/// Чем кормится стенд: настоящая нарезка из <c>replay/</c> на обоих колёсах и придуманные
/// сценарии для случаев, которых в записях нет. Порядок неслучаен — сначала то, что было на самом
/// деле, потом то, что мы досочинили.
/// <para>
/// Перенесено из <c>WheelTalk.Lab/Scenarios/ScenarioCatalog.cs</c> без изменений: путь к ассету тот
/// же (<c>replay/…</c>), поменялся только способ его открыть (см. <see cref="DumpTimeline"/>).
/// </para>
/// </summary>
public static class ScenarioCatalog
{
    public sealed record Scenario(string Id, string Title, string Subtitle, Func<Task<Timeline>> Load);

    private static readonly Dictionary<string, Timeline> Loaded = [];

    public static IReadOnlyList<Scenario> All { get; } =
    [
        Dump("mten3-calm-ride", "MTen3 · спокойно", "10–25 км/ч, ШИМ до 59 % — основной кусок для работы над интерфейсом", WheelProtocol.Gotway),
        Dump("mten3-alarm-spinup", "MTen3 · раскрут", "12 секунд, ШИМ 110 % — тревога в полный голос и трёхзначный ШИМ", WheelProtocol.Gotway),
        Dump("mten3-slowing-to-stop", "MTen3 · остановка", "замедление с 18 км/ч до нуля", WheelProtocol.Gotway),
        Dump("mten3-parking", "MTen3 · стоянка", "скорость 0, ШИМ 2 % — как выглядит панель в покое", WheelProtocol.Gotway),
        Dump("shermanl-calm-ride", "Sherman L · спокойно", "6–32 км/ч, ШИМ до 43 %, пак 150 В", WheelProtocol.Veteran),
        Dump("shermanl-spinup-cutout", "Sherman L · срыв", "раскрут до 145 км/ч и отключение колеса — крайний случай вёрстки", WheelProtocol.Veteran),
        Dump("shermanl-parking", "Sherman L · стоянка", "стоящее колесо, 147 В", WheelProtocol.Veteran),

        Synthetic("synthetic-sag", SyntheticTimeline.Sag),
        Synthetic("synthetic-approach", SyntheticTimeline.Approach),
        Synthetic("synthetic-step", SyntheticTimeline.Step),
        Synthetic("synthetic-jitter", SyntheticTimeline.Jitter),
        Synthetic("synthetic-sawtooth", SyntheticTimeline.Sawtooth),
    ];

    /// <summary>
    /// Разобранный сценарий держится в памяти: разбор двухминутного дампа — это несколько тысяч
    /// кадров через декодер, и делать это заново при каждом переключении варианта незачем.
    /// </summary>
    public static async Task<Timeline> LoadAsync(Scenario scenario)
    {
        if (Loaded.TryGetValue(scenario.Id, out var cached)) return cached;

        var timeline = await scenario.Load();
        Loaded[scenario.Id] = timeline;
        return timeline;
    }

    private static Scenario Dump(string file, string title, string subtitle, WheelProtocol protocol) =>
        new(file, title, subtitle, () => DumpTimeline.LoadAsync(title, subtitle, $"replay/{file}.csv", protocol));

    private static Scenario Synthetic(string id, Func<Timeline> build)
    {
        var probe = build();
        return new Scenario(id, probe.Title, probe.Subtitle, () => Task.FromResult(build()));
    }
}
