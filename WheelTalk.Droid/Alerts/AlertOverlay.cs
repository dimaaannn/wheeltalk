using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Core.View;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Droid.Main;

namespace WheelTalk.Droid.Alerts;

/// <summary>
/// Тревога поверх любого экрана приложения — требование владельца 05.08.2026: райдер, зашедший в
/// настройки или в «Данные», рискует ровно так же, как на панели, и предупреждение обязано быть и
/// там. Показывается тем же, чем на панели: полосы сверху и снизу плюс строка со словами
/// (<see cref="AlertOverlayView"/>).
/// <para>
/// Сделано наблюдателем за жизненным циклом, а не общим предком активностей: экранов девять, они
/// собраны каждый по-своему, и общий базовый класс потребовал бы тронуть все девять — а забытый
/// десятый молча остался бы без тревоги. Здесь новый экран получает тревогу тем, что он экран.
/// </para>
/// <para>
/// Наложение кладётся в <c>android.R.id.content</c> поверх корня экрана (<c>AddContentView</c>) и
/// ничего у разметки не отнимает: экраны собираются как собирались, тревога всплывает над ними и
/// уходит, не сдвинув ни строки, и не забирая ни одного касания (см. <see cref="AlertOverlayView"/>).
/// Своего окна (<c>TYPE_APPLICATION_OVERLAY</c>) не заводим — оно требует разрешения «поверх других
/// приложений» и показывалось бы поверх чужих экранов тоже, а речь только о наших.
/// </para>
/// <para>
/// Главный экран пропускается: там и полоса слов — часть рамки (<c>MainScreenView</c>), и полосы
/// тревоги рисует сама панель. Второй, плавающий поверх, был бы двойным.
/// </para>
/// </summary>
public sealed class AlertOverlay : Java.Lang.Object, Application.IActivityLifecycleCallbacks
{
    private readonly AlertBanner _banner;
    private readonly DashboardOptions _options;

    private Activity? _host;
    private AlertOverlayView? _view;

    public AlertOverlay(AlertBanner banner, DashboardOptions options)
    {
        _banner = banner;
        _options = options;
        _banner.Changed += OnBannerChanged;
    }

    public void OnActivityResumed(Activity activity)
    {
        if (activity is MainActivity) return;

        _host = activity;
        _view = null;
        Render(activity);
    }

    public void OnActivityPaused(Activity activity)
    {
        if (!ReferenceEquals(_host, activity)) return;

        // Наложение принадлежит окну ушедшего экрана и уходит вместе с ним: держать ссылку на
        // остановленную Activity значило бы держать и всё её окно.
        (_view?.Parent as ViewGroup)?.RemoveView(_view);
        _view = null;
        _host = null;
    }

    public void OnActivityCreated(Activity activity, Bundle? savedInstanceState) { }

    public void OnActivityStarted(Activity activity) { }

    public void OnActivityStopped(Activity activity) { }

    public void OnActivitySaveInstanceState(Activity activity, Bundle outState) { }

    public void OnActivityDestroyed(Activity activity) { }

    private void OnBannerChanged()
    {
        if (_host is { } host) Render(host);
    }

    /// <summary>
    /// Наложение создаётся первой же тревогой, а не вместе с экраном: райдеру, у которого её ни разу
    /// не было, оно не стоит ни одной <c>View</c>.
    /// </summary>
    private void Render(Activity host) => host.RunOnUiThread(() =>
    {
        // Пока сообщение шло до потока интерфейса, экран мог смениться — тогда его тревогу уже
        // рисует другой вызов, а этот опоздал.
        if (!ReferenceEquals(_host, host)) return;

        string text = _banner.Text;
        if (text.Length == 0)
        {
            _view?.Hide();
            return;
        }

        _view ??= Attach(host);
        _view.Show(text);
    });

    private AlertOverlayView Attach(Activity host)
    {
        var view = new AlertOverlayView(host, _options, () => _banner.Alert);

        host.Window!.AddContentView(view, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // Сколько сверху занято чужим. Два слагаемых, и оба найдены глазами на эмуляторе 05.08.2026:
        //
        // Статус-бар — экраны живут edge-to-edge (EdgeToEdge.Apply) и рисуют под ним, поэтому без
        // отступа часы ложатся прямо на текст тревоги (та же поправка, что у полосы главного экрана,
        // план 22 §1). Заголовок экрана — ActionBar, и он лежит **поверх** содержимого
        // (ActionBarOverlayLayout), то есть поверх наложения тоже: строка со словами уходила под него
        // целиком и была не видна ни разу.
        //
        // Полос это не касается: они цветные прямоугольники у самых кромок, и то, что верхняя уходит
        // под системную строку, только на пользу — тревогу видно и с погашенным экраном приложения.
        //
        // Инсет читается у окна разом, а не подпиской на его раздачу: экран уже показан, значение
        // известно, а меняется оно только со сменой конфигурации — а та пересоздаёт эти экраны, и
        // наложение собирается заново вместе с ними. Подписка на раздачу тут и не сработала:
        // наложение приходит в уже размеченное окно, и слушателя больше никто не зовёт.
        int top = ViewCompat.GetRootWindowInsets(host.Window.DecorView!) is { } insets
            ? insets.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout())!.Top
            : 0;

        view.TopInset = top + (host.ActionBar?.Height ?? 0);

        return view;
    }
}
