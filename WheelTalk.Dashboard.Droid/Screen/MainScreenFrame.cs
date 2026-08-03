using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Посчитанное состояние кадра основного экрана — всё, что <see cref="IMainScreen"/> показывает, и
/// ничего сверх того: показания приборов, связь, имя колеса, запись, вуаль, инсет
/// (план 17 §2 — «один тип состояния кадра, уже посчитанный»).
/// <para>
/// Считает его хозяин экрана (приложение — поверх сессии, следа и рекордера; стенд — поверх своих
/// ручек и <c>LinkCycle</c>), а <see cref="MainScreenDriver"/> лишь приносит на каждом кадре. Так
/// экран не знает ни про <c>WheelSession</c>, ни про контейнер служб, и второй экран заведёт ровно
/// столько связей с ядром, сколько первый, — ни одной.
/// </para>
/// <para>
/// Бывший <c>PanelChrome</c>: к хрому добавились сами показания, потому что состояние кадра одно, а
/// не два (раньше <c>IPanelSource</c> отдавал их порознь).
/// </para>
/// </summary>
public sealed record MainScreenFrame
{
    /// <summary>Данные для приборов. <c>null</c> — показывать нечего, экран остаётся с прежними.</summary>
    public DashboardReading? Reading { get; init; }

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
