using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Dashboard.Droid;

/// <summary>
/// Что панель показывает сверх приборов на очередном кадре: связь, имя колеса, запись, вуаль,
/// инсет. Источник кадра (<see cref="IPanelSource"/>) отдаёт его целиком, <see cref="PanelDriver"/>
/// раскладывает по полям <see cref="DashboardView"/> одним местом — раньше каждый потребитель
/// (приложение, стенд) делал это своей копией присвоений (план 19, «Карта проблем» п. 3).
/// </summary>
public sealed record PanelChrome
{
    public LinkPhase LinkPhase { get; init; } = LinkPhase.Idle;
    public string LinkText { get; init; } = "";
    public int LinkSeconds { get; init; }
    public string WheelName { get; init; } = "";
    public bool Recording { get; init; }
    public bool ShowRecordDot { get; init; }
    public bool ShowSheetHint { get; init; }
    public bool IsStale { get; init; }
    public float TopInset { get; init; }
    public bool SpeedExceeded { get; init; }
}
