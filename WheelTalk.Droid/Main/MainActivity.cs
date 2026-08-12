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
using WheelTalk.Core.Dashboard;
using WheelTalk.Core.Detection;
using WheelTalk.Core.Ports;
using WheelTalk.Core.Services;
using WheelTalk.Core.Settings;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Dashboard.Droid.Screen.Tiles;
using WheelTalk.Dashboard.Droid.Widgets;
using WheelTalk.Droid.Alerts;
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
using WheelTalk.Droid.Settings.Catalogue;
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

    /// <summary>
    /// Счётчик попыток спросить об экономии заряда (bugfix 3 §3.1) — общий на все колёса по той же
    /// причине, что и <see cref="ScreenChoiceKey"/>: привычка райдера, а не свойство колеса. В
    /// каталоге настроек намеренно нет строки — крутить порог показов некому и незачем.
    /// </summary>
    private const string BatterySaverAsksKey = "Power:BatterySaverAsks";


    private WheelSession _session = null!;
    private ITransport _transport = null!;
    private RideRecorder _recorder = null!;
    private WheelOptions _wheel = null!;
    private UserSettingsStore _userSettings = null!;
    private WheelIdentity _identity = null!;
    private ScreenOptions _screenOptions = null!;
    private PowerOptions _power = null!;
    private DiagnosticsOptions _diagnostics = null!;
    private SettingsBinder _binder = null!;
    private IWheelConfig _wheelConfig = null!;
    private IObservable<AlertState> _alerts = null!;
    private AlertBanner _banner = null!;
    private TimeProvider _timeProvider = null!;
    private ILogger<MainActivity> _logger = null!;

    private DashboardOptions _dashboardOptions = null!;
    private RideTrace _trace = null!;
    private LayeredSettings _layers = null!;
    private MainScreenView _screen = null!;
    private MainScreenDriver _driver = null!;

    private MainScreenRegistry _screens = null!;
    private PanelVariants _panels = null!;

    /// <summary>Где живёт состав справочного блока центра — общий слой настроек (план 11 §4.5 порядком).</summary>
    private ICentreLayoutStore _centreLayout = null!;

    /// <summary>
    /// Точки отсчёта дистанций — те же, что у плиток: один экземпляр на приложение, иначе два
    /// затирали бы точки друг друга. Отсюда счётчик поездки в центре панели и его сброс.
    /// </summary>
    private TripPoints _tripPoints = null!;

    /// <summary>
    /// Собранные экраны по идентификатору. Собираются при первом показе: райдеру, который плиток не
    /// открывает, они не стоят ничего.
    /// </summary>
    private readonly Dictionary<string, IMainScreen> _built = new(StringComparer.Ordinal);

    private string _screenChoice = "";

    /// <summary>Каким вариантом собрана панель, что лежит в <see cref="_built"/>: сменили в настройках — пересобрать.</summary>
    private string _panelVariantShown = "";

    /// <summary>
    /// Погоню остановили мы сами, потому что адаптер выключен (план 11 §3.2). Признак нужен ради
    /// <b>пути назад</b>: включили Bluetooth — приложение обязано вернуться к колесу само, а не
    /// остаться «навсегда остановленным».
    /// </summary>
    private bool _chaseStoppedByAdapter;

    /// <summary>Слушает включение и выключение адаптера, пока экран виден. Регистрируется в OnStart, снимается в OnStop.</summary>
    private BluetoothStateReceiver? _adapterWatch;

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

    /// <summary>Окна поверх экрана — отказ колеса и подтверждения. Держит их этот экран, закрывает в <see cref="OnDestroy"/>.</summary>
    private readonly OwnedWindow _windows = new();


    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Панель занимает экран целиком, системный заголовок ей не положен — MAUI-эталон прятал
        // его через Shell.NavBarIsVisible=false (приёмка визуала 29.07.2026, пункт 1).
        ActionBar?.Hide();

        // Показ панели поверх замка (план 16 §2, способ А) больше не ставится здесь и навсегда:
        // это переключатель в шторке, выключенный по умолчанию, — см. ApplyShowOverLock.

        LoadFonts();
        _tapDetector = new GestureDetector(this, new SingleTapListener(OnTapped, OnLongPressed));

        // Свайп вверх открывает шторку, но только от нижней кромки (quick-commands-design.md §2):
        // весь остальной экран — приборы, и жест по ним не должен значить ничего.
        //
        // 64 → 96 → 128 dp (владелец, 04.08.2026). Причина последнего расширения не в размере как
        // таком: на жестовой навигации самые нижние ~10 dp забирает система под свой «домой», и
        // касание, начатое у самой кромки, до этого обработчика не доходит вовсе. Отнять их у
        // системы можно, но правильнее дать пальцу запас выше — тогда промах по системной полосе
        // перестаёт быть промахом по шторке.
        //
        // Правка чисто числовая: зона невидима и разметке панели не стоит ни пикселя. Ограничение
        // сверху одно — в зоне ловится только флик вверх, а тапы по приборам идут своим путём,
        // поэтому расширение не отнимает у панели ни одного жеста.
        int edgeZonePx = this.Dp(128);
        int screenHeightPx = Resources!.DisplayMetrics!.HeightPixels;
        _sheetGestureDetector = new GestureDetector(this,
            new SwipeUpFromEdgeListener(() => _sheet.Toggle(), screenHeightPx, edgeZonePx));

        _session = MainApplication.Services.GetRequiredService<WheelSession>();
        _transport = MainApplication.Services.GetRequiredService<ITransport>();
        _recorder = MainApplication.Services.GetRequiredService<RideRecorder>();
        _wheel = MainApplication.Services.GetRequiredService<IOptions<WheelOptions>>().Value;
        _userSettings = MainApplication.Services.GetRequiredService<UserSettingsStore>();
        _identity = MainApplication.Services.GetRequiredService<WheelIdentity>();
        _screenOptions = MainApplication.Services.GetRequiredService<IOptions<ScreenOptions>>().Value;
        _power = MainApplication.Services.GetRequiredService<IOptions<PowerOptions>>().Value;
        _diagnostics = MainApplication.Services.GetRequiredService<IOptions<DiagnosticsOptions>>().Value;
        _binder = MainApplication.Services.GetRequiredService<SettingsBinder>();
        _wheelConfig = MainApplication.Services.GetRequiredService<IWheelConfig>();
        _alerts = MainApplication.Services.GetRequiredService<IObservable<AlertState>>();
        _banner = MainApplication.Services.GetRequiredService<AlertBanner>();
        _dashboardOptions = MainApplication.Services.GetRequiredService<DashboardOptions>();
        _trace = MainApplication.Services.GetRequiredService<RideTrace>();
        _layers = MainApplication.Services.GetRequiredService<LayeredSettings>();
        _screens = MainApplication.Services.GetRequiredService<MainScreenRegistry>();
        _panels = MainApplication.Services.GetRequiredService<PanelVariants>();
        _tripPoints = MainApplication.Services.GetRequiredService<TripPoints>();

        // Состав центра и слова — в живые настройки панели, раз и до конца жизни приложения: панель
        // читает их каждым кадром, а библиотека ресурсов приложения не видит (тот же порядок, каким
        // слова получают плитки и шторка).
        _centreLayout = MainApplication.Services.GetRequiredService<ICentreLayoutStore>();
        _dashboardOptions.CentreRows = CenterLayout.Sane(_centreLayout.Load());
        _dashboardOptions.Words = TranslateExtension.Get;
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

        // Тоже на всю Activity, а не на видимую её часть: колесо меняют с экрана поиска (план 24
        // §А2), когда панель остановлена, — а вернуться она должна к плиткам нового колеса, не к
        // максимумам прежнего.
        _session.WheelChanged += OnWheelChanged;

        // Погоня забуксовала — спросить о причине (план 11 §3.2). Подписка живёт всю Activity по
        // той же причине, что и смена колеса: буксовать погоня начинает и при остановленном экране.
        _session.ChaseTroubled += OnChaseTroubled;

        // Приложение подняли самой командой — тогда extras лежат в стартовом Intent, и OnNewIntent
        // не будет вовсе.
        HandleCommand(Intent);

        // Последним в сборке экрана, как и у оригинала: сперва экран, потом системный диалог поверх
        // него, а не наоборот.
        AskAboutBatterySaver();

        // После просьбы про экономию заряда, а не одновременно с ней (план молчит, решение
        // владельца 12.08.2026): та поднимает системный экран настроек, и диалог, всплывший в тот
        // же миг, наслоился бы на переход. Показанный следом, он просто встречает вернувшегося
        // назад — одно окно за раз, как и везде на этом экране (см. OwnedWindow).
        OfferCrashShareIfNeeded();
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
        // Окно поверх экрана уходит вместе с ним: брошенное, оно переживает свою активность и течёт
        // (дамп владельца 10.08.2026 — WindowLeaked на уничтоженной MainActivity).
        _windows.Close();
        _snapshotClock?.Dispose();
        _snapshotClock = null;
        _session.WheelChanged -= OnWheelChanged;
        _session.ChaseTroubled -= OnChaseTroubled;
        base.OnDestroy();
    }

    protected override void OnStart()
    {
        base.OnStart();

        _logger.LogInformation("Ui.ScreenStarted");

        // Выключение адаптера приходит событием, и это единственная причина, о которой не надо
        // догадываться (план 11 §3.2). Слушаем, пока экран виден: приёмник, переживающий экран,
        // — это уже служба, а её здесь не заводим.
        _adapterWatch = BluetoothStateReceiver.Register(this, OnAdapterStateChanged);

        // Вариант панели могли выбрать в настройках, пока экран стоял: пересборка здесь и есть
        // «смена без перезапуска» (план 17 §3) — страница настроек всё равно отдельный экран.
        ApplyPanelVariant();

        _telemetry = _session.Telemetry.Subscribe(s => RunOnUiThread(() => Render(s)));

        if (_session.LastSnapshot is { } snapshot) Render(snapshot);

        _alertSubscription = _alerts.Subscribe(a => _alert = a);

        _banner.Changed += OnBannerChanged;
        ShowWheelAlert();

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

        // Райдер сам сказал «оставь это колесо» (план 24 §Б2), и признак живёт в файле настроек —
        // значит переживает и перезапуск приложения, и пересоздание экрана. Причины плашке не
        // нужно: отключённое состояние без причины и есть покой, «Отключено».
        if (_wheel.StoppedByRider) return;

        _ = AutoConnectAsync();
    }

    protected override void OnStop()
    {
        _telemetry?.Dispose();
        _telemetry = null;
        _alertSubscription?.Dispose();
        _alertSubscription = null;
        _banner.Changed -= OnBannerChanged;

        _driver.Stop();

        _adapterWatch?.Unregister(this);
        _adapterWatch = null;

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
    /// Долгий тап — тем же путём: жест ловит хозяин, а во что палец попал, знает экран. У панели это
    /// справочный блок центра, и ответом приходит намерение <c>EditCentre</c>.
    /// </summary>
    private void OnLongPressed(float x, float y) => _screen.Current.LongPress(x, y);

    /// <summary>
    /// Исполнение намерений экрана — то, чего экран не делает сам никогда: переходы и связь.
    /// <para>
    /// «Показать запись» — тап по метке записи. Единственным входом к поездкам метка быть перестала
    /// — в шторке есть «Поездки», ведущие прямо в список (и через него к плееру). За меткой осталось
    /// своё: она про запись, и ведёт туда, где видно, что пишется прямо сейчас, и где включается
    /// сырой дамп перед выездом. Здесь стояло обратное, пока входа в шторке не было.
    /// </para>
    /// <para>
    /// «Открыть шторку» — тап по галочке, второй вход к тому же <c>_sheet.Toggle()</c>, что и жест
    /// свайпа (<see cref="DispatchTouchEvent"/>). Экран сам не открывает ничего — он лишь просит.
    /// </para>
    /// </summary>
    private void OnScreenIntent(MainScreenIntent intent)
    {
        switch (intent)
        {
            case MainScreenIntent.ShowConnection: OnLinkBadgeTapped(); break;
            case MainScreenIntent.ShowRecording: OpenScreen(typeof(RecordingActivity)); break;
            case MainScreenIntent.ShowSheet: _sheet.Toggle(); break;
            case MainScreenIntent.EditCentre: EditCentre(); break;
        }
    }

    /// <summary>
    /// Правка справочного блока центра. Окно открывает хозяин и держит его при себе: правило панели
    /// запрещает ей трогать разметку после сборки, а окно — это разметка (прогон 3); хозяин же
    /// закроет его в <c>OnDestroy</c>, иначе поворот телефона оставит <c>WindowLeaked</c>.
    /// <para>
    /// Сохранение идёт сразу в слои и в живые настройки панели: панель читает состав каждым кадром,
    /// и правка видна на экране за спиной окна.
    /// </para>
    /// </summary>
    private void EditCentre() => _windows.Own(CentreEditor.Show(
        this,
        _dashboardOptions.CentreRows,
        TranslateExtension.Get,
        rows =>
        {
            _dashboardOptions.CentreRows = rows;
            _centreLayout.Save(rows);
        }));

    /// <summary>
    /// Тап по плашке связи. Плашка видна ровно тогда, когда связи нет или она только что появилась,
    /// и говорит она про подключение — значит и вести должна туда же, где подключаются. Пока колесо
    /// не поймано, это единственная крупная цель на экране, и искать ради этого шторку не надо.
    /// <para>
    /// Реплей — тот же принцип: плашка «Запись готова» сама и есть пуск (решение владельца
    /// 02.08.2026, план 22 §2) — открывать шторку ради единственной кнопки незачем.
    /// </para>
    /// <para>
    /// Подключены и данные свежи — тап не делает ничего: обрывать связь случайным касанием посреди
    /// поездки нельзя, а отключение живёт в шторке, где спрашивает подтверждение на ходу.
    /// </para>
    /// <para>
    /// Решает не сырой <c>ConnectionState</c>, а фаза, которую показывает сама плашка
    /// (<see cref="LinkBadgeTap"/> поверх <see cref="LinkStatus.Evaluate"/>): <c>NoData</c> — это
    /// тоже <c>Connected</c> по состоянию сессии (колесо подключено, но кадры замолчали дольше
    /// 1,5 с), и тап там обязан вести в поиск, а не бездействовать (bugfix 2 §2.1, решение владельца
    /// 09.08.2026). Ждём пароль — ведём в настройки, а не в поиск: связи и так не будет.
    /// </para>
    /// <para>
    /// Всё остальное — «оставь это колесо» (план 24 §Б3): гасим сессию, ставим признак и ведём в
    /// поиск. В <c>Reconnecting</c> подтверждения не спрашиваем — телеметрии там всё равно нет, а
    /// поиск при живой погоне бесполезен: попытки подключения отбирают радио у скана.
    /// </para>
    /// </summary>
    private async void OnLinkBadgeTapped()
    {
        try
        {
            var link = LinkStatus.Evaluate(_session.CurrentState, StaleFor, _problem);
            switch (LinkBadgeTap.Decide(link, _session.AwaitingPassword, _transport.IsReplay))
            {
                case LinkBadgeTapAction.None:
                    return;

                case LinkBadgeTapAction.GoToSettings:
                    OpenScreen(typeof(SettingsActivity));
                    return;

                case LinkBadgeTapAction.ToggleReplay:
                    OnStateTapped();
                    return;

                case LinkBadgeTapAction.GoToScan:
                    await Disconnect();
                    _userSettings.SaveStoppedByRider();
                    OpenScreen(typeof(ScanActivity));
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ui.LinkBadgeTapFailed {State}", _session.CurrentState);
            _alertStrip.Show(AppStrings.ActionFailed, AlertStrip.Danger);
        }
    }

    /// <summary>
    /// Leaving is deliberate only: a stray back press mid-ride must not tear down the connection,
    /// so the first one warns and the second within a couple of seconds actually quits.
    /// </summary>
    public override void OnBackPressed()
    {
        // Экран мог взять кнопку себе — плитки так закрывают режим правки. Спрашиваем до счётчика
        // двойного нажатия: иначе выход из правки считался бы первым «назад» к выходу из приложения.
        if (_screen.Current.Back()) return;

        if (_timeProvider.GetElapsedTime(_lastBackPressAt) < DoubleBackWindow)
        {
            _ = ExitAsync();
            return;
        }

        _lastBackPressAt = _timeProvider.GetTimestamp();
        _alertStrip.Show(AppStrings.StripBackAgainToExit, AlertStrip.Notice);

        // Полоса живёт ровно столько, сколько действует второе «назад»: дольше она обещает выход,
        // которого уже не будет. Прячет её возврат к слову тревоги (ShowWheelAlert) — он сам знает,
        // что стоит показывать вместо неё: слово колеса или ничего. Без этого служебная строка
        // висела до следующей смены тревоги, то есть на спокойном колесе — без конца (баг владельца
        // 09.08.2026). Повторное нажатие ставит второй отложенный вызов — не страшно:
        // ShowWheelAlert идемпотентен, а лишний вызов после выхода упирается в закрытый экран.
        _alertStrip.PostDelayed(() => { if (!IsFinishing) ShowWheelAlert(); }, (long)DoubleBackWindow.TotalMilliseconds);
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

        // Пинок полосам тревоги рамки: силу они спрашивают сами (источник стоит в BuildLayout), но
        // из тишины их будит кадр — события «тревога началась» у полос нет намеренно, см.
        // AlertBarsView. Пустая перерисовка тихих полос — два ранних выхода, дешевле подписки.
        _screen.Bars.Invalidate();
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
                ? DashboardFrame.From(snapshot, _trace, _alert.PwmIntensity, TripCounterKm(snapshot))
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
        };
    }

    /// <summary>
    /// Счётчик поездки для центра: одометр минус точка отсчёта, которую двигает только рука хозяина
    /// (кнопка «Сброс пиков»). Молчащий одометр — <c>null</c>, то есть прочерк: без него вычитать
    /// нечего, а ноль читался бы как «никуда не ездили». Колеса нет вовсе — тот же прочерк: сложить
    /// пути разных колёс в одну кучу хуже, чем не показать ничего (правило плиток-дистанций).
    /// </summary>
    private double? TripCounterKm(TelemetrySnapshot snapshot) =>
        snapshot.TotalDistanceKm > 0 && _layers.Scope is { Length: > 0 } wheel
            ? _tripPoints.Since(wheel, TripPoints.Centre, snapshot.TotalDistanceKm)
            : null;

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
    /// запуске, пока исключения нет, — и переключатель в настройках как один из двух тормозов.
    /// Выдали исключение — система помнит его сама, и проверка ниже больше не проходит.
    /// <para>
    /// Второй тормоз — <see cref="BatterySaverAsk"/> (bugfix 3 §3.1, решение владельца 09.08.2026):
    /// сверка с оригиналом расхождений не нашла, но часть прошивок никогда не считает исключение
    /// выданным (вендорский список вместо системного, или Doze чистит его при перезагрузке), и
    /// приложение выпрашивало бы разрешение на каждом запуске до посинения. Спрашиваем три раза,
    /// дальше молчим; включили тумблер заново — снова три.
    /// </para>
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
        // Реплей — не устройство райдера: считать его попытки в общий счётчик значило бы портить
        // решение для настоящих запусков дампом, снятым неизвестно на чём.
        if (_transport.IsReplay) return;

        var power = (PowerManager?)GetSystemService(PowerService);
        bool isIgnoring = power is not null && power.IsIgnoringBatteryOptimizations(PackageName!);
        int asksSoFar = int.TryParse(_layers.Get(_layers.Scope, BatterySaverAsksKey, SettingLayer.GlobalOnly).Value, out int parsed)
            ? parsed
            : 0;

        var decision = BatterySaverAsk.Decide(_power.WarnAboutBatterySaver, isIgnoring, asksSoFar);

        _logger.LogInformation("Power.BatterySaverAsk {ShouldAsk} {IsIgnoring} {AsksSoFar}",
            decision.ShouldAsk, isIgnoring, asksSoFar);

        if (decision.NextAskCount != asksSoFar)
        {
            _layers.Set(_layers.Scope, BatterySaverAsksKey, decision.NextAskCount.ToString(), SettingLayer.GlobalOnly);
        }

        if (!decision.ShouldAsk) return;

        try
        {
            StartActivity(new Intent(
                Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations,
                Android.Net.Uri.Parse($"package:{PackageName}")));
        }
        catch (Exception ex)
        {
            // Экрана может не быть вовсе — прошивки бывают и без него. Это не повод падать на старте,
            // а счётчик уже вырос: считается попытка, а не успех.
            _logger.LogWarning(ex, "Power.BatterySaverRequestUnavailable");
        }
    }

    /// <summary>
    /// Прошлый запуск упал (<see cref="CrashReport.PreviousRunCrashed"/>, метка ставится один раз в
    /// <c>MainApplication.OnCreate</c>) — предложить отправить журнал, пока причина ещё свежа.
    /// Условие короткое и в одном месте намеренно: «упали» И настройка разрешает спрашивать
    /// (<see cref="DiagnosticsOptions.PromptShareAfterCrash"/>), выключенная — молчит навсегда, но
    /// кнопка «Передать» в настройках это не трогает.
    /// <para>
    /// Галочка «Больше не предлагать» пишет ту же настройку — <c>SettingsBinder.Set</c> по ключу
    /// каталога, тем же путём, что и правка со страницы настроек (план 29 §29.3): боевая область у
    /// глобальной настройки одна, значения кому и откуда ни пиши. Обе кнопки её уважают: и «не
    /// сейчас», и «отправить» — до, а не вместо самой отправки.
    /// </para>
    /// <para>
    /// «Отправить» ведёт в <see cref="DiagnosticsShare.Send"/> — тот же экран состава, что и кнопка
    /// в настройках, а не в голый <c>ACTION_SEND</c>: обещание «сначала покажем, что внутри» держит
    /// один код в одном месте.
    /// </para>
    /// </summary>
    private void OfferCrashShareIfNeeded()
    {
        if (!CrashReport.PreviousRunCrashed || !_diagnostics.PromptShareAfterCrash) return;

        var check = new CheckBox(this) { Text = AppStrings.CrashPromptDontAskAgain };
        int pad = this.Dp(24);
        check.SetPadding(pad, this.Dp(4), pad, 0);

        void SaveIfChecked()
        {
            if (check.Checked) _binder.Set(AppPage.PromptAfterCrashKey, "False");
        }

        _windows.Show(new AlertDialog.Builder(this)!
            .SetTitle(AppStrings.CrashPromptTitle)!
            .SetMessage(AppStrings.CrashPromptMessage)!
            .SetView(check)!
            .SetCancelable(false)!
            .SetPositiveButton(AppStrings.CrashPromptSend, (_, _) =>
            {
                SaveIfChecked();
                DiagnosticsShare.Send();
            })!
            .SetNegativeButton(AppStrings.CrashPromptDismiss, (_, _) => SaveIfChecked())!);
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
            // «Данных нет» — правда, но не вся: когда колесо не пустило, молчание объяснимо, и
            // человек иначе смотрит в жёлтую плашку без единой подсказки. Окна ввода нет намеренно
            // (решение владельца 08.08.2026) — вместо него причина и путь к настройке, где пароль
            // задаётся. Фазу по-прежнему выбирает ядро: здесь только текст внутри уже решённой им
            // ветки, иначе «нужен пароль» однажды легло бы поверх «переподключение».
            // Реплей исключён: писать в запись некуда, ответа не будет никогда, и дамп, где
            // slow-info не пришёл в первые секунды (обрезанный, снятый с середины поездки),
            // объявил бы виноватым пароль. Плеер работал до всей этой затеи — врать в нём нельзя.
            WheelLink.NoData when _session.AwaitingPassword && !_transport.IsReplay =>
                (LinkPhase.Connecting, AppStrings.StatePasswordNeeded),
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
        // Новое подключение — новый выезд, и следы на шкалах начинаются заново. Это шире смены
        // колеса (её чистит событие сессии): подключение к тому же колесу с этого экрана — тоже
        // новый выезд.
        _trace.Reset();

        // Имя анонса адаптер узнаёт в скане и подключении — то есть прямо сейчас. Это
        // единственный момент, когда его стоит спросить заново (WheelIdentity.Forget), и здесь он
        // о том же колесе: смену колеса, откуда бы она ни пришла, чистит подписка в CrashGuard.
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
    /// цикле (<see cref="MainScreenDriver"/>) и берёт то, что накопилось. Здесь только копится.
    /// </summary>
    private void Render(TelemetrySnapshot snapshot) => _trace.Push(snapshot);

    /// <summary>
    /// Колесо сменилось — <see cref="WheelSession.WheelChanged"/>. Экраны копят своё («на что
    /// колесо оказалось способно» у плиток), и накопленное про прежнее колесо к новому не
    /// относится (баг владельца 09.08.2026). Что именно забыть, решает каждый экран сам
    /// (<see cref="IMainScreen.WheelChanged"/>); хозяин лишь разносит весть — всем собранным, а не
    /// одному показанному: вернуться на плитки человек может и после смены колеса. Сам след поездки
    /// и имя колеса чистятся не здесь, а в <c>CrashGuard</c>: они переживают этот экран.
    /// <para>
    /// Через UI-поток: сессия поднимает событие на потоке подключавшегося и о потоке ничего не
    /// обещает, а сброс трогает разметку. Из главного потока это выполнится тут же, без кадра
    /// задержки.
    /// </para>
    /// </summary>
    private void OnWheelChanged(string? previous, string current) =>
        RunOnUiThread(() =>
        {
            foreach (var screen in _built.Values) screen.WheelChanged();
        });

    /// <summary>
    /// Погоня буксует (<see cref="WheelSession.ChaseTroubled"/>, план 11 §3.2). Спрашиваем дешёвые
    /// причины <b>молча</b> — <see cref="BleReadiness.FindProblem"/>, без диалогов: человек в этот
    /// момент едет, а не настраивает телефон.
    /// <para>
    /// Найденная причина уезжает в плашку связи — туда же, где живут все прочие слова о связи.
    /// А вот <b>останавливаем погоню только по доказанному</b>: выключенный адаптер — это факт,
    /// проверенный опросом, гнаться при нём не за чем. «Нет разрешения» причиной названо будет, но
    /// погоня остаётся: отозвать разрешение могли и на секунду, а ложно остановленная погоня — это
    /// колесо, которое не вернулось само, и худший из возможных новых дефектов.
    /// </para>
    /// </summary>
    private void OnChaseTroubled() => RunOnUiThread(() =>
    {
        // Реплей крутит записанный файл — ни адаптер, ни разрешения к нему отношения не имеют.
        if (_transport.IsReplay) return;

        if (BleReadiness.FindProblem() is not { } problem) return;

        _problem = problem.Cause;
        _problemDetail = problem.Message;
        _logger.LogWarning("Ble.ChaseTroubled {Cause}", problem.Cause);

        // Останавливаем только по доказанному: адаптер выключен, спрошен здесь же. Причина
        // BluetoothOff приходит и от выключенной локации на старых Android — там слова показываем,
        // а погоню оставляем.
        if (problem.Cause == LinkProblem.BluetoothOff && BleReadiness.IsAdapterOff())
        {
            StopChaseUntilAdapterReturns();
        }
    });

    /// <summary>
    /// Адаптер выключили — гнаться не за чем. Останавливаем погоню и <b>помним, что это сделали
    /// мы</b>: без этой памяти нет пути назад, а приложение, замолчавшее навсегда, хуже вечной
    /// погони.
    /// <para>
    /// «Оставь это колесо» (<c>SaveStoppedByRider</c>) здесь не ставится намеренно: райдер ничего
    /// не выбирал, выключился адаптер, — и следующий запуск обязан подключаться как обычно.
    /// </para>
    /// </summary>
    private async void StopChaseUntilAdapterReturns()
    {
        if (_chaseStoppedByAdapter) return;

        try
        {
            _chaseStoppedByAdapter = true;
            await _session.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ble.StopChaseFailed");
        }
    }

    /// <summary>
    /// Адаптер включили или выключили. Выключение — та же остановка, что и по опросу, только без
    /// ожидания трёх отказов. Включение — <b>путь назад</b>: возвращаемся к тому колесу, за которым
    /// гнались, а если возвращаться некуда (колесо не выбрано или райдер сам сказал «оставь») —
    /// снимаем слова о причине, и плашка снова становится обычным «Отключено», из которого поиск
    /// открывается касанием.
    /// </summary>
    private void OnAdapterStateChanged(bool on) => RunOnUiThread(() =>
    {
        if (_transport.IsReplay) return;

        if (!on)
        {
            _problem = LinkProblem.BluetoothOff;
            _problemDetail = AppStrings.BleBluetoothDisabled;
            StopChaseUntilAdapterReturns();
            return;
        }

        if (!_chaseStoppedByAdapter) return;

        _chaseStoppedByAdapter = false;
        _problem = LinkProblem.None;
        _problemDetail = "";

        if (_wheel.Address.Length == 0 || _wheel.StoppedByRider) return;

        _logger.LogInformation("Ble.AdapterBack {Mac}", _wheel.Address);
        _ = Connect(_wheel.Address);
    });

    private void OnBannerChanged() => RunOnUiThread(ShowWheelAlert);

    /// <summary>
    /// Полоса тревоги главного экрана. Слова считает <see cref="AlertBanner"/> — один на всё
    /// приложение, — а берётся отсюда только его «колёсная» половина: перегрузку и превышение панель
    /// показывает сама, и всплывающая над ней полоса сдвинула бы приборы вниз ровно тогда, когда
    /// цифрам надо стоять на месте. На прочих экранах приборов нет, и там полоса
    /// (<see cref="AlertOverlay"/>) говорит обо всём.
    /// </summary>
    private void ShowWheelAlert()
    {
        if (_banner.WheelText is { Length: > 0 } text) _alertStrip.Show(text, AlertStrip.Danger);
        else _alertStrip.Hide();
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

            // «Отключить» значит «оставь это колесо», а не «прекрати сейчас» (план 24 §Б3). Признак
            // ставится здесь, а не в Disconnect(): тот же метод останавливает и реплей, а запись
            // дампа никакого колеса не выбирает и отказаться от него не может.
            _userSettings.SaveStoppedByRider();
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
    // ---- Разделы шторки (план 32 §1, этап 4) --------------------------------------------------
    //
    // Порода, а не частота: рука идёт к месту, глаз — к разделу из двух-трёх, а не к ряду из семи.
    // Шторка о значении этих слов не знает: ключ говорит ей, где раздел сменился, а имя раздела на
    // боковом корешке она спрашивает у нас (SectionLabel). Соседи по разделу стоят в списке подряд
    // — порядок списка и есть порядок строк.
    private const string WheelNow = "wheel";
    private const string Ride = "ride";
    private const string Phone = "phone";

    /// <summary>
    /// Имя раздела для корешка шторки. Пятый раздел, «Экран», шторка собирает сама из корешков
    /// экранов, шестого не бывает: неизвестный ключ — это забытое слово, и пустой корешок скажет об
    /// этом громче, чем ключ, показанный райдеру.
    /// </summary>
    private static string SectionLabel(string group) => group switch
    {
        WheelNow => AppStrings.SheetSectionWheel,
        Ride => AppStrings.SheetSectionRide,
        Phone => AppStrings.SheetSectionPhone,
        _ => "",
    };

    /// <summary>
    /// Оперативные команды — те, после которых человек продолжает ехать (план 25 §2, шаг 2:
    /// «Данные», «Поездки» и «Настройки» ушли отсюда в раздел переходов). Порядок и состав
    /// закреплены: он и есть та точка отсчёта, от которой считается правило «позиции фиксированы
    /// навсегда» (quick-commands-design.md §3), и держит его тест <c>QuickSheetLayoutTests</c> —
    /// перестановка роняет сборку, а не всплывает жалобой.
    /// <para>
    /// Порядок здесь же задаёт и разделы (план 32 §1, этап 4): колесо · поездка · телефон, каждый
    /// своей строкой. Булавку шторка приписывает сама, в раздел «телефон» — <c>PinGroup</c>.
    /// </para>
    /// </summary>
    private IReadOnlyList<QuickSheetCommand> BuildWheelCommands()
    {
        var commands = new List<QuickSheetCommand>
        {
            new()
            {
                Icon = QuickIcons.Light,
                Group = WheelNow,
                Label = () => _wheelConfig.LightEnabled ? AppStrings.ButtonLightOn : AppStrings.ButtonLight,
                IsOn = () => _wheelConfig.LightEnabled,
                Action = LightAsync,
            },
            new()
            {
                Icon = QuickIcons.Beep,
                Group = WheelNow,
                Label = () => AppStrings.ButtonBeep,
                Action = BeepAsync,
            },

            // Подключение и отключение переехали сюда из полосы состояния вместе с ней (прогон 5).
            // Одна команда на оба действия, а не две: они взаимоисключающие, и подпись говорит, что
            // случится сейчас. Поведение прежнее — поиск колеса, если не подключены, и подтверждение
            // на ходу, если едем. Раздел «колесо»: связь с колесом — про колесо (план 32 §1, этап 4).
            new()
            {
                Icon = QuickIcons.Power,
                Group = WheelNow,
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
                Icon = QuickIcons.Record,
                Group = Ride,
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
                Icon = QuickIcons.Reset,
                Group = Ride,
                Label = () => AppStrings.ButtonResetPeaks,
                IsEnabled = () => _trace.HasData,
                Action = ResetPeaksAsync,
            },
        };

        // Реплей не запускается сам: на телефоне это внезапная тревога в полный голос, и не факт,
        // что рядом окажется, чем её выключить. Кнопка есть только у отладочного транспорта — на
        // колесе она не появится ни при каких обстоятельствах, а не просто спрятана.
        //
        // Встаёт он в середину списка, а не в конец: раздел — это подряд идущие соседи, и
        // приписанный за телефонными командами реплей завёл бы вторую строку «Поездка» под ними.
        if (_transport.IsReplay)
        {
            commands.Add(new QuickSheetCommand
            {
                Icon = QuickIcons.Play,
                Group = Ride,
                Label = () => _session.CurrentState == ConnectionState.Disconnected
                    ? AppStrings.ReplayStart
                    : AppStrings.ReplayStop,
                Action = ReplayToggleAsync,
            });
        }

        commands.AddRange(
        [
            new()
            {
                // Экран, который не гаснет, нужен на разбор у обочины и на долгий светофор — то
                // есть здесь, рядом с остальными «сейчас», а не в настройках через три экрана.
                // Гаснет он от кнопки питания как обычно; флаг отменяет только таймаут.
                Icon = QuickIcons.Sun,
                Group = Phone,
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
                Icon = QuickIcons.Lock,
                Group = Phone,
                Label = () => _screenOptions.ShowOverLock ? AppStrings.ButtonLockScreenOn : AppStrings.ButtonLockScreen,
                IsOn = () => _screenOptions.ShowOverLock,
                Action = () =>
                {
                    _screenOptions.ShowOverLock = !_screenOptions.ShowOverLock;
                    return Task.CompletedTask;
                },
            },
        ]);

        return commands;
    }

    /// <summary>
    /// Переходы на другие экраны — раздел «Перейти», последний в шторке (план 32 §1, этап 4).
    /// Оперативной командой переход не является: после него человек уже не едет, и кнопки у них
    /// свои — узкие, со значком и словом в строку.
    /// <para>
    /// Значок «Данных» — столбики, а корешку «Панель» досталась шкала со стрелкой: прежний «📊»
    /// стоял на обоих, и один знак на два разных дела был готовым объяснением жалобы «какая за что
    /// — непонятно» (план 25 §1). Уникальность знаков в шторке держит тест.
    /// </para>
    /// </summary>
    private IReadOnlyList<QuickSheetLink> BuildScreenLinks() =>
    [
        new()
        {
            Icon = QuickIcons.Data,
            Label = AppStrings.ButtonData,
            Open = () => OpenScreen(typeof(TelemetryActivity)),
        },
        new()
        {
            // Ведёт в RidesActivity, а не в RecordingActivity: та отвечает на вопрос «что пишется
            // прямо сейчас», и попасть на неё, нажав «Поездки», — не то, зачем сюда шли.
            Icon = QuickIcons.Rides,
            Label = AppStrings.ButtonRides,
            Open = () => OpenScreen(typeof(RidesActivity)),
        },
        new()
        {
            // Через OpenScreen, как и все выходы с главного экрана: настройки поверх замка не
            // показываются, сперва разблокировка.
            Icon = QuickIcons.Settings,
            Label = AppStrings.ButtonSettings,
            Open = () => OpenScreen(typeof(SettingsActivity)),
        },
    ];

    private Task LightAsync() => _session.SendCommand(new WheelCommand.SetLight(!_wheelConfig.LightEnabled));

    private Task BeepAsync() => _session.SendCommand(new WheelCommand.Beep());

    private Task RecordToggleAsync()
    {
        _recorder.Toggle();
        return Task.CompletedTask;
    }

    /// <summary>
    /// «Сброс максимумов» (design doc §3) — обнуляет пиковые показания, не журнал поездки.
    /// <para>
    /// Ею же обнуляется счётчик поездки в центре (решение владельца 12.08.2026): своей кнопки он не
    /// получил — «сброс» на экране один, и человек, нажавший его, ждёт, что обнулится всё, что копилось.
    /// Одометр молчит — точку не двигаем: сдвинуть её на ноль значило бы показать весь одометр как
    /// путь этой поездки, а это хуже, чем не сбросить.
    /// </para>
    /// </summary>
    private Task ResetPeaksAsync()
    {
        _session.ResetPeaks();
        _trace.ResetPeaks();

        if (_session.LastSnapshot is { TotalDistanceKm: > 0 } snapshot && _layers.Scope is { Length: > 0 } wheel)
        {
            _tripPoints.Reset(wheel, TripPoints.Centre, snapshot.TotalDistanceKm);
        }

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
    {
        // Отказ приходит событием сессии, а не нажатием: пока весть шла до потока интерфейса, экран
        // мог начать закрываться, и окно на уходящей активности — это уже не показ, а BadToken.
        if (IsFinishing || IsDestroyed) return;

        _windows.Show(new AlertDialog.Builder(this)!
            .SetTitle(AppStrings.WheelRefusedTitle)!
            .SetMessage(reason)!
            .SetPositiveButton(AppStrings.Ok, (_, _) => { })!);
    });

    /// <summary>Подтверждение через системный диалог — аналог <c>DisplayAlertAsync</c> MAUI.</summary>
    private Task<bool> ConfirmAsync(string title, string message, string positive, string negative)
    {
        var tcs = new TaskCompletionSource<bool>();
        var dialog = _windows.Show(new AlertDialog.Builder(this)!
            .SetTitle(title)!
            .SetMessage(message)!
            .SetCancelable(false)!
            .SetPositiveButton(positive, (_, _) => tcs.TrySetResult(true))!
            .SetNegativeButton(negative, (_, _) => tcs.TrySetResult(false))!);

        // Окно ушло само (экран закрылся, хозяин прибрал) — ответ «нет». Иначе ожидающая задача не
        // кончается никогда и держит собой экран со всем, что за ним: вопрос без ответа хуже отказа.
        dialog.DismissEvent += (_, _) => tcs.TrySetResult(false);

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
        // Тот экран, на котором человек ушёл в прошлый раз. Умолчание — первый в реестре (панель):
        // с неё начинали все, кто ни разу не трогал корешки. Неизвестный id — тоже он.
        _screenChoice = _screens.Find(_layers.Get(_layers.Scope, ScreenChoiceKey).Value ?? "").Id;
        _screen = new MainScreenView(this, _dashboardOptions, Screen(_screenChoice));

        // Полосы тревоги рамки — тот же источник, что у наложения прочих экранов: сила приходит из
        // общего потока тревог, вторых вычислителей нет.
        _screen.Bars.Alert = () => _alert;

        _alertStrip = _screen.Alert;
        _alertStrip.SetTypeface(_bold, TypefaceStyle.Bold);

        _sheet = _screen.Sheet;
        _sheet.PinLabel = () => AppStrings.ButtonPin;
        // Булавка — про то, как телефон ведёт себя эту поездку, поэтому стоит с телефонными
        // командами, а не отдельной строкой ради одной кнопки (макет 3a, раздел «Телефон»).
        _sheet.PinGroup = Phone;
        _sheet.SectionLabel = SectionLabel;
        _sheet.ScreensSectionLabel = () => AppStrings.SheetSectionScreen;
        _sheet.LinksSectionLabel = () => AppStrings.SheetSectionGo;
        _sheet.SetCommands(BuildWheelCommands());
        _sheet.SetScreens(BuildScreenTabs());
        _sheet.SetLinks(BuildScreenLinks());

        _driver.Attach(_screen.Current, BuildFrame);
        _driver.Refresh();

        return _screen;
    }

    /// <summary>
    /// Корешки экранов над рядом команд (план 23 §2.2). Это не команды: правило «позиции команд
    /// фиксированы навсегда» их не касается, и полоса у них своя.
    /// <para>
    /// Список — из реестра (план 17 §3): экраны здесь больше не перечислены поимённо, и шестой
    /// корешок появится от одной записи в реестре, а не от правок в четырёх местах.
    /// </para>
    /// </summary>
    private IReadOnlyList<QuickSheetScreen> BuildScreenTabs() =>
    [
        .. _screens.Screens.Select(entry => new QuickSheetScreen
        {
            Icon = entry.Icon,
            Label = entry.Label(),
            IsSelected = () => _screenChoice == entry.Id,
            Select = () => ChooseScreen(entry.Id),
        }),
    ];

    /// <summary>Выбор человека: показать и запомнить. Общий слой, без «этого колеса» — план 23 §2.3.</summary>
    private void ChooseScreen(string choice)
    {
        if (_screenChoice == choice) return;

        ShowScreen(choice);
        _layers.Set(_layers.Scope, ScreenChoiceKey, choice, SettingLayer.GlobalOnly);
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
        _screen.Show(Screen(choice));
        _driver.Attach(_screen.Current, BuildFrame);
        _driver.Refresh();
    }

    /// <summary>
    /// Экран по идентификатору — собранный однажды и оставленный: пересборка на каждом переключении
    /// стоила бы плиткам их раскладки и графиков. Собирает его фабрика реестра, а здесь остаётся
    /// проводка намерений — без неё экран нем: галочка шторки и плашка связи звали бы в пустоту.
    /// </summary>
    private IMainScreen Screen(string id)
    {
        if (_built.TryGetValue(id, out var known)) return known;

        var screen = _screens.Find(id).Create(this);
        screen.OnIntent = OnScreenIntent;
        _built[id] = screen;

        if (id == MainScreenRegistry.PanelId) _panelVariantShown = _panels.CurrentId;
        return screen;
    }

    /// <summary>
    /// Вариант панели могли сменить в настройках, пока экран стоял (план 17 §3). Пересобираем на
    /// месте — того же экрана, но другим вариантом; смотрит человек на плитки — панель просто
    /// выбрасывается из собранных и родится следующей уже новой.
    /// </summary>
    private void ApplyPanelVariant()
    {
        if (_panelVariantShown == _panels.CurrentId) return;

        _built.Remove(MainScreenRegistry.PanelId);
        if (_screenChoice == MainScreenRegistry.PanelId) ShowScreen(MainScreenRegistry.PanelId);
    }

    private void LoadFonts()
    {
        // Resources.GetFont — платформенный API 26+ для файлов из Resources/font; AndroidX здесь не
        // нужен (SupportedOSPlatformVersion уже 28.0).
        _regular = Resources!.GetFont(Resource.Font.opensans_regular) ?? Typeface.Default!;
        _bold = Typeface.Create(_regular, TypefaceStyle.Bold) ?? Typeface.DefaultBold!;
    }


}
