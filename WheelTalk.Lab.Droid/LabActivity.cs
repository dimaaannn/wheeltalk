using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Core.Alerts;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Layouts;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Dashboard.Droid.Screen.Tiles;
using WheelTalk.Dashboard.Droid.Widgets;
using WheelTalk.Lab.Data;
using WheelTalk.Lab.Droid.Scenarios;
using WheelTalk.Lab.Droid.Ui;

namespace WheelTalk.Lab.Droid;

/// <summary>
/// Стенд: сверху выбор варианта и сценария, посередине сама панель, снизу транспорт с метками.
/// Два режима работы, ради которых он и сделан, — статичная картинка (встать на метку и снять) и
/// движение (проиграть кусок целиком). Хром прячется, потому что на снимке должна остаться панель,
/// а не стенд.
/// <para>
/// Портировано с <c>WheelTalk.Lab/Pages/LabPage.xaml(.cs)</c>. Поведение и порядок действий те же,
/// разметка собирается кодом, как и весь остальной нативный каркас. Три отличия, и все три —
/// следствие платформы, а не решения:
/// </para>
/// <list type="number">
/// <item>Кадры гонит <c>PostOnAnimation</c>, а не таймер на 33 мс: панель рисует себя по vsync сама
/// (<c>DashboardView.OnDraw</c>), и второй источник частоты дал бы биение с первым. Настройка
/// «снять потолок в 30 кадров» вместе с таймером исчезла — потолка больше нет.</item>
/// <item>Касания ловит сам контейнер панели. В MAUI для этого лежал отдельный прозрачный слой:
/// жест, повешенный на элемент, содержимое которого обработчик и заменяет, через несколько смен
/// варианта переставал срабатывать. У <c>ViewGroup</c> такой болезни нет.</item>
/// <item>Тревожные полосы — общий элемент <see cref="AlertBarsView"/>, а не своя канва стенда:
/// в режиме «экран целиком» их носит рамка (<see cref="MainScreenView.Bars"/>), в вариант-режиме
/// стенд кладёт тот же элемент поверх хоста — ровно то, что видит райдер.</item>
/// </list>
/// <para>
/// Кадр панели ведёт общий с приложением <see cref="MainScreenDriver"/> (план 19 Б2): состояние
/// кадра считает <see cref="BuildFrame"/> (ручки стенда + <see cref="LinkCycle"/>), а продвижение
/// позиции записи, обновление ползунка/подписи и счётчика кадров — стендовые дела, они живут в
/// <see cref="BeforeFrame"/>, хуке водителя. Стенд пересоздаёт панель при смене варианта или
/// экрана (<see cref="ShowVariant"/>/<see cref="ShowScreen"/>) — там же водитель переставляется на
/// новую панель методом <c>Attach</c>, без остановки цикла.
/// </para>
/// </summary>
// Имя компонента задано явно, как у боевой MainActivity: иначе Android собирает имя Java-класса из
// crc64-хеша пространства имён, и `am start -n …` отвалился бы от переименования папки. Имя —
// контракт командного входа (см. HandleCommand), а не деталь сборки.
[Activity(Name = "com.wheeltalk.lab.droid.LabActivity",
    Label = "WheelTalk Lab", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class LabActivity : Activity
{
    private static readonly double[] Rates = [0.25, 0.5, 1, 2];

    /// <summary>Ползунок ходит в десятых долях секунды: у него целочисленная шкала.</summary>
    private const int TicksPerSecond = 10;

    private readonly LabSettings _settings = LabSettings.Current;

    private ScenarioCatalog.Scenario _scenario = ScenarioCatalog.All[0];
    private DashboardCatalog.Variant _variant = DashboardCatalog.All[0];
    private ReadingSource? _source;
    private DashboardView? _dashboard;

    /// <summary>
    /// Режим «экран целиком»: не вариант панели, а весь главный экран приложения тем же классом
    /// (<see cref="MainScreenView"/>) — полоса тревоги, панель, шторка. В пикере вариантов он
    /// первый пункт; пока он показан, здесь не null, и <see cref="_dashboard"/> — его панель.
    /// </summary>
    private MainScreenView? _screen;

    /// <summary>
    /// Полосы тревоги вариант-режима: рамки там нет, а полосы правят глазами именно на стенде —
    /// потому поверх хоста стоит тот же элемент, что носит рамка. Живёт только пока показан вариант.
    /// </summary>
    private AlertBarsView? _hostBars;

    private GestureDetector? _sheetGesture;
    private GestureDetector? _tapGesture;
    private bool _lightOn;

    /// <summary>Стендовые состояния двух телефонных команд: щёлкают только сами себя — гасить и запирать стенду нечего.</summary>
    private bool _keepOn;

    private bool _overLock;

    /// <summary>База стенда с придуманной историей. Открывается в фоне: файл, миграции и набивка.</summary>
    private LabStore? _store;

    /// <summary>Раскладка плиток стенда — файлом: слоёв настроек у стенда нет, а перезапуск она пережить обязана.</summary>
    private readonly LabTileLayoutFile _layoutFile = new();

    /// <summary>
    /// Экран плиток в рамке — тот же класс, что показывает райдеру приложение. Живёт ровно столько,
    /// сколько сама рамка: смена варианта панели пересобирает её, и плитка со старым родителем в
    /// новую не встанет.
    /// </summary>
    private TilesScreen? _tiles;

    /// <summary>Какой корешок выбран в полосе шторки: плитки или панель.</summary>
    private bool _tilesShown;

    /// <summary>Через сколько мс писать очередной отсчёт: пять герц, как у боевой записи.</summary>
    private static readonly long TelemetryPeriodMs = (long)(1000 / LabRideHistory.Hz);

    private long _wroteAt;
    private TimeSpan _position;
    private double _rate = 1;
    private long _lastTick;
    private bool _playing = true;
    private bool _seeking;
    private string _positionText = "";

    private MainScreenDriver _driver = null!;
    private readonly LinkCycle _linkCycle = new();
    private readonly ShotWalk _walk = new();

    private float _density;
    private LinearLayout _chrome = null!;
    private LinearLayout _transport = null!;
    private FrameLayout _host = null!;
    private FpsOverlayView _fps = null!;
    private Spinner _variantPicker = null!;
    private Spinner _scenarioPicker = null!;
    private Spinner _ratePicker = null!;
    private SeekBar _positionSlider = null!;
    private Button _playButton = null!;
    private TextView _positionLabel = null!;
    private LinearLayout _markBar = null!;
    private TextView _statusLabel = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _density = Resources!.DisplayMetrics!.Density;

        var root = BuildLayout();
        SetContentView(root);

        // Та же механика окна, что у приложения (общая, из библиотеки панели): окно занимает экран
        // целиком, фон уходит под системную строку.
        //
        // Верхний инсет достаётся не корню, а ряду органов управления: под часами им делать нечего,
        // а панели — есть. Тогда в режиме «экран целиком», где хром спрятан, панель начинается от
        // самой кромки и уходит под бар ровно как в приложении. Отдать инсет корню значило бы
        // оставить над панелью чёрную полосу и в этом режиме — так и было до 01.08.2026.
        int chromeTop = _chrome.PaddingTop;
        EdgeToEdge.Apply(this, root, top =>
        {
            _barInset = top;
            _chrome.SetPadding(_chrome.PaddingLeft, chromeTop + top, _chrome.PaddingRight, _chrome.PaddingBottom);
        });

        _settings.Changed += OnSettingsChanged;

        _variantPicker.SetSelection(0);
        _ratePicker.SetSelection(Array.IndexOf(Rates, 1));
        _scenarioPicker.SetSelection(0);

        _lastTick = System.Environment.TickCount64;
        _driver = new MainScreenDriver(BeforeFrame);

        // Стенд открывается «экраном целиком»: править собираются главный экран, значит его и
        // показываем первым. Отдельные варианты панели — следующие пункты того же пикера.
        ShowScreen();
        _ = LoadScenario();
        _ = OpenStore();

        // HOTRELOAD: правка, применённая из Visual Studio, пересобирает экран сама — см. LabHotReload.
        LabHotReload.Rebuild = () => RunOnUiThread(Rebuild);

        // Стенд подняли самой командой — тогда extras лежат в стартовом Intent, и OnNewIntent не
        // будет вовсе.
        HandleCommand(Intent);
    }

    protected override void OnDestroy()
    {
        LabHotReload.Rebuild = null;
        _settings.Changed -= OnSettingsChanged;

        // База переживает экран ровно до этого места: держать соединение открытым после ухода
        // стенда незачем, а ждать закрытия — тем более.
        var store = _store;
        _store = null;
        if (store is not null) _ = store.DisposeAsync().AsTask();

        base.OnDestroy();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);

        // SetIntent не зовём по той же причине, что MainActivity: подменённый Intent пережил бы
        // пересоздание Activity и повторил бы команду при восстановлении экрана.
        HandleCommand(intent);
    }

    /// <summary>
    /// Командный вход стенда — тот же приём, которым гоняют реплей боевого приложения (план 22 §2):
    /// <c>am start -n com.wheeltalk.lab.droid/.LabActivity --es rebuild screen</c>,
    /// <c>--es screen panel|tiles</c>, <c>--es history new</c>, <c>--es layout reset</c>,
    /// <c>--es alarm on|off</c> (полоса тревоги — тот же тумблер, что на странице ручек).
    /// <para>
    /// Заведён затем, что <c>input tap</c> по координатам промахивается хронически, а каждый промах —
    /// потерянный прогон. Своей логики здесь нет: команда зовёт ровно то же, что кнопка.
    /// </para>
    /// </summary>
    private void HandleCommand(Intent? intent)
    {
        if (intent is null) return;

        // HOTRELOAD: пересобрать экран, не трогая телефон руками.
        if (intent.GetStringExtra("rebuild") is not null) Rebuild();
        if (intent.GetStringExtra("screen") is { } screen) ShowTiles(screen == "tiles");
        if (intent.GetStringExtra("history") is not null) _ = RefillStore();
        if (intent.GetStringExtra("alarm") is { } alarm)
        {
            _settings.WheelAlarm = alarm != "off";
            _settings.Notify();
        }

        // Переставленная руками раскладка лежит в файле и закрывает собой зашитую: правку
        // TilesLayout.Fixed без сброса на экране не увидеть.
        if (intent.GetStringExtra("layout") is not null)
        {
            _layoutFile.Reset();
            Rebuild();
        }
    }

    /// <summary>
    /// HOTRELOAD: пересборка показанного экрана на месте. Горячая перезагрузка подменяет тела
    /// методов, но собранную иерархию <c>View</c> не перестроит — числа раскладки прочитаны в
    /// конструкторе плитки и там же остались. Пересобираем то, что показано: рамку с экраном либо
    /// отдельный вариант панели.
    /// </summary>
    private void Rebuild()
    {
        if (_screen is null) ShowVariant(_variant);
        else ShowScreen();
    }

    protected override void OnStart()
    {
        base.OnStart();

        // Экран стенда гасить нельзя: половина сценариев — «смотреть, как оно едет».
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);

        // Сброс, а не только на OnCreate: без него после долгого пребывания в фоне первый кадр
        // после возврата увидел бы огромный elapsed и прыгнул бы позицией записи вперёд.
        _lastTick = System.Environment.TickCount64;
        _driver.Start();
    }

    protected override void OnStop()
    {
        _driver.Stop();
        Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
        base.OnStop();
    }

    /// <summary>
    /// Свайп вверх от нижней кромки открывает шторку — тот же жест и тот же слушатель из
    /// библиотеки, что в приложении. Кормится только в режиме «экран целиком» и только пока
    /// шторка закрыта — по той же причине, что в <c>MainActivity</c>: зона свайпа совпадает с
    /// полосой её собственных кнопок.
    /// </summary>
    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        if (ev is not null && _screen is { } screen && !screen.Sheet.IsOpen)
        {
            // 96 dp — как в MainActivity (владелец 04.08.2026, было 64): стенд открывает шторку
            // тем же порогом, что и приложение, а не своей копией.
            _sheetGesture ??= new GestureDetector(this, new SwipeUpFromEdgeListener(
                () => _screen?.Sheet.Toggle(), Resources!.DisplayMetrics!.HeightPixels, Dp(128)));
            _sheetGesture.OnTouchEvent(ev);

            // Тап по экрану — тем же слушателем из библиотеки, что в приложении. Без него на стенде
            // молчали все попадания разом: галочка шторки, точка записи, плашка связи. Правят вид
            // здесь, и «не отзывается» читалось бы как «сломал правкой».
            _tapGesture ??= new GestureDetector(this, new SingleTapListener(OnTapped));
            _tapGesture.OnTouchEvent(ev);
        }
        return base.DispatchTouchEvent(ev);
    }

    /// <summary>
    /// Жест ловит хозяин, а во что касание попало — решает сам экран: метки нарисованы на его канве.
    /// То же разделение, что в <c>MainActivity</c>.
    /// <para>
    /// Хром отсюда не зовётся: на экране плиток касание занято правкой раскладки, и возврат стенда
    /// по любому тапу отменял бы кнопку «⛶» сразу после нажатия. Там стенд зовут Escape, на панели
    /// по-прежнему кликом по хосту.
    /// </para>
    /// </summary>
    private void OnTapped(float x, float y) => _screen?.Current.Tap(x, y);

    /// <summary>
    /// «Назад» на стенде разбирается по старшинству: сперва её предлагают экрану — плитки закрывают
    /// ею режим правки; затем она возвращает спрятанный хром и только потом закрывает стенд.
    /// <para>
    /// Хром здесь потому, что эмулятор шлёт <c>Esc</c> с клавиатуры как «назад», а не как
    /// <see cref="Keycode.Escape"/>: без этого клавиша, которой стенд зовут, выкидывала бы из него.
    /// </para>
    /// </summary>
    public override void OnBackPressed()
    {
        if (_screen?.Current.Back() == true) return;

        if (_chrome.Visibility != ViewStates.Visible)
        {
            ShowChrome(true);
            return;
        }

        base.OnBackPressed();
    }

    /// <summary>
    /// Escape возвращает хром стенда. Нужен затем, что в режиме «экран целиком» стенд зовут
    /// касанием, а касание на экране плиток занято правкой раскладки: клавиша снаружи —
    /// с клавиатуры или <c>adb shell input keyevent 111</c> — ничему не мешает.
    /// <para>
    /// Перехват здесь, а не в <c>OnKeyDown</c>: до него событие не доходит, если клавишу взял себе
    /// элемент в фокусе — сетка плиток, список или поле. Хром должен зваться отовсюду, где показан
    /// стенд.
    /// </para>
    /// </summary>
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is { KeyCode: Keycode.Escape, Action: KeyEventActions.Down })
        {
            ShowChrome(true);
            return true;
        }

        return base.DispatchKeyEvent(e);
    }

    /// <summary>
    /// Исполнение намерений экрана — по-стендовому. Действия здесь свои, но <b>немых намерений
    /// нет</b>: на каждое видно, что экран услышан, иначе правящий вид решит, что сломал его правкой.
    /// </summary>
    private void OnScreenIntent(MainScreenIntent intent)
    {
        switch (intent)
        {
            // Связи у стенда нет — вместо неё перебор состояний плашки, тот же, что у кнопки «⇄».
            case MainScreenIntent.ShowConnection: CycleLink(); break;
            // Экрана записи нет — щёлкаем саму точку записи, как это делает команда шторки.
            case MainScreenIntent.ShowRecording: _settings.Recording = !_settings.Recording; break;
            case MainScreenIntent.ShowSheet: _screen?.Sheet.Toggle(); break;
        }
    }

    /// <summary>
    /// Хук <see cref="MainScreenDriver"/>: что живёт на кадре стенда, но не про панель. Продвижение
    /// позиции записи по прошедшему времени, полоса тревоги колеса (часть экрана, не панели),
    /// счётчик кадров и синхронизация ползунка/подписи с текущей позицией — стендовые дела, водителю
    /// они не принадлежат.
    /// </summary>
    private void BeforeFrame()
    {
        long now = System.Environment.TickCount64;
        var elapsed = TimeSpan.FromMilliseconds(now - _lastTick);
        _lastTick = now;

        if (_playing && _source is { } source)
        {
            _position += elapsed * _rate;
            // Запись кончилась — начинаем сначала: короткого куска должно хватать надолго.
            if (_position > source.Timeline.Duration) _position = TimeSpan.Zero;

            WriteTelemetry(source, now);
        }

        // Пинок полосам тревоги — тем же порядком, что MainActivity.BeforeFrame: силу они
        // спрашивают сами (BarsAlert), а из тишины их будит кадр стенда.
        (_screen?.Bars ?? _hostBars)?.Invalidate();

        if (_source is not { } current || _dashboard is null) return;

        // Полоса тревоги колеса — часть рамки экрана, а не панели: есть только в режиме «экран
        // целиком». Инсет ей — тем же путём и под тем же тумблером, что панели (см. BuildFrame):
        // «Панель под системной строкой» решает, стоит ли экран стенда под реальным баром прямо
        // сейчас (план 22 §1).
        if (_screen is { } screen)
        {
            if (_settings.WheelAlarm) screen.Alert.Show("Тревога колеса", AlertStrip.Danger);
            else screen.Alert.Hide();
            screen.Alert.TopInset = _settings.UnderSystemBar ? _barInset : 0;
        }

        _fps.PanelDrawMs = _dashboard.LastDrawMs;

        if (!_seeking)
        {
            _positionSlider.Progress = (int)(_position.TotalSeconds * TicksPerSecond);
        }

        // Правка свойств разметки только при изменении: подпись меняется десять раз в секунду, а
        // кадров вшестеро больше.
        string text = $"{_position.TotalSeconds,6:F1} / {current.Timeline.Duration.TotalSeconds:F0} с";
        if (_positionText != text)
        {
            _positionText = text;
            _positionLabel.Text = text;
        }
    }

    /// <summary>
    /// Состояние кадра — то же самое, что считает приложение: показания (снимок сценария в текущей
    /// позиции), плашка связи, имя колеса, точка записи и подсказка про шторку. Связи у стенда нет и
    /// быть не может — состояние крутит <see cref="_linkCycle"/> кнопкой «⇄», проверяя ровно то же,
    /// что рисует библиотека приложению.
    /// </summary>
    /// <summary>
    /// Проигрываемый сценарий пишется в базу стенда — иначе история кончается там, где кончилась
    /// набивка, и график за пять минут пустеет на глазах: окно едет вперёд, а данных за ним нет.
    /// <para>
    /// Пять раз в секунду, а не на кадре: столько же пишет боевое приложение
    /// (<see cref="LabRideHistory.Hz"/>), а кадров идёт шестьдесят. На паузе не пишем вовсе — в бою
    /// молчащее колесо тоже не повторяет последний отсчёт.
    /// </para>
    /// </summary>
    private void WriteTelemetry(ReadingSource source, long now)
    {
        if (now - _wroteAt < TelemetryPeriodMs || _store is not { } store) return;
        if (source.SnapshotAt(_position) is not { } snapshot) return;

        _wroteAt = now;
        store.Write(snapshot);
    }

    /// <summary>
    /// Сила тревоги для полос — из показаний текущей позиции, то же число, что панель раньше брала
    /// из кадра (<c>Reading.AlertIntensity</c>). Мягкой тревоги по скорости у стенда нет — ручки
    /// такой нет и не было.
    /// </summary>
    private AlertState BarsAlert() => new(
        _source?.At(_position) is { } reading ? reading.AlertIntensity : 0, SpeedExceeded: false);

    private MainScreenFrame BuildFrame()
    {
        var link = _linkCycle.Current;
        return new MainScreenFrame
        {
            Reading = _source?.At(_position),
            // Живой снимок — для плиток: они показывают то, что сказало колесо, панель — то, что
            // из этого посчитано.
            Snapshot = _source?.SnapshotAt(_position),
            LinkPhase = link.Phase,
            LinkText = link.Text,
            LinkSeconds = link.Phase == LinkPhase.Connecting
                ? _linkCycle.SecondsSince(System.Environment.TickCount64)
                : 0,
            WheelName = _settings.WheelName,
            Recording = _settings.Recording,
            ShowRecordDot = true,
            ShowSheetHint = _settings.ShowSheetHint && !(_screen?.Sheet.IsOpen ?? false),
            IsStale = _settings.Stale,
            // То же, что MainActivity: панель рисует под системной строкой и знает её высоту.
            // Без этого стенд молча не показывал тень под значками и затухание разметки лент —
            // они считаются от инсета, а у стенда он был нулевым (найдено сверкой 01.08.2026).
            TopInset = _settings.UnderSystemBar ? _barInset : 0,
        };
    }

    private async Task LoadScenario()
    {
        _statusLabel.Text = $"{_scenario.Title}: читаю…";

        var timeline = _settings.Tweaks.Apply(await ScenarioCatalog.LoadAsync(_scenario));
        _source = new ReadingSource(timeline, _settings.Options);
        _position = TimeSpan.Zero;

        _positionSlider.Max = Math.Max(1, (int)(timeline.Duration.TotalSeconds * TicksPerSecond));
        _positionSlider.Progress = 0;
        BuildMarks(timeline);

        _statusLabel.Text = $"{timeline.Subtitle} · {timeline.Frames.Count} кадров · {_settings.Tweaks.Describe()}";
        _driver.Refresh();
    }

    /// <summary>
    /// База стенда с историей на несколько часов: графикам плиток нужно, из чего строиться, а на
    /// свежей установке база пуста. Открытие, миграции и набивка — в фоне: это десятки тысяч строк,
    /// и на UI-потоке им делать нечего.
    /// </summary>
    private async Task OpenStore()
    {
        try
        {
            var store = new LabStore();
            _statusLabel.Text = "История: открываю базу…";
            string summary = await store.OpenAsync();

            _store = store;
            _statusLabel.Text = summary;

            // База открывается позже, чем строится экран, а читателя он берёт при рождении: пересобрать
            // плитки — единственный способ отдать им историю, не заводя ради этого отложенной ссылки.
            _tiles = null;
            if (_tilesShown) ShowTiles(true);
        }
        catch (Exception ex)
        {
            // Стенд без истории всё ещё стенд: панель и плитки живут на записи, а не на базе.
            _statusLabel.Text = $"История недоступна: {ex.Message}";
        }
    }

    /// <summary>Набить другую покатушку — по команде <c>--es history new</c>.</summary>
    private async Task RefillStore()
    {
        if (_store is not { } store) return;

        try
        {
            _statusLabel.Text = "История: набиваю заново…";
            _statusLabel.Text = await store.RefillAsync();
        }
        catch (Exception ex)
        {
            // Молча упавшая набивка выглядит как набивка удавшаяся: строка состояния — единственное
            // место, где стенд может сказать правду про базу.
            _statusLabel.Text = $"История не набилась: {ex.Message}";
        }
    }

    private void BuildMarks(Timeline timeline)
    {
        _markBar.RemoveAllViews();
        foreach (var mark in timeline.Marks)
        {
            var button = new Button(this) { Text = mark.Name };
            button.SetTextSize(ComplexUnitType.Sp, 12);
            button.SetAllCaps(false);
            var at = mark.At;
            button.Click += (_, _) => SeekTo(at);
            _markBar.AddView(button, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, Dp(38)) { RightMargin = Dp(4) });
        }
    }

    /// <summary>Встать на точку и замереть: статичная картинка — половина всех сценариев.</summary>
    private void SeekTo(TimeSpan at)
    {
        _position = at;
        SetPlaying(false);
        _driver.Refresh();
    }

    private void ShowVariant(DashboardCatalog.Variant variant)
    {
        _variant = variant;

        RemoveShown();
        _dashboard = variant.Create(this, _settings.Options);
        _dashboard.OnIntent = OnScreenIntent;
        _host.AddView(_dashboard, 0, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // Полосы — над панелью, под служебной строкой fps: она стендовая и тревоге не ровня.
        _hostBars = new AlertBarsView(this, _settings.Options) { Alert = BarsAlert };
        _host.AddView(_hostBars, 1, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        _dashboard.Rotation = (float)_settings.Options.Tilt;

        // Обход переключает варианты сам, и пикер должен показывать то, что на экране: иначе после
        // обхода он врёт про то, на что смотришь. Нулевой пункт пикера — «экран целиком».
        int index = DashboardCatalog.All.ToList().IndexOf(variant) + 1;
        if (_variantPicker.SelectedItemPosition != index) _variantPicker.SetSelection(index);

        _driver.Attach(_dashboard!, BuildFrame);
        _driver.Refresh();
    }

    /// <summary>
    /// Главный экран приложения целиком — тем же классом, что показывает райдеру
    /// <c>MainActivity</c>. Команды шторки здесь пустышки: их вид и поведение — те же, действия —
    /// стендовые (запись щёлкает точку записи, ⏻ перебирает состояния связи, ⚙ открывает ручки).
    /// </summary>
    private void ShowScreen()
    {
        RemoveShown();

        // Стенд мерит кадр панели (LastDrawMs) — половина того, ради чего варианты вообще сравнивают
        // на устройстве, — и потому держит её отдельно от того, что показано сейчас. Приложению
        // такое знание не нужно и не выдано: у него в руках только контракт IMainScreen.
        //
        // Панель собирает хозяин рамки, а не сама рамка (план 17 §3): приложению её выбирает реестр
        // вариантов, стенду — эта строка, а рамка обоим достаётся одна и та же, нейтральная.
        var panel = new TwinTapesDashboard(this, _settings.Options);
        _dashboard = panel;
        panel.OnIntent = OnScreenIntent;
        _screen = new MainScreenView(this, _settings.Options, panel);
        // Источник полос рамки — тот же, что у вариант-режима: сила тревоги из показаний позиции.
        _screen.Bars.Alert = BarsAlert;
        // «Не закрывать», как в боевом: слово «Закрепить» уже занято командой с замком, и две
        // одинаковые подписи в одной шторке — ровно та неразличимость, которую чинит план 25.
        _screen.Sheet.PinLabel = () => "Не закрывать";
        _screen.Sheet.PinGroup = Phone;
        _screen.Sheet.SectionLabel = SectionLabel;
        _screen.Sheet.ScreensSectionLabel = () => "Экран";
        // «Ссылки», не «Перейти»: семь букв с разрядкой длиннее корешка 48 dp — срезало концы
        // (слово владельца 10.08.2026).
        _screen.Sheet.LinksSectionLabel = () => "Ссылки";
        _screen.Sheet.SetCommands(BuildFakeCommands());
        _screen.Sheet.SetScreens(BuildScreens());
        _screen.Sheet.SetLinks(BuildFakeLinks());
        _host.AddView(_screen, 0, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _dashboard.Rotation = (float)_settings.Options.Tilt;

        if (_variantPicker.SelectedItemPosition != 0) _variantPicker.SetSelection(0);

        ShowTiles(_tilesShown);
    }

    /// <summary>
    /// Переключение экранов в рамке — то же, что делает приложение: рамка меняет содержимое, водитель
    /// кадра переставляется на новый экран (очередь <c>PostOnAnimation</c> принадлежит его
    /// <c>View</c>), полоса тревоги и шторка остаются общими.
    /// </summary>
    private void ShowTiles(bool tiles)
    {
        if (_screen is not { } screen) return;

        _tilesShown = tiles;
        screen.Show(tiles
            // История графикам идёт из базы стенда — тем же читателем, каким её читает приложение
            // (LabStore): свой генератор точек проверял бы путь, которого в бою нет.
            ? _tiles ??= new TilesScreen(this, _settings.Options, LabMetricWords.Get, _store?.History, _layoutFile)
            {
                OnIntent = OnScreenIntent,
            }
            : _dashboard!);

        _driver.Attach(screen.Current, BuildFrame);
        _driver.Refresh();
    }

    private void RemoveShown()
    {
        if (_screen is { } screen)
        {
            _host.RemoveView(screen);
            _screen = null;
            // Вместе с рамкой уходит и её экран плиток: у View один родитель, и в новую рамку
            // старый уже не встанет.
            _tiles = null;
        }
        else if (_dashboard is { } dashboard)
        {
            _host.RemoveView(dashboard);
            if (_hostBars is { } bars) _host.RemoveView(bars);
        }

        _dashboard = null;
        _hostBars = null;
    }

    /// <summary>
    /// Полоса переключения экранов (план 23 §2.2) — те же два корешка, что у приложения, и ведут
    /// они туда же. Выбор стенд не запоминает: у него нет слоёв настроек, а держать ради одного
    /// признака вторую память смысла нет.
    /// </summary>
    private IReadOnlyList<QuickSheetScreen> BuildScreens() =>
    [
        new()
        {
            Icon = QuickIcons.Panel,
            Label = "Панель",
            IsSelected = () => !_tilesShown,
            Select = () => ShowTiles(false),
        },
        new()
        {
            Icon = QuickIcons.Tiles,
            Label = "Цифры",
            IsSelected = () => _tilesShown,
            Select = () => ShowTiles(true),
        },
    ];

    /// <summary>
    /// Меню шторки — <b>полный состав боевого</b>: те же значки, те же слова, тот же порядок и те же
    /// разделы, действия стендовые (решение владельца 10.08.2026). Чего стенд не умеет — стоит
    /// неактивным, а не отсутствует: пропавшая кнопка сдвигает соседей, и снимок со стенда
    /// перестаёт отвечать на вопрос «что увидит райдер», ради которого его и делают. Сегодня на
    /// этом и споткнулись — «пропали» кнопки, которых на стенде никогда не было, а отличить
    /// забывчивость от замысла было нечем; теперь это держит замок <c>LabSheetParityTests</c>.
    /// <para>
    /// Слова продублированы с <c>AppStrings</c> приложения сознательно: библиотека слов не держит,
    /// а ссылаться на ресурсы приложения стенд не может — потому их и сверяет тот же замок.
    /// </para>
    /// </summary>
    private IReadOnlyList<QuickSheetCommand> BuildFakeCommands() =>
    [
        new()
        {
            Icon = QuickIcons.Light,
            Group = WheelNow,
            Label = () => _lightOn ? "Фара вкл" : "Фара",
            IsOn = () => _lightOn,
            Action = () =>
            {
                _lightOn = !_lightOn;
                return Task.CompletedTask;
            },
        },
        new()
        {
            Icon = QuickIcons.Beep,
            Group = WheelNow,
            Label = () => "Бип",
            Action = () => Task.CompletedTask,
        },
        new()
        {
            // Связь у стенда своя — колесо ей ни к чему: кнопка крутит фазы «⇄», как и раньше, а
            // слово берёт то же, что боевое, от фазы «отключено».
            Icon = QuickIcons.Power,
            Group = WheelNow,
            Label = () => _linkCycle.Current.Phase == LinkPhase.Idle ? "Подключить" : "Отключить",
            Action = () =>
            {
                CycleLink();
                return Task.CompletedTask;
            },
        },
        new()
        {
            Icon = QuickIcons.Record,
            Group = Ride,
            Label = () => _settings.Recording ? "Стоп" : "Запись",
            IsOn = () => _settings.Recording,
            Action = () =>
            {
                _settings.Recording = !_settings.Recording;
                return Task.CompletedTask;
            },
        },
        new()
        {
            Icon = QuickIcons.Reset,
            Group = Ride,
            Label = () => "Сброс пиков",
            Action = () => Task.CompletedTask,
        },
        new()
        {
            // Реплей боевой показывает только у отладочного транспорта — а стенд весь и есть
            // отладочный транспорт: кнопка пускает и останавливает ту же запись, что ручка «⏸».
            Icon = QuickIcons.Play,
            Group = Ride,
            Label = () => _playing ? "Стоп" : "Пуск",
            Action = () =>
            {
                SetPlaying(!_playing);
                return Task.CompletedTask;
            },
        },
        new()
        {
            Icon = QuickIcons.Sun,
            Group = Phone,
            Label = () => _keepOn ? "Не гаснет" : "Не гасить",
            IsOn = () => _keepOn,
            Action = () =>
            {
                _keepOn = !_keepOn;
                return Task.CompletedTask;
            },
        },
        new()
        {
            Icon = QuickIcons.Lock,
            Group = Phone,
            Label = () => _overLock ? "Экран закреплён" : "Закрепить экран",
            IsOn = () => _overLock,
            Action = () =>
            {
                _overLock = !_overLock;
                return Task.CompletedTask;
            },
        },
    ];

    // Разделы — те же ключи и та же раскладка, что у приложения: шторка одна на двоих, и снимки со
    // стенда должны показывать ровно те строки, которые увидит райдер (план 32 §1, этап 4).
    private const string WheelNow = "wheel";
    private const string Ride = "ride";
    private const string Phone = "phone";

    private static string SectionLabel(string group) => group switch
    {
        WheelNow => "Колесо",
        Ride => "Поездка",
        Phone => "Телефон",
        _ => "",
    };

    /// <summary>
    /// Переходы — все три боевых, в том же порядке. Экранов данных и поездок у стенда нет, и они
    /// стоят неактивными: место у кнопки то же, что увидит райдер, а нажатие ей не отдано
    /// (решение владельца 10.08.2026). «Настройки» ведут в стендовые — они у него есть.
    /// </summary>
    private IReadOnlyList<QuickSheetLink> BuildFakeLinks() =>
    [
        new()
        {
            Icon = QuickIcons.Data,
            Label = "Данные",
            IsEnabled = () => false,
            Open = () => { },
        },
        new()
        {
            Icon = QuickIcons.Rides,
            Label = "Поездки",
            IsEnabled = () => false,
            Open = () => { },
        },
        new()
        {
            Icon = QuickIcons.Settings,
            Label = "Настройки",
            Open = () => StartActivity(new Intent(this, typeof(LabSettingsActivity))),
        },
    ];

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        _playButton.Text = playing ? "⏸" : "▶";
    }

    private void OnSettingsChanged()
    {
        _source?.Retune(_settings.Options);
        if (_screen is null) ShowVariant(_variant); else ShowScreen();
        _ = LoadScenarioKeepingPosition();
    }

    /// <summary>
    /// Правка записи меняет саму запись, поэтому её приходится пересобрать — но позицию при этом
    /// надо удержать: крутить ручку и каждый раз улетать в начало нельзя.
    /// </summary>
    private async Task LoadScenarioKeepingPosition()
    {
        var was = _position;
        await LoadScenario();
        _position = was > _source!.Timeline.Duration ? TimeSpan.Zero : was;
        _driver.Refresh();
    }

    /// <summary>
    /// Шаг ровно на один кадр телеметрии — на нём и видно, каким скачком приходит значение и что
    /// с этим скачком делает вариант.
    /// </summary>
    private void Step(int frames)
    {
        if (_source is not { } source) return;

        SetPlaying(false);
        var timeline = source.Timeline;
        int index = Math.Clamp(timeline.IndexAt(_position) + frames, 0, timeline.Frames.Count - 1);
        _position = timeline.Frames[index].At;
        _driver.Refresh();
    }

    private void OnHostTapped()
    {
        if (_walk.IsWalking)
        {
            Advance();
            return;
        }

        if (_chrome.Visibility != ViewStates.Visible) ShowChrome(true);
    }

    private void ShowChrome(bool visible)
    {
        var target = visible ? ViewStates.Visible : ViewStates.Gone;
        _chrome.Visibility = target;
        _transport.Visibility = target;

        // Счётчик кадров прячется вместе с остальным стендом: он лежит поверх верха панели, а там
        // теперь живёт плашка связи — на снимке они наезжают друг на друга. Хром прячут ровно затем,
        // чтобы на снимке осталась панель, а не стенд, и счётчик здесь такой же стенд, как кнопки.
        _fps.Visibility = target;
    }

    /// <summary>Следующее состояние связи. Счётчик «данных нет N с» начинает считать с этого мига.</summary>
    private void CycleLink() => _linkCycle.Next(System.Environment.TickCount64);

    /// <summary>
    /// Обход: все варианты во всех характерных точках сценария, по одному шагу на касание экрана —
    /// перебор ведёт <see cref="ShotWalk"/>, здесь только запуск и переход к следующему шагу.
    /// </summary>
    private void StartWalk()
    {
        if (_source is not { } source) return;

        _walk.Start(_scenario.Id, source.Timeline);

        SetPlaying(false);
        ShowChrome(false);
        Advance();
    }

    private void Advance()
    {
        var step = _walk.Advance();
        if (step is null)
        {
            ShowChrome(true);
            _statusLabel.Text = $"Обход пройден: {_walk.Count} шагов, порядок в order.txt";
            return;
        }

        var (variant, mark) = step.Value;
        ShowVariant(variant);
        _position = mark.At;
        _driver.Refresh();
    }

    // ---- Разметка, собранная кодом -----------------------------------------------------------

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Color.ParseColor("#101010"));

        root.AddView(BuildChrome(), new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        // Панель растёт весом: всё, что не хром, отдано ей.
        root.AddView(BuildHost(), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        root.AddView(BuildTransport(), new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        return root;
    }

    /// <summary>
    /// Хром стенда. Он же прячется целиком: снимок с пикерами и ползунками сравнивать нельзя, на
    /// нём видно не панель, а стенд.
    /// </summary>
    private View BuildChrome()
    {
        _chrome = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _chrome.SetPadding(Dp(6), Dp(4), Dp(6), Dp(4));

        var items = new List<string> { "Экран целиком" };
        items.AddRange(DashboardCatalog.All.Select(v => $"{v.Id} · {v.Title}"));
        _variantPicker = Picker(items);
        _variantPicker.ItemSelected += (_, e) =>
        {
            if (e.Position == 0)
            {
                if (_screen is null) ShowScreen();
                return;
            }

            var variant = DashboardCatalog.All[e.Position - 1];
            if (variant == _variant && _screen is null) return;
            ShowVariant(variant);
        };

        _scenarioPicker = Picker(ScenarioCatalog.All.Select(s => s.Title).ToList());
        _scenarioPicker.ItemSelected += (_, e) =>
        {
            var scenario = ScenarioCatalog.All[e.Position];
            if (scenario == _scenario) return;
            _scenario = scenario;
            _ = LoadScenario();
        };

        _chrome.AddView(_variantPicker, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        _chrome.AddView(_scenarioPicker, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        // Кнопки — ровно по значку. Без явной ширины системная Button держит свои 64 dp минимума и
        // поля фона, три штуки съедали больше половины строки, а пикеры ужимались до одних стрелок.
        _chrome.AddView(ChromeButton("⚙", () => StartActivity(new Intent(this, typeof(LabSettingsActivity)))), IconSize());
        // HOTRELOAD: пересобрать экран пальцем — тот же вход, что у команды и у события перезагрузки.
        _chrome.AddView(ChromeButton("♻", Rebuild), IconSize());
        _chrome.AddView(ChromeButton("⇄", CycleLink), IconSize());
        _chrome.AddView(ChromeButton("⤓", StartWalk), IconSize());
        _chrome.AddView(ChromeButton("⛶", () => ShowChrome(false)), IconSize());

        return _chrome;
    }

    private View BuildHost()
    {
        _host = new FrameLayout(this) { Clickable = true };
        _host.Click += (_, _) => OnHostTapped();

        // Счётчик кадров — поверх панели и сверху экрана, как в MAUI-версии.
        _fps = new FpsOverlayView(this);
        _host.AddView(_fps, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(22))
        {
            Gravity = GravityFlags.Top,
        });

        return _host;
    }

    private View BuildTransport()
    {
        _transport = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _transport.SetPadding(Dp(6), Dp(2), Dp(6), Dp(2));

        _positionSlider = new SeekBar(this) { Max = 1 };
        _positionSlider.ProgressChanged += (_, e) =>
        {
            if (!e.FromUser) return;
            _seeking = true;
            _position = TimeSpan.FromSeconds((double)e.Progress / TicksPerSecond);
            _driver.Refresh();
        };
        _positionSlider.StopTrackingTouch += (_, _) => _seeking = false;
        _transport.AddView(_positionSlider, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);

        _playButton = ChromeButton("⏸", () => SetPlaying(!_playing));
        row.AddView(_playButton, IconSize());
        row.AddView(ChromeButton("◀", () => Step(-1)), IconSize());
        row.AddView(ChromeButton("▶", () => Step(1)), IconSize());

        _ratePicker = Picker(Rates.Select(r => $"×{r:0.##}").ToList());
        _ratePicker.ItemSelected += (_, e) => _rate = Rates[e.Position];
        row.AddView(_ratePicker, new LinearLayout.LayoutParams(Dp(100), ViewGroup.LayoutParams.WrapContent));

        _positionLabel = new TextView(this) { Gravity = GravityFlags.End | GravityFlags.CenterVertical };
        _positionLabel.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal);
        _positionLabel.SetTextSize(ComplexUnitType.Sp, 13);
        // Одной строкой: «6.9 / 120 с» переносилось по знаку в столбик, когда места было мало.
        _positionLabel.SetSingleLine(true);
        row.AddView(_positionLabel, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent, 1f));

        _transport.AddView(row, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        // Метки: по ним варианты снимаются в одних и тех же точках.
        _markBar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var scroller = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        scroller.AddView(_markBar);
        _transport.AddView(scroller, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        _statusLabel = new TextView(this);
        _statusLabel.SetTextSize(ComplexUnitType.Sp, 11);
        _statusLabel.Alpha = 0.7f;
        _transport.AddView(_statusLabel, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        return _transport;
    }

    private Spinner Picker(IList<string> items)
    {
        var spinner = new Spinner(this);
        var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, items.ToArray());
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        spinner.Adapter = adapter;
        return spinner;
    }

    private Button ChromeButton(string text, Action onClick)
    {
        var button = new Button(this) { Text = text };
        button.SetTextSize(ComplexUnitType.Sp, 16);
        button.SetAllCaps(false);
        button.SetPadding(0, 0, 0, 0);
        button.SetMinimumWidth(0);
        button.SetMinWidth(0);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Кнопка со значком: ровно под значок, чтобы хром не отъедал строку у пикеров.</summary>
    private LinearLayout.LayoutParams IconSize() =>
        new(Dp(52), ViewGroup.LayoutParams.WrapContent) { RightMargin = Dp(4) };

    private int Dp(float dp) => (int)Math.Round(dp * _density);

    /// <summary>
    /// Высота системной строки, как её сообщило окно (<see cref="EdgeToEdge"/>). Не константа: бар
    /// разный на разных телефонах, и отступы, подогнанные под выдуманное число, проверяли бы не то.
    /// <para>
    /// В режиме «экран целиком» панель под этим баром и правда лежит. В обычном режиме над ней ряд
    /// органов управления, и тогда это уже не факт, а условие показа: панель рисует тень и
    /// затухание так же, как нарисует в приложении. Выключается тумблером «Панель под системной
    /// строкой».
    /// </para>
    /// </summary>
    private int _barInset;
}
