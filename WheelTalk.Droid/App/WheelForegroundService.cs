using System.Globalization;
using System.Reactive.Linq;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Services;
using WheelTalk.Droid.Resources.Strings;

namespace WheelTalk.Droid.App;

/// <summary>
/// Keeps the process alive while a wheel is connected. The session itself lives in the app's
/// container, not here — this service exists because Android stops running a backgrounded process
/// otherwise, and a ride outlasts the screen being on by hours.
///
/// The notification is a platform requirement, not a feature: a foreground service without one
/// does not exist. It is therefore as quiet as Android allows — lowest importance, no sound, no
/// vibration, one line of text.
/// <para>
/// Раз уж строка всё равно висит — она показывает состояние связи и заряд колеса (план 11 §2.2,
/// первая половина). Это же половина «уведомления как канала телеметрии» из
/// <c>android-plan-10-telemetry-out.md</c> §3: браслет зеркалит именно текст уведомления, никакой
/// чужой SDK для этого не нужен. Режимы `Min`/`Medium`/`Max` с полным набором полей и действие
/// «Отключиться» остаются там же и не сделаны здесь.
/// </para>
/// <para>
/// <b>Живучесть важнее содержимого.</b> Сервис существует, чтобы приложение не умирало в кармане,
/// поэтому обновление текста устроено так, чтобы не мочь этому помешать: перерисовка идёт через
/// <c>Notify</c> с тем же идентификатором (уведомление остаётся тем же самым, привязка сервиса к
/// нему не рвётся), <c>StartForeground</c> после первого раза не вызывается, а всё, что может
/// бросить, — обёрнуто. Не показать заряд — мелкая неприятность; уронить сервис из-за него —
/// потерянная поездка.
/// </para>
/// </summary>
[Service(ForegroundServiceType = Android.Content.PM.ForegroundService.TypeConnectedDevice, Exported = false)]
public sealed class WheelForegroundService : Service
{
    private const string ChannelId = "wheeltalk.connection";
    private const int NotificationId = 1;
    private const string StopAction = "wheeltalk.stop";

    /// <summary>Заряда ещё не было: ни одного кадра с колеса за эту жизнь сервиса.</summary>
    private const int NoBattery = -1;

    /// <summary>Стартовал ли сервис в этом процессе — чтобы <see cref="Stop"/> по состоянию сессии
    /// не поднимал foreground-сервис (с мельканием уведомления) только ради его остановки.</summary>
    private static bool _started;

    private IDisposable? _telemetry;
    private IDisposable? _connection;

    private ConnectionState _state = ConnectionState.Connecting;
    private int _battery = NoBattery;

    /// <summary>Что сейчас написано в шторке — чтобы не перерисовывать одно и то же.</summary>
    private string _text = "";

    public static void Start()
    {
        var context = Android.App.Application.Context;
        _started = true;
        Diagnostics.CrashReport.ServiceAlive(true);
        Launch(context, new Intent(context, typeof(WheelForegroundService)));
    }

    /// <summary>
    /// Останавливается сервис через самого себя, а не <c>StopService</c>. Причина найдена полевым
    /// выходом 28.07.2026: между <c>startForegroundService()</c> и первым вызовом
    /// <c>startForeground()</c> есть окно в пять секунд, и <c>StopService</c> в этом окне убивал
    /// приложение — Android бросал RemoteServiceException в главный поток. Так сервис при любом
    /// раскладе сначала становится foreground'ом, а уже потом завершается.
    /// </summary>
    public static void Stop()
    {
        if (!_started) return;

        _started = false;
        var context = Android.App.Application.Context;
        var intent = new Intent(context, typeof(WheelForegroundService));
        intent.SetAction(StopAction);
        Diagnostics.CrashReport.ServiceAlive(false);
        Launch(context, intent);
    }

    private static void Launch(Context context, Intent intent)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        CreateChannel();

        // Сначала обязательно foreground — иначе система убьёт процесс, даже если мы пришли сюда
        // только затем, чтобы остановиться.
        StartForeground(NotificationId, Build(_text.Length > 0 ? _text : AppStrings.ServiceNotification));

        if (intent?.Action == StopAction)
        {
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        Watch();

        // NotSticky: if Android kills the process, the connection is gone with it and silently
        // restarting a service with no session behind it would only look like it still works.
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        _telemetry?.Dispose();
        _telemetry = null;
        _connection?.Dispose();
        _connection = null;
        base.OnDestroy();
    }

