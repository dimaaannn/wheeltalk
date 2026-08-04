using Android.Content;
using Android.Graphics;
using Android.Views;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Dashboard.Droid;

/// <summary>
/// Общее у всех вариантов панели: они собираются кодом, получают одни и те же настройки и умеют
/// одно — показать очередное значение. Порт <c>WheelTalk.Dashboard/DashboardView.cs</c> с поправкой
/// на платформу: у MAUI это <c>ContentView</c> с дочерними канвами на прибор, здесь — один
/// <see cref="View"/> с одним <see cref="OnDraw"/> на всю панель (см. class doc
/// <c>Layouts/TwinTapesDashboard</c>: у обычного Android-View нет системы разметки с общей
/// инвалидацией между соседями, есть один <c>Invalidate()</c> на весь холст).
/// <para>
/// Фон и вуаль устаревших данных рисует база, а не раскладка: это не приборы, а состояние экрана,
/// и одинаково оно во всех вариантах. Раскладке остаётся <see cref="DrawPanel"/> — только её
/// собственные приборы. Полосы тревоги панель не рисует вовсе: они не принадлежат экрану (слово
/// владельца 05.08.2026) и живут самостоятельным элементом <c>Widgets/AlertBarsView</c> поверх
/// рамки главного экрана.
/// </para>
/// <para>
/// Кадры гонит <c>PostInvalidateOnAnimation</c> в конце <see cref="OnDraw"/>: отрисовка привязана к
/// вертикальному синхроимпульсу сама по себе, отдельного таймера кадров не нужно.
/// <see cref="Show(DashboardReading)"/> лишь обновляет данные для следующего кадра.
/// </para>
/// <para>
/// Панель — основной экран приложения (<see cref="IMainScreen"/>): принимает посчитанное состояние
/// кадра и сообщает намерения, а откуда взялись показания и что делать с тапом по плашке связи, не
/// знает вовсе.
/// </para>
/// </summary>
public abstract class DashboardView : View, IMainScreen
{
    /// <summary>Порог смены наклона — меньше не стоит перерисовывать ради шума с плавающей точкой.</summary>
    private const float TiltEpsilon = 0.01f;

    /// <summary>Буфер под <c>GetLocationInWindow</c>: тапы редки, но выделять массив на каждый незачем.</summary>
    private readonly int[] _windowLocation = new int[2];

    /// <summary>
    /// Вуаль устаревших данных — косые серые полосы (dashboard-feedback.md, прогон 4 §5). Здесь
    /// было сплошное затемнение в 55 %, и оно читалось как приглушённая яркость экрана, а не как
    /// «связи нет»: у затемнения нет формы, оно меняет только светлоту — а светлота на улице и так
    /// всё время разная (автояркость, солнце, очки). Величина, которой на экране распоряжается не
    /// приложение, сообщением быть не может. Полосы — форма: видны при любой яркости и оставляют
    /// между собой читаемый контент.
    /// <para>
    /// Подложки под полосами нет намеренно: сплошное затемнение существовало ровно затем, чтобы
    /// вуаль была заметна, и эту работу забрали полосы. Оставить оба слоя — затемнить дважды.
    /// </para>
    /// <para>
    /// <b>Отличимо от barber pole.</b> Косая штриховка на панели уже занята: <c>TapeHatchPart</c>
    /// метит ленту ШИМ выше предела красным шагом 10 dp с наклоном «/». Два разных сигнала одной
    /// фактурой хуже одного — на предельном ШИМ они окажутся на экране одновременно. Поэтому здесь
    /// разошлись по всем трём признакам: серый вместо красного, шаг втрое шире, наклон «\».
    /// </para>
    /// <para>
    /// Серый **тёмный**, а не светлый. Светлый (158) промерян 01.08.2026 и не годится: полупрозрачная
    /// заливка тянет всё к своему тону, поэтому светлая лента ШИМ оставалась почти нетронутой
    /// (138 → 146 под полосой) и вылезала на передний план, а тёмная лента заряда, наоборот,
    /// размывалась и пропадала. Тёмный тон гасит светлое и почти не трогает тёмное — то есть
    /// работает так же, как прежнее затемнение, но формой.
    /// </para>
    /// </summary>
    private const float StaleStripeAlpha = 0.45f;

    /// <summary>Ширина полосы и период (полоса + просвет), точки экрана.</summary>
    private const float StaleStripeWidth = 14;

    private const float StaleStripePeriod = 30;

