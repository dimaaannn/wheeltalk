using System.Reactive.Linq;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WheelTalk.Core.Alerts;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Detection;
using WheelTalk.Core.Ports;
using WheelTalk.Core.Services;
using WheelTalk.Core.Settings;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Dashboard.Droid.Screen.Tiles;
using WheelTalk.Dashboard.Droid.Widgets;
using WheelTalk.Droid.Ble;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Diagnostics;
using WheelTalk.Droid.Logging;
using WheelTalk.Droid.Resources.Strings;
using WheelTalk.Droid.App;
using WheelTalk.Droid.Recording;
using WheelTalk.Droid.Rides;
using WheelTalk.Droid.Scan;
using WheelTalk.Droid.Settings;
using WheelTalk.Droid.Telemetry;
using WheelTalk.Droid.Ui;

namespace WheelTalk.Droid.Main;

/// <summary>
/// Хозяин главного экрана. Сам он не рисует (план 17 §2, план 23 §2.1): показом занят
/// <see cref="IMainScreen"/> в рамке <see cref="MainScreenView"/>, а здесь — жизненный цикл,
/// подключение и погоня, сервис, разрешения, замок и яркость, шторка, полоса тревоги, инсеты и
/// кадровый цикл. Портировано по <c>docs/native-rewrite-inventory.md</c> §3 с эталона
/// <c>WheelTalk.App/Pages/MainPage.xaml(.cs)</c> — компоновка и поведение те же, разметка собирается
/// кодом (без AXML/XAML), как и весь остальной каркас.
/// <para>
/// Кадр ведёт общий со стендом <see cref="MainScreenDriver"/> (план 19 Б2): он сам ставит себя в
/// очередь <c>View.PostOnAnimation</c> — ту же очередь Choreographer'а (ANIMATION), в которую панель
/// сама кладёт свой <c>PostInvalidateOnAnimation</c> в конце <c>OnDraw</c>, — значит его тик и
/// перерисовка панели идут от одного и того же вертикального синхроимпульса, а не от параллельного
/// таймера со своей частотой (риск биения — опись §7). Здесь остаётся ответ на вопрос «что
/// показать» — <see cref="BuildFrame"/> поверх сессии, следа и рекордера, — и исполнение намерений
/// экрана (<see cref="OnScreenIntent"/>). То, что «на кадре, но не про экран» (флаги окна), — в
/// <see cref="BeforeFrame"/>, хуке водителя.
/// </para>
/// </summary>
// Label — подпись под значком в списке приложений: тем же ресурсом, что имя приложения в манифесте,
// иначе они разойдутся при первом же переименовании.
//
// Name задано руками, потому что на это имя ссылаются снаружи — командный вход для агентских
// прогонов (см. HandleCommand и AGENTS.md, «Как гонять приложение без колеса»). Без него .NET for
// Android сам собирает имя Java-класса из crc64-хеша пространства имён (`crc64….MainActivity`), и
// хеш меняется вместе с этим пространством имён: `am start -n …` начал бы отвечать «Activity class
// does not exist» после обычного переименования папки. Имя компонента — контракт, а не деталь сборки.
[Activity(Name = "com.wheeltalk.droid.MainActivity",
    Label = "@string/app_name", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class MainActivity : Activity
{
    private static readonly TimeSpan DoubleBackWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Какой основной экран показан. Запоминается между запусками (план 23 §2.3) и <b>общий на все
    /// колёса</b>: это привычка человека, а не свойство колеса, — потому <c>globalOnly</c> при
    /// записи. Описания в <c>SettingsCatalogue</c> у ключа нет намеренно: строкой в настройках
    /// экран не выбирают, его выбирают корешком в шторке, а слой хранения тут нужен ровно один — тот
    /// же самый.
    /// </summary>
    private const string ScreenChoiceKey = "Screen:Main";

    private const string PanelChoice = "panel";
    private const string TilesChoice = "tiles";

    private WheelSession _session = null!;
    private ITransport _transport = null!;
    private RideRecorder _recorder = null!;
    private WheelOptions _wheel = null!;
    private WheelIdentity _identity = null!;
    private ScreenOptions _screenOptions = null!;
    private PowerOptions _power = null!;
    private IWheelConfig _wheelConfig = null!;
    private IObservable<AlertState> _alerts = null!;
    private TimeProvider _timeProvider = null!;
    private ILogger<MainActivity> _logger = null!;

    private DashboardOptions _dashboardOptions = null!;
    private RideTrace _trace = null!;
    private LayeredSettings _layers = null!;
    private MainScreenView _screen = null!;
    private MainScreenDriver _driver = null!;

    /// <summary>Экран плиток. Собирается при первом показе: райдеру, который его не открывает, он не стоит ничего.</summary>
    private TilesScreen? _tiles;

    private string _screenChoice = PanelChoice;

    private IDisposable? _telemetry;
    private IDisposable? _alertSubscription;
    private IDisposable? _snapshotClock;
    private AlertState _alert = AlertState.Quiet;
    private long _lastSnapshotAt;
    private long _lastBackPressAt;
    private bool _autoConnectTried;
    private bool _keepScreenOn;
    private bool _showOverLock;

    /// <summary>Высота статус-бара: её забирает панель, а не паддинг корня (прогон 5).</summary>
    private int _topInsetPx;

    /// <summary>
    /// Почему подключиться нельзя вовсе — типизированная причина для ядра (<see cref="LinkStatus.Evaluate"/>,
    /// план 19 Б4) и текст для показа рядом с ней. <see cref="LinkProblem.None"/> означает «причин не
    /// знаем», и тогда отключённое состояние — это просто покой.
    /// </summary>
    private LinkProblem _problem = LinkProblem.None;

    /// <summary>
    /// Текст причины — по месту показа, а не в ядре: у большинства причин он фиксирован
    /// (<c>AppStrings</c>), а у отказа колеса несёт имя семейства из текста исключения и фиксированным
    /// быть не может.
    /// </summary>
    private string _problemDetail = "";

    private Typeface _regular = Typeface.Default!;
    private Typeface _bold = Typeface.DefaultBold!;

    private AlertStrip _alertStrip = null!;
    private QuickSheet _sheet = null!;

    private GestureDetector _sheetGestureDetector = null!;
    private GestureDetector _tapDetector = null!;


    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Панель занимает экран целиком, системный заголовок ей не положен — MAUI-эталон прятал
        // его через Shell.NavBarIsVisible=false (приёмка визуала 29.07.2026, пункт 1).
        ActionBar?.Hide();

        // Показ панели поверх замка (план 16 §2, способ А) больше не ставится здесь и навсегда:
        // это переключатель в шторке, выключенный по умолчанию, — см. ApplyShowOverLock.

        LoadFonts();
        _tapDetector = new GestureDetector(this, new TapListener(OnTapped));

        // Свайп вверх открывает шторку, но только от нижней кромки (quick-commands-design.md §2):
        // весь остальной экран — приборы, и жест по ним не должен значить ничего. Было 64 dp,
        // на ходу палец не всегда в них попадал — поднято до 96 (владелец 04.08.2026). Чисто
        // числовая правка: зона невидима, разметке панели не стоит ни пикселя ни до, ни после.
        int edgeZonePx = this.Dp(96);
        int screenHeightPx = Resources!.DisplayMetrics!.HeightPixels;
        _sheetGestureDetector = new GestureDetector(this,
            new SwipeUpFromEdgeListener(() => _sheet.Toggle(), screenHeightPx, edgeZonePx));

        _session = MainApplication.Services.GetRequiredService<WheelSession>();
        _transport = MainApplication.Services.GetRequiredService<ITransport>();
        _recorder = MainApplication.Services.GetRequiredService<RideRecorder>();
        _wheel = MainApplication.Services.GetRequiredService<IOptions<WheelOptions>>().Value;
        _identity = MainApplication.Services.GetRequiredService<WheelIdentity>();
        _screenOptions = MainApplication.Services.GetRequiredService<IOptions<ScreenOptions>>().Value;
        _power = MainApplication.Services.GetRequiredService<IOptions<PowerOptions>>().Value;
        _wheelConfig = MainApplication.Services.GetRequiredService<IWheelConfig>();
        _alerts = MainApplication.Services.GetRequiredService<IObservable<AlertState>>();
        _dashboardOptions = MainApplication.Services.GetRequiredService<DashboardOptions>();
        _trace = MainApplication.Services.GetRequiredService<RideTrace>();
        _layers = MainApplication.Services.GetRequiredService<LayeredSettings>();
        _timeProvider = MainApplication.Services.GetRequiredService<TimeProvider>();
        _logger = MainApplication.Services.GetRequiredService<ILogger<MainActivity>>();

        // Источник вместо покадрового зеркала (план 19 Б5): след поездки сам берёт живую настройку
        // сглаживания у панели, когда она ему понадобится.
        _trace.SmoothingSecondsSource = () => _dashboardOptions.SmoothingSeconds;

        // Водитель кадра — до разметки: она сама выбирает, какой экран показать (запомненный с
        // прошлого запуска), и ставит водителя на него.
        _driver = new MainScreenDriver(BeforeFrame);

        var root = BuildLayout();
        SetContentView(root);

        // Верхний инсет забирает панель, а не паддинг корня: фон уходит под статус-бар, приборы и
        // плашка связи начинаются ниже него. Прочие экраны применяют инсеты как раньше.
        // Полоса тревоги — второй элемент у той же кромки (план 22 §1): тем же значением добирает
        // свой верхний паддинг, иначе она встаёт под статус-бар, когда панель уже ниже него.
        EdgeToEdge.Apply(this, root, top =>
        {
            _topInsetPx = top;
            _alertStrip.TopInset = top;
        });

        // Возраст показаний считаем по приходу настоящих отсчётов, а не по жизненному циклу экрана
        // (план 11 — вуаль устаревших данных): эта подписка живёт всю Activity, не только пока
        // экран виден, иначе уход на «Данные» и обратно (OnStop/OnStart) обнулял бы возраст на
        // «сейчас» и прятал вуаль от простой перерисовки, а не от свежих данных.
        _lastSnapshotAt = _timeProvider.GetTimestamp();
        _snapshotClock = _session.Telemetry.Subscribe(_ => _lastSnapshotAt = _timeProvider.GetTimestamp());

        // Приложение подняли самой командой — тогда extras лежат в стартовом Intent, и OnNewIntent
        // не будет вовсе.
        HandleCommand(Intent);

        // Последним в сборке экрана, как и у оригинала: сперва экран, потом системный диалог поверх
        // него, а не наоборот.
        AskAboutBatterySaver();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);

        // SetIntent намеренно не зовём: extras разбираются тут же и больше никем не читаются, а
        // подменённый Intent пережил бы пересоздание Activity и повторил бы команду при
        // восстановлении экрана.
        HandleCommand(intent);
    }

    /// <summary>
    /// Командный вход для прогонов без касаний (план 22 §2):
    /// <c>am start -n com.wheeltalk.droid/.MainActivity --es replay start|stop</c> и
    /// <c>--es open rides|data|settings|sheet</c>. Заведён потому, что <c>input swipe/tap</c> по
    /// координатам и таймингу промахивается хронически, а каждый промах — потерянный прогон.
    /// <para>
    /// Существует только у реплей-транспорта: на живом колесе внешней команды нет вовсе — не
    /// спрятана, а не создана, тем же правилом, что кнопка «Пуск» в шторке.
    /// </para>
    /// <para>
    /// Своей логики здесь нет — команда зовёт ровно то же, что кнопка. Отличие одно: «start» и
    /// «stop» названы явно, а не переключают. Тумблер на уже запущенном реплее остановил бы его,
    /// то есть команда промахивалась бы ровно так же, как касание по координатам.
    /// </para>
    /// </summary>
    private void HandleCommand(Intent? intent)
    {
        if (intent is null || !_transport.IsReplay) return;

        if (intent.GetStringExtra("replay") is { } replay) RunReplayCommand(replay);
        if (intent.GetStringExtra("open") is { } screen) RunOpenCommand(screen);
    }

    private async void RunReplayCommand(string value)
    {
        _logger.LogInformation("Ui.Command replay={Value} State={State}", value, _session.CurrentState);

        if (value is not ("start" or "stop"))
        {
            _logger.LogWarning("Ui.CommandUnknown replay={Value}", value);
            return;
        }

        try
        {
            await ReplaySetRunningAsync(value == "start");
        }
        catch
        {
            // Причина уже в журнале (ReplaySetRunningAsync), а из async void исключение уронило бы
            // приложение — командный вход отладочный и ронять его не должен.
        }
    }

    private void RunOpenCommand(string value)
    {
        _logger.LogInformation("Ui.Command open={Value}", value);

        switch (value)
        {
            case "rides": OpenScreen(typeof(RidesActivity)); break;
            case "data": OpenScreen(typeof(TelemetryActivity)); break;
            case "settings": OpenScreen(typeof(SettingsActivity)); break;
            case "sheet": _sheet.Toggle(); break;
            default: _logger.LogWarning("Ui.CommandUnknown open={Value}", value); break;
        }
    }

    protected override void OnDestroy()
    {
        // Жизненный цикл экрана пишется в журнал не для порядка: когда после кармана вместо
        // панели встречает рабочий стол, отличить «ушла сама» от «убита системой» больше нечем —
        // процесс к тому времени жив, а Activity уже нет (план 16 §3, шаг 2).
        _logger.LogInformation("Ui.ScreenDestroyed IsFinishing={IsFinishing}", IsFinishing);
        _snapshotClock?.Dispose();
        _snapshotClock = null;
        base.OnDestroy();
    }

    protected override void OnStart()
    {
        base.OnStart();

        _logger.LogInformation("Ui.ScreenStarted");

        _telemetry = _session.Telemetry.Subscribe(s => RunOnUiThread(() => Render(s)));

        if (_session.LastSnapshot is { } snapshot) Render(snapshot);

        _alertSubscription = _alerts.Subscribe(a => _alert = a);

        _driver.Start();

        // Реплей не запускается сам: на телефоне это внезапная тревога в полный голос, и не факт,
        // что рядом окажется, чем её выключить. Ждём «Пуск» в шторке.
        if (_transport.IsReplay) return;

        if (_autoConnectTried) return;
        _autoConnectTried = true;

        // Одного флага выше мало: он живёт в Activity, а сессия — в контейнере и переживает её.
        // Android разбирает экран, пока телефон заблокирован, и при возврате создаёт новый — у него
        // флаг снова false. Спрашиваем саму сессию: она уже при деле — подключена или гонится за
        // колесом — значит подключать нечего.
        //
        // Без этой проверки возвращение в приложение рвало живую связь: ConnectAsync начинается с
        // DisconnectAsync. Снаружи это и есть та жёлтая «Подключение», которая мгновенно зеленеет;
        // внутри — новый WheelState на каждое возвращение, то есть обнулённые максимумы поездки.
        if (_session.CurrentState != ConnectionState.Disconnected) return;

        if (_wheel.Address.Length == 0)
        {
            _problem = LinkProblem.NoWheelSelected;
            _problemDetail = AppStrings.StateNoWheelSelected;
            return;
        }

        _ = AutoConnectAsync();
    }

    protected override void OnStop()
    {
        _telemetry?.Dispose();
        _telemetry = null;
        _alertSubscription?.Dispose();
        _alertSubscription = null;

        _driver.Stop();

        _logger.LogInformation("Ui.ScreenStopped");
        base.OnStop();
    }

    protected override void OnResume()
    {
        base.OnResume();

        // «Мы должны быть живы» — план 11 §0. В MAUI это стояло в App.OnResume/OnSleep; здесь у
        // Application нет такого колбэка, а у Activity есть, и семантика та же («экран активен»).
        CrashReport.ActivityAlive(true);
    }

    protected override void OnPause()
    {
        CrashReport.ActivityAlive(false);
        base.OnPause();
    }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        if (ev is not null)
        {
            // Только пока шторка закрыта: её зона свайпа (нижние 96 dp) — та же полоса экрана, где
            // сидит ряд её собственных кнопок, когда шторка открыта. Кормить детектор фликов и там
            // тоже значило бы, что обычный тап по кнопке — с естественным дребезгом пальца при
            // отрыве — может попутно распознаться как свайп-вверх-от-кромки и вызвать свой
            // Toggle() поверх того, что уже сделал клик по кнопке: то самое «шторка сама
            // переоткрывается/закрывается» (план 11, дефект найден проверочным проходом 29.07.2026).
            if (!_sheet.IsOpen)
            {
                _sheetGestureDetector.OnTouchEvent(ev);
                _tapDetector.OnTouchEvent(ev);
            }
        }
        return base.DispatchTouchEvent(ev);
    }

    /// <summary>
    /// Жест ловит хозяин — <c>DispatchTouchEvent</c> у него, — а во что касание попало, решает сам
    /// экран: метки нарисованы на его канве, и координаты хозяину знать нечего.
    /// </summary>
    private void OnTapped(float x, float y) => _screen.Current.Tap(x, y);

    /// <summary>
    /// Исполнение намерений экрана — то, чего экран не делает сам никогда: переходы и связь.
    /// <para>
    /// «Показать запись» — тап по метке записи. Единственным входом к поездкам метка быть перестала
    /// — в шторке есть «Поездки», ведущие прямо в список (и через него к плееру). За меткой осталось
    /// своё: она про запись, и ведёт туда, где видно, что пишется прямо сейчас, и где включается
    /// сырой дамп перед выездом. Здесь стояло обратное, пока входа в шторке не было.
    /// </para>
    /// </summary>
    private void OnScreenIntent(MainScreenIntent intent)
    {
        switch (intent)
        {
            case MainScreenIntent.ShowConnection: OnLinkBadgeTapped(); break;
            case MainScreenIntent.ShowRecording: OpenScreen(typeof(RecordingActivity)); break;
        }
    }

    /// <summary>
    /// Тап по плашке связи. Плашка видна ровно тогда, когда связи нет или она только что появилась,
    /// и говорит она про подключение — значит и вести должна туда же, где подключаются. Пока колесо
    /// не поймано, это единственная крупная цель на экране, и искать ради этого шторку не надо.
    /// <para>
    /// Реплей — тот же принцип: плашка «Запись готова» сама и есть пуск (решение владельца
    /// 02.08.2026, план 22 §2) — открывать шторку ради единственной кнопки незачем.
    /// </para>
    /// <para>
    /// Подключены — тап не делает ничего: обрывать связь случайным касанием посреди поездки нельзя,
    /// а отключение живёт в шторке, где спрашивает подтверждение на ходу.
    /// </para>
    /// </summary>
    private void OnLinkBadgeTapped()
    {
        if (_session.CurrentState == ConnectionState.Connected) return;

        if (_transport.IsReplay && _session.CurrentState == ConnectionState.Disconnected)
        {
            OnStateTapped();
            return;
        }

        OpenScreen(typeof(ScanActivity));
    }

    /// <summary>
    /// Leaving is deliberate only: a stray back press mid-ride must not tear down the connection,
    /// so the first one warns and the second within a couple of seconds actually quits.
    /// </summary>
    public override void OnBackPressed()
    {
        if (_timeProvider.GetElapsedTime(_lastBackPressAt) < DoubleBackWindow)
        {
            _ = ExitAsync();
            return;
        }

        _lastBackPressAt = _timeProvider.GetTimestamp();
        _alertStrip.Show(AppStrings.StripBackAgainToExit, AlertStrip.Notice);
    }

    /// <summary>
    /// Уход из приложения — такой же конец поездки, как кнопка «стоп» (план 23 §5.4): незакрытых
    /// поездок не бывает, а <c>ended_at IS NULL</c> значит ровно «идёт прямо сейчас». Не закрыть её
    /// здесь — значит сделать штатный выход неотличимым от смерти телефона.
    /// <para>
    /// Порядок такой, а не «дождались и ушли»: разметка снимается сразу, синхронно, а запись конца
    /// идёт своим чередом — она копится до полутора секунд, и держать на них погашенный экран
    /// незачем. Процесс после <c>FinishAffinity</c> живёт, и запись успевает лечь.
    /// </para>
    /// </summary>
    private async Task ExitAsync()
    {
        var closed = _recorder.StopAsync();
        FinishAffinity();

        await closed;

        // Сервис остановит подписка CrashGuard по Disconnected — как и при любом другом отключении.
        await _session.DisconnectAsync();
    }

    /// <summary>Единственный мост между системным диалогом разрешений и BleReadiness — оно само Activity не видит.</summary>
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        BleReadiness.OnRequestPermissionsResult(requestCode, grantResults);
    }

    private async Task AutoConnectAsync()
    {
        try
        {
            // Спрашивать разрешения на Bluetooth, когда вместо колеса подставлен записанный дамп, —
            // бессмысленно и вредно: отказ запретил бы читать файл.
            if (!_transport.IsReplay && await BleReadiness.FindProblemAsync(this) is { } problem)
            {
                // Причина уезжает в плашку связи: красная фаза — это ровно «подключиться нечем», и
                // она говорит человеку то же самое, но там, где он и так ищет состояние.
                _problem = problem.Cause;
                _problemDetail = problem.Message;
                return;
            }

            _problem = LinkProblem.None;
            _problemDetail = "";

            await Connect(_wheel.Address);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connect.Failed {Mac}", _wheel.Address);
        }
    }

    /// <summary>
    /// Хук <see cref="MainScreenDriver"/> — что живёт на каждом кадре, но не про экран. Показ — дело
    /// экрана, ему уходит <see cref="MainScreenFrame"/>; здесь остаётся то, о чём ни экран, ни
    /// водитель знать не обязаны: оконные флаги. Зеркалирование сглаживания в след поездки ушло
    /// вместе с этим методом (план 19 Б5) — <see cref="RideTrace.SmoothingSecondsSource"/>, заведённый
    /// в <see cref="OnCreate"/>, читает его сам.
    /// </summary>
    private void BeforeFrame()
    {
        ApplyKeepScreenOn();
        ApplyShowOverLock();
    }

    /// <summary>
    /// Состояние кадра для экрана — всё, что он показывает, посчитанное здесь, у сессии, следа
    /// поездки и рекордера. Спрашивается на каждом кадре водителем.
    /// <para>
    /// Показаний может не быть вовсе (<c>null</c>): пока след пуст — сразу после подключения, до
    /// первого отсчёта — показывать нечего, и экран остаётся с прежними цифрами, а не обнуляется.
    /// </para>
    /// <para>
    /// Состояние связи — так, как его показывает панель (прогон 5). Пять фаз, и каждая отвечает на
    /// свой вопрос: «всё хорошо» — плашки нет; «идёт попытка» — жёлтая со счётчиком; «отключено
    /// хозяином» — серая; «подключиться нечем» — красная; «только что подключились» — зелёная,
    /// которая уходит сама.
    /// </para>
    /// <para>
    /// Связь и свежесть данных — разные вещи: линк может держаться, пока колесо молчит (заснуло,
    /// ушло в защиту, потерялось за телом на повороте). Поэтому подключённое, но замолчавшее колесо
    /// показывается той же жёлтой, что и переподключение: данных нет в обоих случаях.
    /// </para>
    /// </summary>
    private MainScreenFrame BuildFrame()
    {
        var (phase, text) = LinkState();
        return new MainScreenFrame
        {
            Reading = _session.LastSnapshot is { } snapshot && _trace.HasData
                ? DashboardFrame.From(snapshot, _trace, _alert.PwmIntensity)
                : null,
            // Плиткам — то, что колесо сказало прямо сейчас, без сглаживания и следа: они про
            // текущее число, а не про ход величины (план 23 §3.2).
            Snapshot = _session.LastSnapshot,
            LinkPhase = phase,
            LinkText = text,
            LinkSeconds = phase == LinkPhase.Connecting ? (int)StaleFor : 0,
            WheelName = WheelName(),
            Recording = _recorder.IsRecording,
            ShowRecordDot = true,
            ShowSheetHint = !_sheet.IsOpen,
            // Вуаль устаревших данных: замершие цифры на ходу читаются как живые. Плашка связи её
            // не заменяет — она говорит «связи нет», а вуаль метит сами цифры (прогон 5).
            IsStale = LinkStatus.IsStale(StaleFor),
            TopInset = _topInsetPx,
            SpeedExceeded = _alert.SpeedExceeded,
        };
    }

    /// <summary>
    /// Настройка «не гасить экран» — сверяется на кадре и переставляется только при расхождении,
    /// тем же приёмом, что наклон панели: подписка на изменение настроек была бы ещё одним местом,
    /// где связь рвётся незаметно. Флаг живёт ровно пока окно впереди — ушли в фон, и экран снова
    /// гаснет по правилам системы.
    /// </summary>
    private void ApplyKeepScreenOn()
    {
        if (_screenOptions.KeepOn == _keepScreenOn) return;

        _keepScreenOn = _screenOptions.KeepOn;
        if (_keepScreenOn) Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        else Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
    }

    /// <summary>
    /// «Закрепить экран» — показывать панель поверх замка (план 16 §2, способ А). Сверяется на
    /// кадре тем же приёмом, что и «не гасить», и переставляется только при расхождении.
    /// <para>
    /// Раньше стояло безусловно в <c>OnCreate</c>. Стало переключателем и выключено по умолчанию:
    /// пока флаг стоит, кнопка питания показывает приборы вместо замка — вместе со шторкой, из
    /// которой команды колесу нажимаются без разблокировки. Такое приложение берёт себе не молча,
    /// а с разрешения (<see cref="ScreenOptions.ShowOverLock"/>).
    /// </para>
    /// </summary>
    private void ApplyShowOverLock()
    {
        if (_screenOptions.ShowOverLock == _showOverLock) return;

        _showOverLock = _screenOptions.ShowOverLock;
        SetShowWhenLocked(_showOverLock);
    }

    /// <summary>
    /// Просьба исключить приложение из экономии заряда. Как у оригинала
    /// (<c>DialogHelper.checkBatteryOptimizationsAndShowAlert</c>): системный запрос напрямую, при
    /// запуске, пока исключения нет, — и переключатель в настройках как единственный тормоз, потому
    /// что своей памяти об отказе тут не нужно. Выдали исключение — система помнит его сама, и
    /// проверка ниже больше не проходит.
    /// <para>
    /// План 11 §2.4 предлагал мягче — свою подсказку с кнопкой в «О приложении», — из-за политики
    /// Google Play на это разрешение. Пока приложение не в магазине, цена этой мягкости
    /// несоразмерна: там исключение надо ещё найти среди страниц, а здесь оно в одно касание.
    /// Уедем в магазин — меняется одна строка, интент.
    /// </para>
    /// <para>
    /// Чего это **не** делает: не мешает Android разобрать остановленную Activity. Исключение
    /// снимает Doze и ограничения фоновой работы, то есть спасает процесс и службу, а не окно.
    /// Экран, встречающий райдера после разблокировки, — по-прежнему план 16.
    /// </para>
    /// </summary>
    private void AskAboutBatterySaver()
    {
        if (!_power.WarnAboutBatterySaver || _transport.IsReplay) return;

        var power = (PowerManager?)GetSystemService(PowerService);
        if (power is null || power.IsIgnoringBatteryOptimizations(PackageName!)) return;

        try
        {
            StartActivity(new Intent(
                Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations,
                Android.Net.Uri.Parse($"package:{PackageName}")));
        }
        catch (Exception ex)
        {
            // Экрана может не быть вовсе — прошивки бывают и без него. Это не повод падать на старте.
            _logger.LogWarning(ex, "Power.BatterySaverRequestUnavailable");
        }
    }

    /// <summary>Сколько секунд не приходило отсчётов.</summary>
    private double StaleFor => _timeProvider.GetElapsedTime(_lastSnapshotAt).TotalSeconds;

    /// <summary>Перевод состояния сессии в фазу и текст плашки связи — решение принимает ядро (<see cref="LinkStatus"/>), здесь только тексты.</summary>
    private (LinkPhase Phase, string Text) LinkState()
    {
        // Решение принимает ядро (LinkStatus, план 14 Б2); здесь — только тексты и перевод в
        // фазы плашки. Зелёная гаснет сама через две секунды (LinkBadgeDrawable), поэтому
        // «подключено» выставляется всё время, пока связь жива: панель показывает её только в
        // начале.
        return LinkStatus.Evaluate(_session.CurrentState, StaleFor, _problem) switch
        {
            WheelLink.NoData => (LinkPhase.Connecting, AppStrings.StateNoData),
            WheelLink.Connected => (LinkPhase.JustConnected, AppStrings.StateConnected),
            WheelLink.Connecting => (LinkPhase.Connecting, AppStrings.StateConnecting),
            WheelLink.Reconnecting => (LinkPhase.Connecting, AppStrings.StateReconnecting),
            WheelLink.Failed => (LinkPhase.Failed, _problemDetail),
            _ => (LinkPhase.Idle, _transport.IsReplay
                ? AppStrings.StateReplayReady
                : AppStrings.StateDisconnected),
        };
    }

    /// <summary>
    /// Как зовут колесо: имя его Bluetooth-анонса, а поверх — алиас, если хозяин его задал
    /// (<see cref="WheelIdentity"/>). Общего имени у колёс нет: оно принадлежит колесу, а не
    /// приложению. MAC на главном экране не показывается: он не читается и ничего не говорит о
    /// том, своё ли это колесо (прогон 5).
    /// </summary>
    private string WheelName() =>
        _identity.Resolve(_wheel.Address, _session.LastSnapshot?.Model);

    private async Task Connect(string address)
    {
        // Новое подключение — новый выезд, и следы на шкалах начинаются заново.
        _trace.Reset();

        // Имя анонса адаптер узнаёт в скане и подключении — то есть прямо сейчас. Это
        // единственный момент, когда его стоит спросить заново (WheelIdentity.Forget).
        _identity.Forget();

        try
        {
            await _session.ConnectAsync(address);
        }
        catch (Exception ex) when (ex is WheelNotRecognisedException or WheelNotSupportedException)
        {
            // Колесо не наше. Сессия уже отключилась и погоню не начнёт — здесь остаётся сказать
            // человеку, что именно случилось, и почему приложение больше не пытается. Текст исключения
            // несёт имя семейства колеса — фиксированной строкой в AppStrings его не заменить.
            _logger.LogWarning(ex, "Connect.Refused {Mac}", address);
            _problem = LinkProblem.WheelRefused;
            _problemDetail = ex.Message;
            ShowRefusal(ex.Message);
            return;
        }
        catch (Exception ex)
        {
            // The session keeps retrying on its own; this only reports the first failure.
            _logger.LogError(ex, "Connect.Failed {Mac}", address);
        }

        if (_session.CurrentState == ConnectionState.Disconnected) return;

        WheelForegroundService.Start();
    }

    private async Task Disconnect()
    {
        // Сервис остановит подписка CrashGuard — она видит Disconnected раньше, чем этот await
        // вернётся, и то же самое делает для всех остальных путей отключения.
        await _session.DisconnectAsync();
        _alertStrip.Hide();

        // «Действует до … конца поездки» (design doc §3) — отключение и есть конец поездки здесь.
        _sheet.Unpin();
    }

    /// <summary>
    /// Приход очередного отсчёта. Экран здесь не трогается вовсе: он живёт на своём кадровом
    /// цикле (<see cref="MainScreenDriver"/>) и берёт то, что накопилось. Здесь только копится — и
    /// поднимается полоса тревоги, которая на приход отсчёта как раз и должна реагировать.
    /// </summary>
    private void Render(TelemetrySnapshot snapshot)
    {
        _trace.Push(snapshot);
        ShowWheelAlert(snapshot);
    }

    private void ShowWheelAlert(TelemetrySnapshot snapshot)
    {
        if (_session.CurrentState != ConnectionState.Connected) return;

        string alertText = snapshot.AlertForDisplay;
        bool alarming = snapshot.WheelAlarm || alertText.Length > 0;
        if (alarming)
        {
            _alertStrip.Show(alertText.Length > 0 ? alertText : AppStrings.StripWheelAlarm, AlertStrip.Danger);
        }
        else
        {
            _alertStrip.Hide();
        }
    }

    private async void OnStateTapped()
    {
        try
        {
            if (_transport.IsReplay)
            {
                await ReplayToggleAsync();
                return;
            }

            if (_session.CurrentState == ConnectionState.Disconnected)
            {
                OpenScreen(typeof(ScanActivity));
                return;
            }

            bool moving = _session.LastSnapshot is { } snapshot && Math.Abs(snapshot.SpeedKmh) > 0.5;
            if (moving && !await ConfirmAsync(AppStrings.DisconnectTitle, AppStrings.DisconnectMessageMoving,
                    AppStrings.DisconnectConfirm, AppStrings.Cancel))
            {
                return;
            }

            await Disconnect();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ui.StateTapFailed {State}", _session.CurrentState);
            _alertStrip.Show(AppStrings.ActionFailed, AlertStrip.Danger);
        }
    }

    /// <summary>
    /// Возвращает экран к тому, что он показывает до первого отсчёта. Вместе с показаниями уходит
    /// и след поездки.
    /// <para>
    /// Пустые показания приходится сказать явно: кадром без данных (<c>Reading = null</c>) экран
    /// оставляет прежние цифры — это его правило для мига между подключением и первым отсчётом.
    /// </para>
    /// </summary>
    private void ClearReadings()
    {
        _trace.Reset();
        _screen.Current.Show(BuildFrame() with { Reading = DashboardReading.Idle });
    }

    /// <summary>
    /// Переход на другой экран приложения — только с разблокированного телефона.
    /// <para>
    /// Граница «Закрепить экран» (план 16 §2) проходит здесь: поверх замка отдаются приборы и
    /// шторка с командами колесу — это и есть то, ради чего флаг включают, — но не телефон целиком.
    /// Ни один другой наш экран поверх замка не появляется: заблокирован — переход не происходит
    /// вовсе, вместо него поднимается замок.
    /// </para>
    /// <para>
    /// После разблокировки экран **не** открывается сам, хотя технически мог бы. Просьба уйти с
    /// панели на заблокированном телефоне — это просьба разблокировать, и только: человек снимает
    /// замок и остаётся там, где стоял. Нужны были настройки — свайп повторяется, это одно
    /// движение; а вот экран, всплывающий сам через полминуты после случайного касания в кармане,
    /// объяснить нечем.
    /// </para>
    /// <para>
    /// Замка нет — обычный случай, телефон в руках — экран открывается сразу, лишнего кадра не
    /// появляется.
    /// </para>
    /// </summary>
    private void OpenScreen(Type screen)
    {
        // IsDeviceLocked, а не IsKeyguardLocked. Вторая отвечает «замок показан и не перекрыт», а
        // наша же панель поверх замка его и перекрывает — то есть ровно тогда, когда защита нужна,
        // она отвечает «замка нет», и экран открывался свободно (найдено на телефоне 31.07.2026).
        // IsDeviceLocked спрашивает про учётные данные: перекрытие окна на неё не влияет, а на
        // телефоне без кода она честно возвращает false — там и снимать нечего.
        var keyguard = (KeyguardManager?)GetSystemService(Context.KeyguardService);
        if (keyguard is { IsDeviceLocked: true })
        {
            keyguard.RequestDismissKeyguard(this, callback: null);
            return;
        }

        StartActivity(new Intent(this, screen));
    }

    /// <summary>
    /// Меню шторки (quick-commands-design.md §3): свет · бип · запись · сброс максимумов, плюс
    /// пуск/стоп реплея, когда транспорт — записанный дамп. Собирается один раз: состав меню
    /// фиксирован на всё время жизни экрана, «прыгающего» списка нет — только подписи/подсветка
    /// команд читаются заново на каждой отрисовке шторки.
    /// </summary>
    private IReadOnlyList<QuickSheetCommand> BuildWheelCommands()
    {
        var commands = new List<QuickSheetCommand>
        {
            new()
            {
                Icon = "💡",
                Label = () => _wheelConfig.LightEnabled ? AppStrings.ButtonLightOn : AppStrings.ButtonLight,
                IsOn = () => _wheelConfig.LightEnabled,
                Action = LightAsync,
            },
            new()
            {
                Icon = "📢",
                Label = () => AppStrings.ButtonBeep,
                Action = BeepAsync,
            },
            new()
            {
                // Красный кружок, а не «⏺» (U+23FA) — тот из того же блока, что и символ питания
                // выше, и с той же судьбой в системном шрифте.
                Icon = "🔴",
                Label = () => _recorder.IsRecording ? AppStrings.ButtonStopRecording : AppStrings.ButtonRecord,
                IsOn = () => _recorder.IsRecording,
                Action = RecordToggleAsync,
                // Второй вход на экран записи (план 23 §5.8): точка записи на панели мала и есть не
                // всегда, а команда и так про запись — держать её долгий тап нашли там же, куда ведёт
                // сама точка (OnScreenIntent). Короткий тап продолжает пускать/останавливать запись.
                LongPress = () =>
                {
                    OnScreenIntent(MainScreenIntent.ShowRecording);
                    return Task.CompletedTask;
                },
            },
            new()
            {
                Icon = "🔄",
                Label = () => AppStrings.ButtonResetPeaks,
                IsEnabled = () => _trace.HasData,
                Action = ResetPeaksAsync,
            },

            // Подключение и отключение переехали сюда из полосы состояния вместе с ней (прогон 5).
            // Одна команда на оба действия, а не две: они взаимоисключающие, и подпись говорит, что
            // случится сейчас. Поведение прежнее — поиск колеса, если не подключены, и подтверждение
            // на ходу, если едем.
            new()
            {
                // Вилка, а не символ питания «⏻» (U+23FB): его нет в системном шрифте символов
                // Android — тот идёт урезанным, и на кнопке был пустой прямоугольник вместо знака
                // (найдено глазами на телефоне 01.08.2026). Правило простое: в шторке — только
                // эмодзи, у них покрытие гарантировано, а знаки из Misc Technical — рулетка.
                Icon = "🔌",
                Label = () => _session.CurrentState == ConnectionState.Disconnected
                    ? AppStrings.ButtonConnect
                    : AppStrings.ButtonDisconnect,
                Action = () =>
                {
                    OnStateTapped();
                    return Task.CompletedTask;
                },
            },
            new()
            {
                // Экран, который не гаснет, нужен на разбор у обочины и на долгий светофор — то
                // есть здесь, рядом с остальными «сейчас», а не в настройках через три экрана.
                // Гаснет он от кнопки питания как обычно; флаг отменяет только таймаут.
                Icon = "☀",
                Label = () => _screenOptions.KeepOn ? AppStrings.ButtonKeepScreenOn : AppStrings.ButtonKeepScreen,
                IsOn = () => _screenOptions.KeepOn,
                Action = () =>
                {
                    _screenOptions.KeepOn = !_screenOptions.KeepOn;
                    return Task.CompletedTask;
                },
            },
            new()
            {
                // Соседняя половина того же вопроса «как телефон ведёт себя эту поездку», поэтому
                // стоит рядом: не гасить экран — и показывать на нём приборы, когда его включили,
                // не требуя разблокировки (план 16 §2). Выключено по умолчанию: панель поверх
                // замка отдаёт шторку с командами любому, кто телефон поднял.
                Icon = "🔒",
                Label = () => _screenOptions.ShowOverLock ? AppStrings.ButtonLockScreenOn : AppStrings.ButtonLockScreen,
                IsOn = () => _screenOptions.ShowOverLock,
                Action = () =>
                {
                    _screenOptions.ShowOverLock = !_screenOptions.ShowOverLock;
                    return Task.CompletedTask;
                },
            },
            new()
            {
                // Вход на экран «Данные». Раньше туда вёл свайп влево по панели — жест убран
                // 31.07.2026 как бесполезный, и место команды здесь: смотреть, что колесо на самом
                // деле отдаёт, — такое же «сейчас», как фара и запись.
                Icon = "📊",
                Label = () => AppStrings.ButtonData,
                Action = () =>
                {
                    OpenScreen(typeof(TelemetryActivity));
                    return Task.CompletedTask;
                },
            },
            new()
            {
                // Вход к списку поездок, а через него — к плееру (поездка → «Пуск»). Тап по точке
                // записи остаётся, но быть единственным входом он не годится: точка — украшение в
                // поле плашки связи, и на выезде 31.07.2026 её не нашли.
                //
                // Ведёт в RidesActivity, а не в RecordingActivity: та отвечает на вопрос «что
                // пишется прямо сейчас», и попасть на неё, нажав «Поездки», — не то, зачем сюда
                // шли. Подпись — слово самого экрана («Поездки»), иначе она путалась с соседней
                // кнопкой «Запись», которая включает и выключает запись.
                Icon = "📁",
                Label = () => AppStrings.ButtonRides,
                Action = () =>
                {
                    OpenScreen(typeof(RidesActivity));
                    return Task.CompletedTask;
                },
            },
            new()
            {
                Icon = "⚙",
                Label = () => AppStrings.ButtonSettings,
                Action = () =>
                {
                    // Через OpenScreen, как и все выходы с главного экрана: настройки поверх замка
                    // не показываются, сперва разблокировка.
                    OpenScreen(typeof(SettingsActivity));
                    return Task.CompletedTask;
                },
            },
        };

        // Реплей не запускается сам: на телефоне это внезапная тревога в полный голос, и не факт,
        // что рядом окажется, чем её выключить. Кнопка есть только у отладочного транспорта — на
        // колесе она не появится ни при каких обстоятельствах, а не просто спрятана.
        if (_transport.IsReplay)
        {
            commands.Add(new QuickSheetCommand
            {
                Icon = "▶",
                Label = () => _session.CurrentState == ConnectionState.Disconnected
                    ? AppStrings.ReplayStart
                    : AppStrings.ReplayStop,
                Action = ReplayToggleAsync,
            });
        }

        return commands;
    }

    private Task LightAsync() => _session.SendCommand(new WheelCommand.SetLight(!_wheelConfig.LightEnabled));

    private Task BeepAsync() => _session.SendCommand(new WheelCommand.Beep());

    private Task RecordToggleAsync()
    {
        _recorder.Toggle();
        return Task.CompletedTask;
    }

    /// <summary>«Сброс максимумов» (design doc §3) — обнуляет пиковые показания, не журнал поездки.</summary>
    private Task ResetPeaksAsync()
    {
        _session.ResetPeaks();
        _trace.ResetPeaks();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Пуск и стоп записанной поездки кнопкой шторки. Стоп нужен не меньше пуска: это единственное,
    /// чем можно оборвать тревогу, когда она звучит в полный голос.
    /// </summary>
    private Task ReplayToggleAsync() =>
        ReplaySetRunningAsync(_session.CurrentState == ConnectionState.Disconnected);

    /// <summary>
    /// Единственное место, где реплей пускается и останавливается: кнопка приходит сюда с
    /// «наоборот, чем сейчас», команда — с явным «start»/«stop». Уже действующее состояние не
    /// трогается, поэтому повтор команды ничего не меняет.
    /// </summary>
    private async Task ReplaySetRunningAsync(bool run)
    {
        bool running = _session.CurrentState != ConnectionState.Disconnected;
        if (running == run) return;

        try
        {
            if (run)
            {
                await Connect(_wheel.Address);
            }
            else
            {
                await Disconnect();
                ClearReadings();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ui.ReplayToggleFailed");
            throw;
        }
    }

    /// <summary>
    /// Колесо не наше — окно с причиной и одной кнопкой. Отказ окончательный: сессия уже
    /// отключилась и повторов не будет, поэтому предлагать тут нечего, кроме «понятно».
    /// </summary>
    private void ShowRefusal(string reason) => RunOnUiThread(() =>
        new AlertDialog.Builder(this)!
            .SetTitle(AppStrings.WheelRefusedTitle)!
            .SetMessage(reason)!
            .SetPositiveButton(AppStrings.Ok, (_, _) => { })!
            .Show());

    /// <summary>Подтверждение через системный диалог — аналог <c>DisplayAlertAsync</c> MAUI.</summary>
    private Task<bool> ConfirmAsync(string title, string message, string positive, string negative)
    {
        var tcs = new TaskCompletionSource<bool>();
        new AlertDialog.Builder(this)!
            .SetTitle(title)!
            .SetMessage(message)!
            .SetCancelable(false)!
            .SetPositiveButton(positive, (_, _) => tcs.TrySetResult(true))!
            .SetNegativeButton(negative, (_, _) => tcs.TrySetResult(false))!
            .Show();
        return tcs.Task;
    }

    // ---- Разметка (см. class doc и docs/native-rewrite-inventory.md §3.1) --------------------

    /// <summary>
    /// Вся визуальная композиция — полоса тревоги, экран, шторка, потолок ширины — живёт в
    /// библиотеке (<see cref="MainScreenView"/>), чтобы стенд показывал ровно это тем же классом.
    /// Здесь остаётся проводка: команды шторки, шрифт из ресурсов приложения, приём намерений экрана
    /// и инсеты (edge-to-edge применяет OnCreate — верхний инсет забирает панель, а не паддинг корня).
    /// </summary>
    private View BuildLayout()
    {
        _screen = new MainScreenView(this, _dashboardOptions);
        _screen.Panel.OnIntent = OnScreenIntent;

        _alertStrip = _screen.Alert;
        _alertStrip.SetTypeface(_bold, TypefaceStyle.Bold);

        _sheet = _screen.Sheet;
        _sheet.PinLabel = () => AppStrings.ButtonPin;
        _sheet.SetCommands(BuildWheelCommands());
        _sheet.SetScreens(BuildScreenTabs());

        // Тот экран, на котором человек ушёл в прошлый раз. Умолчание — панель: с неё начинали все,
        // кто ни разу не трогал корешки.
        ShowScreen(_layers.Get(ScreenChoiceKey).Value ?? PanelChoice);

        return _screen;
    }

    /// <summary>
    /// Корешки экранов над рядом команд (план 23 §2.2). Это не команды: правило «позиции команд
    /// фиксированы навсегда» их не касается, и полоса у них своя.
    /// </summary>
    private IReadOnlyList<QuickSheetScreen> BuildScreenTabs() =>
    [
        new()
        {
            Icon = "📊",
            Label = AppStrings.ScreenPanel,
            IsSelected = () => _screenChoice == PanelChoice,
            Select = () => ChooseScreen(PanelChoice),
        },
        new()
        {
            Icon = "🔢",
            Label = AppStrings.ScreenTiles,
            IsSelected = () => _screenChoice == TilesChoice,
            Select = () => ChooseScreen(TilesChoice),
        },
    ];

    /// <summary>Выбор человека: показать и запомнить. Общий слой, без «этого колеса» — план 23 §2.3.</summary>
    private void ChooseScreen(string choice)
    {
        if (_screenChoice == choice) return;

        ShowScreen(choice);
        _layers.Set(ScreenChoiceKey, choice, globalOnly: true);
    }

    /// <summary>
    /// Смена содержимого рамки. Водитель кадра переставляется на новый экран тут же: его очередь
    /// <c>PostOnAnimation</c> принадлежит той <c>View</c>, которую он обслуживает, а снятая с рамки в
    /// ней больше не стоит. <see cref="MainScreenDriver.Refresh"/> — чтобы новый экран показал
    /// данные сразу, не дожидаясь очередного vsync.
    /// </summary>
    private void ShowScreen(string choice)
    {
        _screenChoice = choice;
        _screen.Show(choice == TilesChoice ? Tiles() : _screen.Panel);
        _driver.Attach(_screen.Current, BuildFrame);
        _driver.Refresh();
    }

    private TilesScreen Tiles() =>
        _tiles ??= new TilesScreen(this, _dashboardOptions, TranslateExtension.Get);

    private void LoadFonts()
    {
        // Resources.GetFont — платформенный API 26+ для файлов из Resources/font; AndroidX здесь не
        // нужен (SupportedOSPlatformVersion уже 28.0).
        _regular = Resources!.GetFont(Resource.Font.opensans_regular) ?? Typeface.Default!;
        _bold = Typeface.Create(_regular, TypefaceStyle.Bold) ?? Typeface.DefaultBold!;
    }

    /// <summary>Одиночный тап с координатами — подтверждённый, чтобы не спорить с двойным.</summary>
    private sealed class TapListener(Action<float, float> onTap) : GestureDetector.SimpleOnGestureListener
    {
        public override bool OnSingleTapConfirmed(MotionEvent? e)
        {
            if (e is null) return false;

            onTap(e.GetX(), e.GetY());
            return false;
        }
    }

}
