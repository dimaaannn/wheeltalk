using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Lab.Droid.Scenarios;

/// <summary>
/// Состояния связи по кругу — кнопкой «⇄». Связи у стенда нет и быть не может, а показывать он
/// обязан ровно то же, что приложение: плашка и её поведение переехали в библиотеку панели, и
/// проверяются они здесь, на записи, а не выездом с колесом.
/// <para>Извлечено из <c>LabActivity</c> (план 14, А3) — тело перебора перенесено как есть.</para>
/// </summary>
public sealed class LinkCycle
{
    private static readonly (LinkPhase Phase, string Text)[] States =
    [
        (LinkPhase.Live, ""),
        (LinkPhase.Connecting, "Подключение"),
        (LinkPhase.Idle, "Отключено"),
        (LinkPhase.Failed, "Нет Bluetooth"),
        (LinkPhase.JustConnected, "Подключено"),
    ];

    private int _step;
    private long _since;

    public (LinkPhase Phase, string Text) Current => States[_step];

    public int SecondsSince(long now) => (int)((now - _since) / 1000);

    /// <summary>Следующее состояние связи. Счётчик «данных нет N с» начинает считать с этого мига.</summary>
    public void Next(long now)
    {
        _step = (_step + 1) % States.Length;
        _since = now;
    }
}