    /// <summary>
    /// Тень под статус-баром: панель уходит под него, и системные значки — часы, заряд, связь —
    /// ложатся прямо на приборы. Они всегда светлые (<c>AppearanceLightStatusBars = false</c>,
    /// панель тёмная при любой теме устройства), но наверху у панели светло-серые ленты, и белое по
    /// светло-серому не читается. Узкий градиент от тёмного к прозрачному держит значки читаемыми
    /// над чем угодно и не трогает приборы: он кончается там же, где кончается бар.
    /// <para>
    /// Не путать с вуалью устаревших данных: та — сообщение и накрывает всё, эта — служебная
    /// подложка чужим значкам, постоянная и в высоту одного бара.
    /// </para>
    /// </summary>
    private const int StatusScrimAlpha = 150;

    /// <summary>Во сколько раз тень длиннее самого бара: последняя треть — это растворение, чтобы граница не читалась линией.</summary>
    private const float StatusScrimHeight = 1.5f;

    private readonly LinkBadgeDrawable _link;
    private readonly PanelChromeDrawable _chrome;
    private readonly Paint _background = new() { AntiAlias = true };
    private readonly Paint _staleVeil = new() { AntiAlias = true };
    private readonly Paint _statusScrim = new();

    /// <summary>Для какой высоты построен градиент тени: пересобирается только когда она изменилась, а не на каждый кадр.</summary>
    private float _scrimBuiltFor = -1;

    protected DashboardView(Context context, DashboardOptions options) : base(context)
    {
        Options = options;
        Density = context.Resources?.DisplayMetrics?.Density ?? 1;
        _link = new LinkBadgeDrawable { Options = options };
        _chrome = new PanelChromeDrawable { Options = options };

        // Настройки читаются динамически на каждом кадре, но правку в «Отображении» иначе было бы
        // видно только на следующем кадре телеметрии — их приход не связан с частотой правок в
        // настройках. Подписка на Changed делает эффект мгновенным.
        options.Changed += Invalidate;
    }

    protected DashboardOptions Options { get; }

    /// <summary>
    /// Плотность экрана. Канва Android считает в физических пикселях, а MAUI-исходник — в dp,
    /// поэтому каждая перенесённая абсолютная величина домножается на неё.
    /// </summary>
    protected float Density { get; }

    protected DashboardReading Reading { get; private set; } = DashboardReading.Idle;

    /// <summary>
    /// Последний кадр телеметрии устарел. Порог «сколько это — устарело» задаёт вызывающая сторона
    /// (в библиотеке возраста кадра нет, только этот флаг): приборы под вуалью остаются прежними
    /// значениями — задача вуали не спрятать их, а быть на них заметной.
    /// </summary>
    public bool IsStale { get; set; }

    /// <summary>
    /// Состояние связи и всё, что панель про него показывает. Панель не знает, откуда берётся связь
    /// (у стенда её нет вовсе), — только как её показать; слова состояния тоже даёт вызывающий,
    /// потому что у приложения они свои и переводимые.
    /// </summary>
    public LinkPhase LinkPhase { get; set; } = LinkPhase.Live;

    public string LinkText { get; set; } = "";

    /// <summary>Сколько секунд нет данных. Ноль убирает счётчик с плашки.</summary>
    public int LinkSeconds { get; set; }

    /// <summary>Имя колеса — то, которым его назвал хозяин. Показывается на стоянке.</summary>
    public string WheelName { get; set; } = "";

    /// <summary>Идёт ли запись поездки — точка в углу поля панели.</summary>
    public bool Recording { get; set; }

    /// <summary>
    /// Показывать ли точку записи вообще. Выключено по умолчанию, как и <see cref="ShowSheetHint"/>:
    /// обе метки включает тот, кто про них знает. Иначе они появились бы у каждого, кто просто
    /// пересобрал панель, — а у приложения к ним прилагается остальной перенос хрома в шторку.
    /// </summary>
    public bool ShowRecordDot { get; set; }

    /// <summary>Подсказка про шторку быстрых команд внизу. Пока шторка открыта — не нужна.</summary>
    public bool ShowSheetHint { get; set; }

    /// <summary>
    /// Попало ли касание в точку записи. Панель её рисует, панель про неё и отвечает — наружу
    /// уходит намерение, а не координаты (<see cref="Tap"/>).
    /// </summary>
    private bool HitsRecordDot(float x, float y) => _chrome.HitsRecordDot(ChromeArea, Density, x, y);

    /// <summary>
    /// Попало ли касание в галочку — подсказку про шторку. Тот же приём, что у точки записи: панель
    /// рисует, панель и отвечает, наружу уходит намерение (<see cref="Tap"/>).
    /// </summary>
    private bool HitsSheetHint(float x, float y) => _chrome.HitsSheetHint(ChromeArea, Density, x, y);

    /// <summary>
    /// Попало ли касание в плашку связи. Область та же, в которую плашка рисуется
    /// (<see cref="OnDraw"/>), и берётся она отсюда, а не считается заново.
    /// </summary>
    private bool HitsLinkBadge(float x, float y) => _link.Hits(LinkArea, Density, x, y);

