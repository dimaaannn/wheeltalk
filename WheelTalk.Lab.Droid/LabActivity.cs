using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Dashboard.Droid.Widgets;
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
/// <item>Тревожные полосы рисует сама панель (<c>DashboardView</c>), а не отдельная канва стенда
/// поверх неё — в приложении они уже внутри панели, и стенд обязан показывать то же самое.</item>
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
[Activity(Label = "WheelTalk Lab", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
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
    private GestureDetector? _sheetGesture;
    private bool _lightOn;

    /// <summary>
    /// Стендовая заглушка выбора экрана (план 23 §2.2, шаг 2): настоящих экранов ещё нет, поле
    /// только держит, какой корешок подсвечен в полосе шторки — переключение никуда не ведёт.
    /// </summary>
    private int _screenTab;

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
    }

    protected override void OnDestroy()
    {
        _settings.Changed -= OnSettingsChanged;
        base.OnDestroy();
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
            _sheetGesture ??= new GestureDetector(this, new SwipeUpFromEdgeListener(
                () => _screen?.Sheet.Toggle(), Resources!.DisplayMetrics!.HeightPixels, Dp(64)));
            _sheetGesture.OnTouchEvent(ev);
        }
        return base.DispatchTouchEvent(ev);
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
        }

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
    private MainScreenFrame BuildFrame()
    {
        var link = _linkCycle.Current;
        return new MainScreenFrame
        {
            Reading = _source?.At(_position),
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
        _host.AddView(_dashboard, 0, new FrameLayout.LayoutParams(
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
        _screen = new MainScreenView(this, _settings.Options);

        // Стенд знает, что в рамке сейчас панель, и это его право: он мерит её кадр (LastDrawMs) —
        // половина того, ради чего варианты вообще сравнивают на устройстве. Приложению такое
        // знание не нужно и не выдано: у него в руках только контракт IMainScreen.
        _dashboard = _screen.Current as DashboardView;
        _screen.Sheet.PinLabel = () => "Закрепить";
        _screen.Sheet.SetCommands(BuildFakeCommands());
        _screen.Sheet.SetScreens(BuildFakeScreens());
        _host.AddView(_screen, 0, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _dashboard!.Rotation = (float)_settings.Options.Tilt;

        if (_variantPicker.SelectedItemPosition != 0) _variantPicker.SetSelection(0);

        _driver.Attach(_screen.Current, BuildFrame);
        _driver.Refresh();
    }

    private void RemoveShown()
    {
        if (_screen is { } screen)
        {
            _host.RemoveView(screen);
            _screen = null;
        }
        else if (_dashboard is { } dashboard)
        {
            _host.RemoveView(dashboard);
        }

        _dashboard = null;
    }

    /// <summary>
    /// Полоса переключения экранов (план 23 §2.2, шаг 2) — два корешка-заглушки, чтобы вид был
    /// виден на стенде. Настоящих экранов ещё нет: выбор меняет только <see cref="_screenTab"/> и
    /// подсветку, содержимое шторки и панели не трогает — механика проверяется отдельно от того,
    /// что на неё позже сядет.
    /// </summary>
    private IReadOnlyList<QuickSheetScreen> BuildFakeScreens() =>
    [
        new()
        {
            Icon = "📊",
            Label = "Панель",
            IsSelected = () => _screenTab == 0,
            Select = () => _screenTab = 0,
        },
        new()
        {
            Icon = "📈",
            Label = "Телеметрия",
            IsSelected = () => _screenTab == 1,
            Select = () => _screenTab = 1,
        },
    ];

    /// <summary>
    /// Меню шторки — состав и подписи боевого (quick-commands-design.md §3), действия стендовые.
    /// Слова продублированы с <c>AppStrings</c> приложения сознательно: библиотека слов не держит,
    /// а ссылаться на ресурсы приложения стенд не может.
    /// </summary>
    private IReadOnlyList<QuickSheetCommand> BuildFakeCommands() =>
    [
        new()
        {
            Icon = "💡",
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
            Icon = "📢",
            Label = () => "Бип",
            Action = () => Task.CompletedTask,
        },
        new()
        {
            Icon = "🔴",
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
            Icon = "🔄",
            Label = () => "Сброс max",
            Action = () => Task.CompletedTask,
        },
        new()
        {
            Icon = "🔌",
            Label = () => "Связь",
            Action = () =>
            {
                CycleLink();
                return Task.CompletedTask;
            },
        },
        new()
        {
            Icon = "⚙",
            Label = () => "Настройки",
            Action = () =>
            {
                StartActivity(new Intent(this, typeof(LabSettingsActivity)));
                return Task.CompletedTask;
            },
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
