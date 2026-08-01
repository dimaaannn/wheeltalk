namespace WheelTalk.Dashboard.Droid;

/// <summary>
/// Покадровый водитель хрома панели. Раньше это было три копии (<c>MainActivity.FrameTick</c>+
/// <c>ShowLink</c>, <c>LabActivity.Render</c>, часть <c>PlaybackActivity.FrameTick</c>), считавшие
/// одно и то же порознь, и расчёт моргания уже разошёлся: приложение считало от часов, стенд — от
/// момента запуска (план 19, «Карта проблем» п. 3). Здесь он один: сам ставит себя в очередь
/// <c>View.PostOnAnimation</c> (тот же приём, на котором держится кадр панели), сам решает наклон и
/// моргание и одним местом раскладывает <see cref="PanelChrome"/> по полям <see cref="DashboardView"/>.
/// <para>
/// Что не входит: всё, что «на кадре, но не про панель» (флаги окна, зеркалирование настроек,
/// стендовая проводка списка/ползунка). Для этого конструктор принимает необязательный хук — он
/// зовётся перед каждым опросом источника. Заводить для такого второй параллельный цикл
/// <c>PostOnAnimation</c> незачем: очередь кадра одна на всех.
/// </para>
/// </summary>
public sealed class PanelDriver
{
    /// <summary>Порог смены наклона — меньше не стоит перерисовывать ради шума с плавающей точкой.</summary>
    private const float TiltEpsilon = 0.01f;

    private readonly DashboardOptions _options;
    private readonly Action? _beforeFrame;

    private DashboardView? _dashboard;
    private IPanelSource? _source;
    private bool _running;

    public PanelDriver(DashboardOptions options, Action? beforeFrame = null)
    {
        _options = options;
        _beforeFrame = beforeFrame;
    }

    /// <summary>
    /// Какую панель и источник обслуживать. Стенд пересоздаёт панель при смене варианта или экрана
    /// целиком — повторный вызов лишь переставляет цель, идущий цикл кадра переезжает на новую
    /// панель сам, без остановки и пересоздания.
    /// </summary>
    public void Attach(DashboardView dashboard, IPanelSource source)
    {
        _dashboard = dashboard;
        _source = source;
    }

    /// <summary>Запускает цикл кадра, если он ещё не идёт. Повторный вызов, пока цикл уже идёт, — не-оп.</summary>
    public void Start()
    {
        if (_running) return;

        _running = true;
        _dashboard?.PostOnAnimation(new Java.Lang.Runnable(Tick));
    }

    public void Stop() => _running = false;

    /// <summary>
    /// Показать текущее значение источника немедленно, не дожидаясь очередного vsync, — для реакции
    /// на касание (перемотка, смена варианта, загрузка сценария): без этого экран ждал бы следующего
    /// кадра, чтобы отразить то, что уже произошло.
    /// </summary>
    public void Refresh()
    {
        _beforeFrame?.Invoke();
        Apply();
    }

    private void Tick()
    {
        if (!_running) return;

        Refresh();
        _dashboard?.PostOnAnimation(new Java.Lang.Runnable(Tick));
    }

    private void Apply()
    {
        if (_dashboard is not { } dashboard || _source is not { } source) return;

        float tilt = (float)_options.Tilt;
        if (Math.Abs(dashboard.Rotation - tilt) > TiltEpsilon)
        {
            dashboard.Rotation = tilt;
        }

        if (source.Reading is { } reading)
        {
            dashboard.Show(reading);
        }

        var chrome = source.Chrome;
        dashboard.LinkPhase = chrome.LinkPhase;
        dashboard.LinkText = chrome.LinkText;
        dashboard.LinkSeconds = chrome.LinkSeconds;
        dashboard.WheelName = chrome.WheelName;
        dashboard.Recording = chrome.Recording;
        dashboard.ShowRecordDot = chrome.ShowRecordDot;
        dashboard.ShowSheetHint = chrome.ShowSheetHint;
        dashboard.IsStale = chrome.IsStale;
        dashboard.TopInset = chrome.TopInset;
        dashboard.SpeedExceeded = chrome.SpeedExceeded;

        // Часы, а не переключение раз в кадр: при плавающей частоте экрана «раз в кадр» плавало бы
        // вместе с ней вместо фиксированных BlinkHz. От момента запуска намеренно не считаем — это и
        // была разошедшаяся стендовая копия (план 19, «Карта проблем» п. 3).
        double period = _options.BlinkHz > 0 ? 1000 / _options.BlinkHz : 0;
        dashboard.AlertLit = period <= 0 || System.Environment.TickCount64 % period < period / 2;
    }
}