    /// <summary>Область плашки связи: вся ширина, от нижней кромки статус-бара и ниже.</summary>
    private RectF LinkArea => new(0, TopInset, Width, Height);

    /// <summary>
    /// Высота системного статус-бара в пикселях. Панель рисует фон под ним (фон за системными
    /// барами — тёмный фон панели, adaptive-layout.md §4), а плашка связи и имя колеса начинаются
    /// ниже: текст под часами не читается. У стенда бар свой хром не перекрывает — там ноль.
    /// </summary>
    public float TopInset { get; set; }

    /// <summary>
    /// Сколько заняла последняя отрисовка, мс. Нужно стенду: средняя стоимость кадра — половина
    /// того, ради чего варианты вообще сравнивают на устройстве.
    /// </summary>
    public double LastDrawMs { get; private set; }

    /// <summary>
    /// Вызывается на каждый новый кадр телеметрии. Сама отрисовка идёт по vsync, не по этому
    /// вызову — <see cref="Show(DashboardReading)"/> только кладёт данные, которые подхватит
    /// очередной <see cref="OnDraw"/>.
    /// </summary>
    public void Show(DashboardReading reading)
    {
        Reading = reading;
        Invalidate();
    }

    View IMainScreen.View => this;

    /// <summary>Куда уходят намерения — тап по плашке связи и по точке записи. Ставит хозяин экрана.</summary>
    public Action<MainScreenIntent>? OnIntent { get; set; }

    /// <summary>
    /// Очередной кадр основного экрана: приборы, хром и то, что панель считает про себя сама —
    /// наклон и светлая фаза моргания. Раньше это раскладывал по полям водитель кадра; поля
    /// панельные, и решать за них ему было нечего.
    /// </summary>
    public void Show(MainScreenFrame frame)
    {
        float tilt = (float)Options.Tilt;
        if (Math.Abs(Rotation - tilt) > TiltEpsilon)
        {
            Rotation = tilt;
        }

        if (frame.Reading is { } reading)
        {
            Show(reading);
        }

        LinkPhase = frame.LinkPhase;
        LinkText = frame.LinkText;
        LinkSeconds = frame.LinkSeconds;
        WheelName = frame.WheelName;
        Recording = frame.Recording;
        ShowRecordDot = frame.ShowRecordDot;
        ShowSheetHint = frame.ShowSheetHint;
        IsStale = frame.IsStale;
        TopInset = frame.TopInset;
    }

    /// <summary>
    /// Касание по панели: попало ли оно в плашку связи или в точку записи, знает только панель —
    /// она их рисует. Наружу уходит намерение, а что с ним делать, решает хозяин экрана.
    /// <para>
    /// Координаты приходят оконные — жест ловит хозяин, у которого уже есть
    /// <c>DispatchTouchEvent</c>, — и переводятся в свои здесь: где панель стоит внутри разметки,
    /// хозяину знать незачем.
    /// </para>
    /// </summary>
    public void Tap(float windowX, float windowY)
    {
        GetLocationInWindow(_windowLocation);
        float x = windowX - _windowLocation[0];
        float y = windowY - _windowLocation[1];

        if (HitsLinkBadge(x, y))
        {
            OnIntent?.Invoke(MainScreenIntent.ShowConnection);
            return;
        }

        if (HitsRecordDot(x, y))
        {
            OnIntent?.Invoke(MainScreenIntent.ShowRecording);
            return;
        }

        if (HitsSheetHint(x, y))
        {
            OnIntent?.Invoke(MainScreenIntent.ShowSheet);
        }
    }

    /// <summary>
    /// Область под приборы — весь холст. Раскладка может сдвинуть край, если ей есть зачем;
    /// у главной (<c>TwinTapesDashboard</c>) причин больше нет: хром лежит поверх приборов, а не
    /// занимает полосу над ними.
    /// </summary>
    protected virtual RectF Content => new(0, 0, Width, Height);

    /// <summary>
    /// Где раскладка разрешает рисовать мелкие метки (точка записи, подсказка про шторку) — по
    /// умолчанию вся площадь приборов. Раскладка сужает её, если по краям у неё что-то, на что метку
    /// класть нельзя: у главной это ленты со шкалами, и метка в углу экрана села бы прямо на деления.
    /// </summary>
    protected virtual RectF ChromeArea => Content;

