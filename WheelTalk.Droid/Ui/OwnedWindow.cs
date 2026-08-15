using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;

namespace WheelTalk.Droid.Ui;

/// <summary>
/// Окно поверх экрана, у которого есть хозяин. Диалог висит не на ветви вью, а на <b>окне
/// активности</b>: показанный и брошенный, он переживает её смерть — активность уничтожается вместе
/// со своим окном, Android пишет в журнал <c>WindowLeaked</c>, а диалог держит живой всю разметку,
/// что за ним стоит.
/// <para>
/// Повод — дамп владельца 10.08.2026: полноэкранный просмотр графика остался открытым, когда
/// активность уничтожили, и единственный стек в хвосте дампа был именно <c>WindowLeaked</c>.
/// Плитки вылечены хозяином (<c>TilesScreen</c>), здесь то же лекарство для активностей: хозяин
/// держит окно и закрывает его в <c>OnDestroy</c>.
/// </para>
/// <para>
/// Смерть активности приходит не только от «назад». Экраны без <c>ConfigurationChanges</c> —
/// поиск, поездки, настройки — пересоздаются от поворота телефона и от смены светлой темы на
/// тёмную, и открытое окно теряется в обоих случаях. А <c>ScanActivity</c> закрывает себя сама,
/// закончив подключение, — с открытым «забыть колесо» это ровно тот случай.
/// </para>
/// </summary>
public sealed class OwnedWindow
{
    private Dialog? _shown;

    /// <summary>
    /// Показать окно и запомнить его. Прежнее закрывается: одно окно на экран — их и открывают по
    /// одному, а забытое прежнее было бы той же утечкой, только изнутри.
    /// </summary>
    public Dialog Show(AlertDialog.Builder builder)
    {
        Close();
        _shown = builder.Show()!;
        return _shown;
    }

    /// <summary>
    /// Лист снизу: окно во всю ширину, прижатое к нижнему краю, с прозрачным фоном — скругление
    /// рисует само содержимое (макет 2c настроек). Собирается здесь, а не у хозяина, по той же
    /// причине, по которой здесь живёт <see cref="Show"/>: окно, которое каждый создаёт сам, кто-то
    /// однажды и забудет закрыть.
    /// </summary>
    public Dialog ShowSheet(Context context, View content)
    {
        Close();

        var sheet = new Dialog(context);
        sheet.RequestWindowFeature((int)WindowFeatures.NoTitle);
        sheet.SetContentView(content);

        if (sheet.Window is { } window)
        {
            window.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
            window.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            window.SetGravity(GravityFlags.Bottom);
        }

        sheet.Show();
        _shown = sheet;
        return sheet;
    }

    /// <summary>
    /// Принять окно, собранное и показанное чужой рукой, — тем, кто отдаёт его наружу
    /// (<c>public static Dialog Show(…)</c>). Хозяин у окна всё равно один: прежнее закрывается, а
    /// это уходит в те же руки, что закроют его в <c>OnDestroy</c>.
    /// </summary>
    public Dialog Own(Dialog dialog)
    {
        if (!ReferenceEquals(_shown, dialog)) Close();
        _shown = dialog;

        return dialog;
    }

    /// <summary>
    /// Открыто ли сейчас окно. Спрашивает тот, кому нельзя трогать разметку под ним: страница
    /// настроек колеса перестраивается от кадра, приходящего сама по себе, и перестроение под
    /// открытым листом схлопнуло бы его под пальцем (план 34, шаг 2.6).
    /// </summary>
    public bool IsOpen => _shown is { IsShowing: true };

    /// <summary>Закрыть, если ещё открыто. Зовётся хозяином на конце его жизни.</summary>
    public void Close()
    {
        if (_shown is { IsShowing: true } shown) shown.Dismiss();
        _shown = null;
    }
}
