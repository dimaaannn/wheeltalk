namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Покадровый цикл основного экрана. Раньше это было три копии (<c>MainActivity.FrameTick</c>+
/// <c>ShowLink</c>, <c>LabActivity.Render</c>, часть <c>PlaybackActivity.FrameTick</c>), считавшие
/// одно и то же порознь, и расчёт моргания уже разошёлся: приложение считало от часов, стенд — от
/// момента запуска (план 19, «Карта проблем» п. 3). Здесь он один: сам ставит себя в очередь
/// <c>View.PostOnAnimation</c> (та же очередь Choreographer'а, в которую панель кладёт свой
/// <c>PostInvalidateOnAnimation</c>), спрашивает у хозяина состояние кадра и отдаёт его экрану.
/// <para>
/// Что не входит: всё, что «на кадре, но не про экран» (флаги окна, стендовая проводка списка и
/// ползунка). Для этого конструктор принимает необязательный хук — он зовётся перед каждым опросом.
/// Заводить для такого второй параллельный цикл <c>PostOnAnimation</c> незачем: очередь кадра одна
/// на всех.
/// </para>
/// <para>
/// Бывший <c>PanelDriver</c>. Наклон и моргание уехали в саму панель: это её свойства, а не
/// свойства цикла, и второй экран (план 23) их не имеет вовсе.
/// </para>
/// </summary>
public sealed class MainScreenDriver
{
    private readonly Action? _beforeFrame;

    private IMainScreen? _screen;
    private Func<MainScreenFrame>? _frame;
    private bool _running;

    public MainScreenDriver(Action? beforeFrame = null) => _beforeFrame = beforeFrame;

    /// <summary>
    /// Какой экран обслуживать и у кого спрашивать состояние кадра. Стенд пересоздаёт панель при
    /// смене варианта или экрана целиком — повторный вызов лишь переставляет цель, идущий цикл
    /// кадра переезжает на новый экран сам, без остановки и пересоздания.
    /// </summary>
    public void Attach(IMainScreen screen, Func<MainScreenFrame> frame)
    {
        _screen = screen;
        _frame = frame;
    }

    /// <summary>Запускает цикл кадра, если он ещё не идёт. Повторный вызов, пока цикл уже идёт, — не-оп.</summary>
    public void Start()
    {
        if (_running) return;

        _running = true;
        _screen?.View.PostOnAnimation(new Java.Lang.Runnable(Tick));
    }

    public void Stop() => _running = false;

    /// <summary>
    /// Показать текущее состояние немедленно, не дожидаясь очередного vsync, — для реакции на
    /// касание (перемотка, смена варианта, загрузка сценария): без этого экран ждал бы следующего
    /// кадра, чтобы отразить то, что уже произошло.
    /// </summary>
    public void Refresh()
    {
        _beforeFrame?.Invoke();

        if (_screen is { } screen && _frame is { } frame) screen.Show(frame());
    }

    private void Tick()
    {
        if (!_running) return;

        Refresh();
        _screen?.View.PostOnAnimation(new Java.Lang.Runnable(Tick));
    }
}