    protected sealed override void OnDraw(Canvas canvas)
    {
        long started = Java.Lang.JavaSystem.NanoTime();

        _background.Color = Options.Palette.Background;
        canvas.DrawRect(0, 0, Width, Height, _background);

        var content = Content;
        DrawPanel(canvas, content);

        // Вуаль устаревших данных: старые показания остаются нарисованными как обычно (не блокируем
        // отображение, решение владельца) — поверх них ложится штриховка.
        if (IsStale)
        {
            DrawStaleStripes(canvas);
        }

        // Тень под статус-баром — поверх приборов и вуали (значкам всё равно, что под ними), но
        // ниже хрома: плашка связи начинается под баром и затемнять её незачем.
        DrawStatusScrim(canvas);

        // Хром — поверх вуали: он про приложение, а не про данные, и «протухать» вместе с ними не
        // должен. Плашка связи вдобавок объясняет саму вуаль — под ней она объясняла бы себя.
        _chrome.Recording = Recording;
        _chrome.ShowRecordDot = ShowRecordDot;
        _chrome.ShowSheetHint = ShowSheetHint;
        _chrome.Draw(canvas, ChromeArea, Density);

        // Плашка связи — последней, поверх меток: точка записи стоит в том же верхнем углу, и
        // нарисованная после она ложилась на плашку оранжевым пятном. Порядок и по смыслу такой:
        // плашка — сообщение, метка — справка, и спорить им незачем.
        //
        // Полос тревоги здесь больше нет: они не принадлежат экрану (слово владельца 05.08.2026) и
        // лежат самостоятельным элементом поверх рамки (MainScreenView.Bars) — над вуалью, метками и
        // плашкой, как лежали здесь последним слоем.
        _link.Phase = LinkPhase;
        _link.StateText = LinkText;
        _link.Seconds = LinkSeconds;
        _link.WheelName = WheelName;
        _link.SpeedKmh = Reading.SpeedKmh;
        _link.Draw(canvas, LinkArea, Density);

        LastDrawMs = (Java.Lang.JavaSystem.NanoTime() - started) / 1_000_000.0;
        PostInvalidateOnAnimation();
    }

    protected abstract void DrawPanel(Canvas canvas, RectF content);

    /// <summary>
    /// Градиент под системными значками. Высота считается от инсета статус-бара: нет инсета — нет и
    /// бара над панелью (так у стенда), рисовать нечего.
    /// </summary>
    private void DrawStatusScrim(Canvas canvas)
    {
        if (TopInset <= 0) return;

        float height = TopInset * StatusScrimHeight;
        if (Math.Abs(_scrimBuiltFor - height) > 0.5f)
        {
            _statusScrim.SetShader(new LinearGradient(
                0, 0, 0, height,
                Color.Argb(StatusScrimAlpha, 0, 0, 0),
                Color.Transparent,
                Shader.TileMode.Clamp!));
            _scrimBuiltFor = height;
        }

        canvas.DrawRect(0, 0, Width, height, _statusScrim);
    }

    /// <summary>
    /// Полосы «данные устарели» по всей панели. Наклон задаётся тем, что линия идёт из левого края
    /// вниз направо на собственную ширину экрана, — ровно 45°, но в другую сторону, чем штриховка
    /// предела ШИМ. Начало отсчёта смещено на ширину вверх, иначе верхний левый угол остался бы
    /// непокрытым: линия, начатая на нулевой высоте, приходит к правому краю уже за пределами вида.
    /// <para>
    /// Концы уводятся за края экрана: у линии плоский торец, и на 45° он читается косым срезом.
    /// Полоса должна уходить за кромку, а не заканчиваться у неё.
    /// </para>
    /// </summary>
    private void DrawStaleStripes(Canvas canvas)
    {
        _staleVeil.SetStyle(Paint.Style.Stroke);
        _staleVeil.StrokeWidth = StaleStripeWidth * Density;

        // Серый, а не цвет палитры: вуаль говорит про связь, а не про раскраску шкал, и должна
        // выглядеть одинаково на любой палитре.
        _staleVeil.Color = Color.Argb((int)Math.Round(StaleStripeAlpha * 255), 72, 72, 72);

        float step = StaleStripePeriod * Density;
        float over = StaleStripeWidth * Density;

        for (float y = -Width; y < Height + Width; y += step)
        {
            canvas.DrawLine(-over, y - over, Width + over, y + Width + over, _staleVeil);
        }
    }

    /// <summary>Куда придёт ШИМ через заданное настройками время, если производная сохранится.</summary>
    protected double? Trend => Options.ShowTrend ? Reading.PwmIn(Options.TrendSeconds) : null;

    protected override void OnDetachedFromWindow()
    {
        // DashboardOptions — синглтон на всё приложение, а не View: без отписки пересоздание этого
        // View (поворот экрана, смена варианта на стенде) копило бы подписчиков на Changed до
        // утечки. У MAUI-версии этого риска не было — ContentView живёт вместе со страницей ровно
        // один раз за сессию.
        Options.Changed -= Invalidate;
        base.OnDetachedFromWindow();
    }
}
