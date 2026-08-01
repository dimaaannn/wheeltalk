using System.Reactive.Linq;
using System.Text;
using Android.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WheelTalk.Core.Alerts;
using WheelTalk.Core.Ports;
using WheelTalk.Core.Services;
using WheelTalk.Droid.Alerts;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Diagnostics;
using WheelTalk.Droid.Logging;

namespace WheelTalk.Droid.App;

/// <summary>
/// Global exception interception, crash context, and the app-level subscriptions that have to
/// outlive any one Activity — split out of <c>MainApplication</c> (plan 14, А2.2), bodies moved
/// as-is; the only change is referring to <see cref="MainApplication.Services"/> by its now-external
/// name instead of the unqualified property.
/// </summary>
public static class CrashGuard
{
    private static IDisposable? _alertSubscription;
    private static IDisposable? _autoRecordSubscription;
    private static IDisposable? _serviceSubscription;

    /// <summary>
    /// План 11 §1.1, P0: перехват — это запись и честное падение, не попытка продолжить работу.
    /// Ни один обработчик здесь не глушит исключение (не выставляет <c>Handled</c>/не отменяет
    /// завершение процесса) — единственное исключение из этого правила и то нарочное:
    /// <c>UnobservedTaskException</c> по определению относится к задаче, результат которой уже
    /// никто не ждёт, и ронять из-за неё процесс на ходу — решение хуже, чем записать и продолжить
    /// (см. свою пометку <c>SetObserved()</c> ниже).
    /// </summary>
    public static void SubscribeGlobalExceptionHandlers()
    {
        // Управляемое исключение, вылетевшее в JNI/Java-часть рантайма — то, что раньше просто
        // роняло процесс без единой нашей строки в diagnostics.log.
        AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
            CrashReport.CollectCrash("AndroidEnvironment.UnhandledExceptionRaiser", args.Exception, BuildCrashContext());

        // Второй путь падения управляемого кода — не всё на Android идёт через JNI-перехват выше.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                CrashReport.CollectCrash("AppDomain.UnhandledException", exception, BuildCrashContext());
            }
        };

        // Исключение из fire-and-forget задачи (WheelService.WriteSafe и подобные уже ловят своё
        // сами — это сеть на то, что где-то не поймали). Помечаем как обработанное и не убиваем
        // процесс: по определению это задача, результат которой уже никто не ждёт.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashReport.CollectCrash("TaskScheduler.UnobservedTaskException", args.Exception, BuildCrashContext());
            args.SetObserved();
        };
    }

    /// <summary>
    /// «Что сейчас» — план 11 §1.1: хвост системного буфера объясняет, что упало, но не что в этот
    /// момент делало приложение, и именно этих полей не хватало при разборе падений 28.07.2026.
    /// Читает уже живые синглтоны контейнера, а не заводит вторую копию состояния — крах может
    /// случиться и до <see cref="MainApplication.Services"/> (в процессе сборки контейнера), поэтому
    /// обёрнуто в try: контекст тут — лучше-чем-ничего, а не то, ради чего можно уронить сам
    /// обработчик.
    /// </summary>
    private static string BuildCrashContext()
    {
        try
        {
            var session = MainApplication.Services.GetRequiredService<WheelSession>();
            var recorder = MainApplication.Services.GetRequiredService<RideRecorder>();
            var rawFrames = MainApplication.Services.GetRequiredService<RawFrameRecorder>();
            var transport = MainApplication.Services.GetRequiredService<ITransport>();
            var snapshot = session.LastSnapshot;

            var sb = new StringBuilder();
            sb.AppendLine($"Transport: {transport.GetType().Name}");
            sb.AppendLine($"Wheel: {session.CurrentState} {session.Address} {session.Protocol}");
            sb.AppendLine(snapshot is null
                ? "LastSnapshot: (ни одного кадра ещё не декодировано)"
                : $"LastSnapshot: speed={snapshot.SpeedKmh:F1} km/h pwm={snapshot.Pwm:F0}% voltage={snapshot.VoltageV:F1} V battery={snapshot.Battery}%");
            sb.AppendLine($"Ride: recording={recorder.IsRecording} rideId={recorder.RideId} rowsWritten={recorder.RowsWritten}");
            sb.AppendLine($"RawDump: {(rawFrames.IsRecording ? rawFrames.FileName : "выключен")}");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"(контекст недоступен: {ex.Message})";
        }
    }

    /// <summary>
    /// Подписки, которые в MAUI-версии жили в <c>App.xaml.cs</c>, а не на странице — план 11 §0 и
    /// риск №8 описи §7: тревоги и запись обязаны продолжаться при погашенном экране и пережить
    /// уничтожение любой Activity, поэтому им место в composition root, а не в MainActivity.
    /// </summary>
    public static void SubscribeAppLevelHandlers()
    {
        var alerts = MainApplication.Services.GetRequiredService<IObservable<AlertState>>();
        var signals = MainApplication.Services.GetRequiredService<AlertSignals>();
        _alertSubscription = alerts.Subscribe(signals.Apply);

        // Same reason, plus one of its own: the dump has to catch the frames of a connection that
        // happens before any screen is up, and survive every reconnect after it.
        MainApplication.Services.GetRequiredService<RawFrameRecorder>().Apply();

        var session = MainApplication.Services.GetRequiredService<WheelSession>();

        // Сервис гаснет по состоянию сессии, а не по кнопке: Disconnected случается только когда
        // погоня снята совсем — «Отключить», выход из приложения или отказ опознания, — и во всех
        // трёх держать процесс на плаву больше незачем. Пока сессия ждёт колесо (Reconnecting),
        // сервис обязан жить: без него Android замораживает процесс с погашенным экраном, и
        // возвращение колеса некому встретить (полевой выход 31.07.2026 — «отключено 900 с»).
        _serviceSubscription = session.State
            .Where(state => state == ConnectionState.Disconnected)
            .Subscribe(_ => WheelForegroundService.Stop());

        // Auto-start is off unless asked for. Start() is idempotent, so a reconnect — which reports
        // Connected again — resumes into the same recording instead of splitting it.
        var logging = MainApplication.Services.GetRequiredService<IOptions<LoggingOptions>>().Value;
        if (!logging.AutoStartRide) return;

        var recorder = MainApplication.Services.GetRequiredService<RideRecorder>();

        // Порог скорости — как у оригинала (`startAutoLoggingWhenIsMovingMore`, решение владельца
        // 02.08.2026): ноль означает «писать с подключения», иначе ждём, когда колесо впервые
        // поедет быстрее порога. Смотрим телеметрию, а не состояние связи, и порог берётся на
        // каждом отсчёте — настройка живая, как и все остальные.
        //
        // Start() идемпотентен, поэтому фильтровать «уже пишем» не нужно: как только порог взят
        // однажды, каждый следующий отсчёт просто попадает в ту же запись. Стоянки и зарядка
        // внутри поездки пишутся — на них стоит кривая покоя для прогнозов (план 9 §3).
        _autoRecordSubscription = logging.AutoStartAboveKmh <= 0
            ? session.State.Where(state => state == ConnectionState.Connected).Subscribe(_ => recorder.Start())
            : session.Telemetry.Where(s => s.SpeedKmh > logging.AutoStartAboveKmh).Subscribe(_ => recorder.Start());
    }
}
