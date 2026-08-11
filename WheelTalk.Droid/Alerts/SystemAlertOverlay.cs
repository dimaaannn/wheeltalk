using Android.Graphics;
using Android.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Droid.Configuration;
using Application = Android.App.Application;

namespace WheelTalk.Droid.Alerts;

/// <summary>
/// Тревога поверх ЧУЖИХ приложений — решение владельца 11.08.2026: райдер, свернувший приложение
/// (карта, звонок, что угодно другое на экране), рискует так же, как с открытым приложением, и
/// полоса обязана быть видна и там. Собственным системным окном (<c>TYPE_APPLICATION_OVERLAY</c>),
/// а не тем же путём, что <see cref="AlertOverlay"/>: тот кладёт наложение в разметку НАШЕЙ
/// активности и разрешения не требует, а рисовать поверх чужих окон без спроса система не даёт.
/// <para>
/// Выключено по умолчанию (<see cref="AlertSignalOptions.OverlayOtherApps"/>, GlobalOnly) —
/// разрешение «поверх других приложений» система выдаёт неохотно, и решать, делиться ли им,
/// должен райдер. Запрос — при первом включении тумблера (<c>SettingsCategoryActivity.Commit</c>,
/// единственное место, где ключ настройки зашит).
/// </para>
/// <para>
/// Показ гейтится четырьмя условиями разом (см. <see cref="Evaluate"/>): есть текст тревоги,
/// настройка включена, разрешение дано и ни одна наша активность не видна прямо сейчас —
/// последнее читается из <see cref="AlertOverlay.HostVisible"/>, чтобы не рисовать поверх
/// собственного же экрана вторую копию того, что тот уже показывает сам.
/// </para>
/// </summary>
public sealed class SystemAlertOverlay : IDisposable
{
    private readonly AlertBanner _banner;
    private readonly AlertOverlay _ownScreens;
    private readonly AlertSignalOptions _channels;
    private readonly DashboardOptions _dashboardOptions;
    private readonly ILogger<SystemAlertOverlay> _logger;
    private readonly IWindowManager _windowManager;

    private AlertOverlayView? _view;
    private bool _lastAttempt;

    public SystemAlertOverlay(
        AlertBanner banner,
        AlertOverlay ownScreens,
        IOptions<AlertSignalOptions> channels,
        DashboardOptions dashboardOptions,
        ILogger<SystemAlertOverlay> logger)
    {
        _banner = banner;
        _ownScreens = ownScreens;
        _channels = channels.Value;
        _dashboardOptions = dashboardOptions;
        _logger = logger;
        _windowManager = (IWindowManager)Application.Context.GetSystemService(Android.Content.Context.WindowService)!;

        _banner.Changed += Evaluate;
        _ownScreens.HostVisibilityChanged += Evaluate;
    }

    public void Dispose()
    {
        _banner.Changed -= Evaluate;
        _ownScreens.HostVisibilityChanged -= Evaluate;
        Remove();
    }

    /// <summary>
    /// Все четыре условия — в одном месте: разошедшиеся копии одного и того же вопроса рано или
    /// поздно перестали бы отвечать одинаково. Лог — только на смену решения (Debug), не на каждый
    /// вызов: банер меняется на каждом кадре тревоги, а решение показывать/убрать — редко.
    /// </summary>
    private void Evaluate()
    {
        string text = _banner.Text;
        bool attempt = text.Length > 0 && _channels.OverlayOtherApps && !_ownScreens.HostVisible
            && Android.Provider.Settings.CanDrawOverlays(Application.Context);

        if (attempt != _lastAttempt)
        {
            _lastAttempt = attempt;
            _logger.LogDebug("Alert.SystemOverlay.Attempt={Attempt}", attempt);
        }

        if (!attempt)
        {
            Remove();
            return;
        }

        if (_view is null) Add();
        _view!.Show(text);
    }

    private void Add()
    {
        _view = new AlertOverlayView(Application.Context, _dashboardOptions, () => _banner.Alert);

        // Без FullScreen: LayoutInScreen сам по себе не рвёт окно под системные панели, и рамка
        // AlertOverlayView.TopInset здесь не нужна — в отличие от AlertOverlay, у которого высота
        // статус-бара бралась у конкретного окна хозяина, а тут хозяина нет вовсе.
        var layoutParams = new WindowManagerLayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent,
            WindowManagerTypes.ApplicationOverlay,
            WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchable | WindowManagerFlags.LayoutInScreen,
            Format.Translucent);

        _windowManager.AddView(_view, layoutParams);
    }

    private void Remove()
    {
        if (_view is not { } view) return;

        _windowManager.RemoveView(view);
        _view = null;
    }
}
