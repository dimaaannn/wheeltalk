namespace WheelTalk.Dashboard.Droid;

/// <summary>
/// Вопрос, который на каждом кадре задаёт <see cref="PanelDriver"/>: что показать и каким хромом
/// это одеть. Реализует тот, кто знает ответ, — приложение поверх сессии/следа/рекордера, стенд
/// поверх ручек и <c>LinkCycle</c>; панель у обоих одна и та же.
/// </summary>
public interface IPanelSource
{
    /// <summary>Данные для приборов. <c>null</c> — показывать нечего, <c>Show</c> не вызывается.</summary>
    DashboardReading? Reading { get; }

    PanelChrome Chrome { get; }
}