    /// <summary>
    /// Подписки на сессию: состояние — как оно есть, заряд — раз в секунду. Оригинал перерисовывает
    /// уведомление на каждом кадре (<c>MainActivity.kt</c>, <c>ACTION_WHEEL_DATA_AVAILABLE</c>), то
    /// есть до пяти раз в секунду; нам столько незачем — заряд меняется раз в минуты, а
    /// <c>Notify</c> дешёвым не бывает. Отсюда <c>Sample</c> в секунду плюс отсев ниже: система
    /// трогается только когда строка на экране действительно другая.
    /// <para>
    /// Вызывается на каждый <c>OnStartCommand</c> — а он приходит и на повторный <c>Start()</c>
    /// (переподключение), — поэтому подписка ставится один раз.
    /// </para>
    /// </summary>
    private void Watch()
    {
        if (_telemetry is not null) return;

        try
        {
            var session = MainApplication.Services.GetRequiredService<WheelSession>();

            _state = session.CurrentState;
            _battery = session.LastSnapshot?.Battery ?? NoBattery;
            Render();

            _connection = session.State.Subscribe(state =>
            {
                _state = state;
                Render();
            });

            _telemetry = session.Telemetry
                .Sample(TimeSpan.FromSeconds(1))
                .Subscribe(snapshot =>
                {
                    _battery = snapshot.Battery;
                    Render();
                });
        }
        catch (Exception ex)
        {
            // Контейнер может быть ещё не собран (сервис поднят раньше приложения) — тогда в шторке
            // просто останется общая строка. Ронять из-за неё сервис нельзя: он тут не ради текста.
            Log(ex);
        }
    }

    /// <summary>
    /// Собирает строку и отдаёт её системе, только если она изменилась. Сравнение — не экономия на
    /// спичках: без него секундный тик перерисовывал бы одно и то же всю поездку.
    /// </summary>
    private void Render()
    {
        string text = _battery >= 0
            ? string.Format(CultureInfo.CurrentCulture, AppStrings.ServiceNotificationData, Status(_state), _battery)
            : Status(_state);

        if (text == _text) return;
        _text = text;

        try
        {
            // Именно Notify, а не StartForeground: идентификатор тот же, уведомление то же самое —
            // сервис остаётся foreground'ом, меняется только текст.
            NotificationManagerCompat.From(this)?.Notify(NotificationId, Build(text));
        }
        catch (Exception ex)
        {
            // Разрешения на уведомления может не быть (Android 13+, отказали в запросе §2.3).
            // Это ровно та мелочь, ради которой нельзя ронять то, что держит поездку.
            Log(ex);
        }
    }

    /// <summary>Состояние связи словами главного экрана — второй раз те же слова не выдумываем.</summary>
    private static string Status(ConnectionState state) => state switch
    {
        ConnectionState.Connected => AppStrings.StateConnected,
        ConnectionState.Connecting => AppStrings.StateConnecting,
        ConnectionState.Reconnecting => AppStrings.StateReconnecting,
        _ => AppStrings.StateDisconnected,
    };

    private Notification Build(string text)
    {
        var launchApp = PackageName is null ? null : PackageManager?.GetLaunchIntentForPackage(PackageName);
        var openApp = PendingIntent.GetActivity(this, requestCode: 0, launchApp, PendingIntentFlags.Immutable);

        // Built step by step rather than as a chain: every setter is bound as returning a nullable
        // builder, so chaining them reads as a string of possible null dereferences.
        var notification = new NotificationCompat.Builder(this, ChannelId);
        notification.SetContentTitle("WheelTalk");
        notification.SetContentText(text);
        notification.SetSmallIcon(Resource.Drawable.ic_notification);
        notification.SetContentIntent(openApp);
        notification.SetOngoing(true);
        notification.SetPriority(NotificationCompat.PriorityLow);

        // Время начала не показываем: на строке связи оно читается как время поездки, а это не оно.
        notification.SetShowWhen(false);

        return notification.Build()!;
    }

    private static void Log(Exception ex)
    {
        try
        {
            MainApplication.Services.GetRequiredService<ILogger<WheelForegroundService>>()
                .LogWarning(ex, "Service.NotificationUpdateFailed");
        }
        catch (Exception)
        {
            // Логгера нет — значит нет и контейнера. Сказать некому, и это не повод падать.
        }
    }

    private void CreateChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

        var channel = new NotificationChannel(ChannelId, AppStrings.ServiceChannelName, NotificationImportance.Low)
        {
            Description = AppStrings.ServiceChannelDescription,
        };
        channel.SetSound(null, null);
        channel.EnableVibration(false);

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
    }
}
