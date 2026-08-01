namespace WheelTalk.Core.Detection;

/// <summary>
/// Дерево GATT не совпало ни с одним отпечатком. Это может быть незнакомая прошивка знакомого
/// колеса, а может быть вообще не колесо — наушники, часы, чужая метка. Разницы для нас нет:
/// говорить с ним нечем, и подключение прекращается без повторов.
/// <para>
/// Отпечаток при этом уже лежит в журнале строкой <c>Wheel.NotDetected</c> — без него добавить
/// прошивку в таблицу потом невозможно.
/// </para>
/// </summary>
public sealed class WheelNotRecognisedException()
    : InvalidOperationException("Устройство не опознано как колесо из известных нам");

/// <summary>
/// Колесо опознано, но его протокол не портирован. Отдельно от «не опознано» нарочно: человеку
/// это разные новости — «у вас InMotion, мы его пока не умеем» можно понять и запомнить, а
/// «непонятное устройство» нельзя.
/// </summary>
public sealed class WheelNotSupportedException(WheelFamily family)
    : InvalidOperationException($"{WheelFamilies.DisplayName(family)} — этот протокол пока не поддерживается")
{
    public WheelFamily Family { get; } = family;
}
