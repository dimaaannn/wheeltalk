using System.Globalization;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using Google.Android.Material.Button;
using Google.Android.Material.Chip;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Services;
using WheelTalk.Core.Settings;
using WheelTalk.Core.Settings.Device;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Droid;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Resources.Strings;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Droid.Ui;

using WheelTalk.Droid.App;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Diagnostics;

namespace WheelTalk.Droid.Settings;

/// <summary>
/// One settings category — variant B from <c>docs/settings-redesign.md</c> §4: the page structure is
/// unchanged (one scrolling list of rows, grouped by section), and what changed is how the list is
/// shown and reached. Rebuilt from <see cref="SettingsBinder"/> on every edit, exactly like the MAUI
/// original's <c>Show()</c> — a cascade (hardware PWM hides the three numbers it would otherwise be
/// computed from, an alarm's own switch hides its follow-up rows) has to close the moment its master
/// setting does, and a partial rebuild would leave stale rows on screen.
/// <para>
/// Three things variant B adds over the plain list: a segmented "Общее / это колесо" switch pinned
/// above the list (<see cref="MaterialButtonToggleGroup"/> — смотреть можно общий слой да слой
/// колеса из <see cref="WheelOptions"/>, третьего не бывает, поэтому две кнопки и есть весь
/// переключатель); quick-jump chips for "Отображение" only, the
/// one page with seven sections instead of two or three; and a row layout that always shows the
/// value's layer and, for numbers, its range — both used to live behind a menu or a dialog.
/// </para>
/// <para>
/// Needs <c>Theme.MaterialComponents</c> (set via the <c>Theme</c> on the <c>[Activity]</c> attribute,
/// <c>Resources/values/styles.xml</c>): <see cref="MaterialButtonToggleGroup"/>/<see cref="MaterialButton"/>/
/// <see cref="Chip"/> throw at construction time under the plain platform theme every other screen in
/// this project uses — this is the one screen that needs Material Components widgets, not just the
/// package.
/// </para>
/// </summary>
[Activity(Theme = "@style/SettingsTheme")]
public sealed class SettingsCategoryActivity : Activity
{
    public const string ExtraPage = "page";

    /// <summary>
    /// Раздел, к которому прокрутить страницу сразу после открытия — ключ секции
    /// (<see cref="SettingDescriptor.SectionKey"/>). Вместе с <see cref="ExtraKey"/> это
    /// <b>общий вход «покажи вот эту строку»</b>, а не частность ссылок: тем же входом сядет
    /// будущий поиск по настройкам (<c>docs/archive/settings-redesign.md</c>, вариант C), которому
    /// только прокрутки и не хватало.
    /// </summary>
    public const string ExtraSection = "section";

    /// <summary>Ключ строки, которую подсветить после прокрутки. Необязателен: без него страница просто встанет на разделе.</summary>
    public const string ExtraKey = "key";

    /// <summary>
    /// Цвета страницы — <b>ролями из палитры документных экранов</b> (план 33): их значения живут в
    /// ресурсах и переключаются вместе с системной темой. Прежде здесь стояли тринадцать тёмных
    /// литералов, и в светлой теме страница выходила о двух хозяевах — фон по теме, карточки по
    /// ночи (снимок владельца 13.08.2026).
    /// </summary>
    private const int HighlightMs = 1200;

    private SettingsBinder _binder = null!;
    private WheelSession _session = null!;
    private WheelOptions _wheel = null!;
    private SettingsPage _page;

    /// <summary>
    /// <b>Смотровая область</b>: какой слой страница показывает и правит. Поле страницы, а не рычаг
    /// приложения (план 29 §29.3): переключатель «Общее / это колесо» отвечает на вопрос «что я
    /// сейчас смотрю», а на вопрос «чем живёт приложение» отвечает выбор колеса, и трогать его
    /// отсюда нельзя. Пока рычаг был один, райдер, открывший «Общее», ехал по общим порогам.
    /// <para>
    /// Открывается страница на колесе — на том же слое, по которому живут: чаще всего человек
    /// пришёл править своё колесо, а не общее умолчание.
    /// </para>
    /// </summary>
    private string _viewScope = LayeredSettings.GlobalScope;

    private ScrollView _scroll = null!;
    private LinearLayout _content = null!;
    private MaterialButton? _globalButton;
    private MaterialButton? _wheelButton;

    private readonly Dictionary<string, View> _sectionAnchors = new(StringComparer.Ordinal);

    /// <summary>Карточки строк по ключу — по ней находит свою цель подсветка перехода.</summary>
    private readonly Dictionary<string, View> _rowCards = new(StringComparer.Ordinal);

    /// <summary>Куда встать при открытии: раздел и строка из <see cref="ExtraSection"/>/<see cref="ExtraKey"/>. Срабатывает один раз.</summary>
    private string? _pendingSection;
    private string? _pendingKey;

    /// <summary>
    /// «Дополнительно» раскрыто. Поле страницы, а не настройка: раскрытое состояние живёт до ухода
    /// со страницы и не сохраняется (план настроек §3.3). Переживает перестроение — иначе правка
    /// соседней строки схлопывала бы список под пальцем.
    /// </summary>
    private bool _advancedShown;

    /// <summary>
    /// Окна поверх экрана — правка значения, выбор варианта, меню строки, ответ действия. Держатся
    /// здесь и закрываются в <see cref="OnDestroy"/>: экран пересоздаётся от поворота телефона и от
    /// смены темы, а брошенное окно переживает свою активность и течёт вместе с ней (дамп владельца
    /// 10.08.2026).
    /// </summary>
    private readonly OwnedWindow _windows = new();

    /// <summary>
    /// Подписка на телеметрию — только у «Конфигурации колеса» и только пока страница на виду
    /// (план 34, шаг 2.6). Остальным пяти страницам она не нужна: их значения меняет человек, и
    /// перестроение уже стоит после каждой правки.
    /// </summary>
    private IDisposable? _liveSettings;

    /// <summary>
    /// Время снимка, по которому собран нынешний список. Мерка «пришло ли что-то новое»: колесо шлёт
    /// телеметрию много чаще, чем страницу настроек (Veteran — раз в 4 секунды на шестнадцать
    /// кадров), и перестраивать список на каждый кадр значило бы дёргать экран впустую сорок раз
    /// из сорока одного.
    /// </summary>
    private DateTimeOffset? _shownAt;

    /// <summary>
    /// Состояние «Конфигурации колеса», по которому собран нынешний экран (план 34 §5). Держится
    /// затем же, зачем <see cref="_shownAt"/>: сторож тикает раз в секунду, а перестраивать список
    /// надо только когда ответ раздела стал другим.
    /// </summary>
    private WheelSettingsView _shownView = WheelSettingsView.Waiting;

    /// <summary>
    /// С какого мгновения страница ждёт ответа колеса — от появления экрана. Отсюда отсчитываются
    /// десять секунд молчания, пока снимка не было вовсе (<see cref="WheelSettingsState.Silence"/>).
    /// </summary>
    private DateTimeOffset _watchingSince;

    /// <summary>
    /// Сторож молчания: связь может остаться живой, а кадр настроек — перестать приходить, и тогда
    /// нового кадра, от которого перестроиться, не будет по определению. Раз в секунду, пока
    /// страница на виду, и только у «Конфигурации колеса».
    /// </summary>
    private readonly Handler _silenceWatch = new(Looper.MainLooper!);
    private readonly Action _tick;
    private const int SilenceTickMs = 1000;

    private TimeProvider _clock = null!;

    public SettingsCategoryActivity() => _tick = Tick;

    protected override void OnDestroy()
    {
        _windows.Close();
        base.OnDestroy();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Заголовок ставится кодом, а не атрибутом [Activity]: в атрибут можно положить только
        // константу, а строки живут в ресурсах (план 6, фаза 1 — вшитых строк не осталось).
        Title = string.Format(CultureInfo.CurrentCulture, AppStrings.ScreenTitleFormat, AppStrings.SettingsTitle);

        _binder = MainApplication.Services.GetRequiredService<SettingsBinder>();
        _session = MainApplication.Services.GetRequiredService<WheelSession>();
        _clock = MainApplication.Services.GetRequiredService<TimeProvider>();
        _wheel = MainApplication.Services.GetRequiredService<IOptions<WheelOptions>>().Value;
        _viewScope = _wheel.Address;
        _page = (SettingsPage)(Intent?.GetIntExtra(ExtraPage, (int)SettingsPage.Application) ?? (int)SettingsPage.Application);
        _pendingSection = Intent?.GetStringExtra(ExtraSection);
        _pendingKey = Intent?.GetStringExtra(ExtraKey);

        Title = TranslateExtension.Get(PageTitleKey(_page));

        var root = BuildLayout();
        SetContentView(root);
        EdgeToEdge.Apply(this, root);
    }

    protected override void OnStart()
    {
        base.OnStart();

        // Смотреть можно общий слой да слой выбранного колеса, третьего не бывает: колесо сменили,
        // пока страница лежала в стопке, — взгляд переезжает вместе с ним, а не остаётся на MAC,
        // которого в приложении больше нет.
        if (_viewScope.Length > 0) _viewScope = _wheel.Address;

        // Ожидание отсчитывается от появления экрана, а не от создания активности: вернувшись на
        // страницу через час, человек спрашивает колесо заново, и приговор «значения не получены»
        // должен быть о нынешнем разговоре.
        _watchingSince = _clock.GetUtcNow();

        Rebuild();
        RevealPending();
        WatchWheel();
        WatchSilence();
    }

    /// <summary>
    /// Подписка живёт ровно столько, сколько страница на виду: снятая только в <c>OnDestroy</c>, она
    /// перестраивала бы разметку экрана, которого никто не смотрит, и держала бы страницу живой в
    /// стопке.
    /// </summary>
    protected override void OnStop()
    {
        _liveSettings?.Dispose();
        _liveSettings = null;
        _silenceWatch.RemoveCallbacks(_tick);
        base.OnStop();
    }

    /// <summary>
    /// Слушать колесо — только «Конфигурации колеса»: это единственная страница, значения которой
    /// меняет не человек, а само колесо, и без подписки её числа замерли бы на первом кадре
    /// (план 34, шаг 2.6).
    /// <para>
    /// Кадр приходит с потока разбора, поэтому решение выносится на поток разметки: <c>Rebuild</c>
    /// собирает вью, а их с чужого потока не трогают.
    /// </para>
    /// </summary>
    private void WatchWheel()
    {
        if (_page != SettingsPage.WheelDevice || _liveSettings is not null) return;

        _liveSettings = _session.Telemetry.Subscribe(frame => RunOnUiThread(() => ShowFreshSettings(frame)));
    }

    /// <summary>
    /// Три состояния раздела из четырёх наступают <b>без всякого кадра</b>: связь оборвалась, кадр
    /// не пришёл за десять секунд, показанный снимок состарился (план 34 §5). Подписка на
    /// телеметрию о них не узнает по определению — узнаёт этот сторож, тикающий раз в секунду.
    /// <para>
    /// Секунда выбрана не ради точности вердикта, а ради его своевременности: он сам о десяти
    /// секундах, и опоздание на секунду ничего не стоит, тогда как реже — экран сколько-то времени
    /// показывал бы устаревшее.
    /// </para>
    /// </summary>
    private void WatchSilence()
    {
        if (_page != SettingsPage.WheelDevice) return;

        _silenceWatch.RemoveCallbacks(_tick);
        _silenceWatch.PostDelayed(_tick, SilenceTickMs);
    }

    private void Tick()
    {
        // Перестроение только на смене ответа: пока состояние прежнее, тикать экрану нечем — числа
        // обновляет подписка на телеметрию.
        if (!_windows.IsOpen && WheelDeviceSection.Resolve(_session, _watchingSince, _clock) != _shownView) Rebuild();

        _silenceWatch.PostDelayed(_tick, SilenceTickMs);
    }

    /// <summary>
    /// Перестроить список — <b>только на новом снимке настроек и только при закрытых окнах</b>.
    /// <para>
    /// Первое условие: страница настроек едет отдельным кадром раз в 4 секунды, а телеметрия — куда
    /// чаще, и <see cref="WheelSettingsSnapshot.ReceivedAt"/> у неприкосновенного снимка тот же
    /// самый. Сравнивать сами значения не нужно: снимок целиком заменяется следующим кадром и
    /// временем отвечает за всё своё содержимое.
    /// </para>
    /// <para>
    /// Второе: перестроение сносит разметку под открытым листом значения, а лист живёт своим окном
    /// и переживёт своего хозяина осиротевшим. Пропущенный кадр не потеря — следующий придёт через
    /// те же 4 секунды.
    /// </para>
    /// </summary>
    private void ShowFreshSettings(TelemetrySnapshot frame)
    {
        if (frame.WheelSettings?.ReceivedAt is not { } received || received == _shownAt) return;
        if (_windows.IsOpen) return;

        Rebuild();
    }

    /// <summary>
    /// Встать на том, ради чего страницу открыли: прокрутить к разделу и подсветить строку. Через
    /// <c>Post</c>, потому что у только что собранной разметки координат ещё нет — якорь раздела
    /// стоит в нуле. Срабатывает один раз: вернувшись на страницу позже, человек остаётся там, где
    /// прокрутил сам.
    /// </summary>
    private void RevealPending()
    {
        if (_pendingSection is not { Length: > 0 } section) return;

        string? key = _pendingKey;
        _pendingSection = null;
        _pendingKey = null;

        _scroll.Post(() =>
        {
            ScrollToSection(section);
            if (key is { Length: > 0 } && _rowCards.TryGetValue(key, out var card)) Highlight(card);
        });
    }

    /// <summary>
    /// Вспышка карточки: цвет гаснет сам, чтобы подсветка не осталась вторым «переопределено». Роль
    /// спрашивается у контекста самой карточки — метод статический, своего под рукой нет.
    /// </summary>
    private static void Highlight(View card)
    {
        card.SetBackgroundColor(card.Context!.Highlight());
        card.PostDelayed(() => card.SetBackgroundColor(Color.Transparent), HighlightMs);
    }

    /// <summary>
    /// Открыть страницу настроек на нужной строке — общий вход, которым ходят ссылки между строками
    /// (план 30 §4) и который остаётся свободным для поиска по настройкам. Цель на этой же
    /// странице — не новая Activity, а прокрутка на месте.
    /// </summary>
    private void Reveal(SettingDescriptor target)
    {
        if (target.Page == _page)
        {
            _pendingSection = target.SectionKey;
            _pendingKey = target.Key;
            RevealPending();
            return;
        }

        var intent = new Intent(this, typeof(SettingsCategoryActivity));
        intent.PutExtra(ExtraPage, (int)target.Page);
        intent.PutExtra(ExtraSection, target.SectionKey);
        intent.PutExtra(ExtraKey, target.Key);
        StartActivity(intent);
    }

    /// <summary>
    /// Прослушивание не переживает страницу: звук, оставшийся играть в кармане, — худшее, что может
    /// сделать экран настроек. Гасится на уходе, а не на закрытии, потому что до закрытия телефон
    /// успевает и погаснуть, и уехать.
    /// </summary>
    protected override void OnPause()
    {
        Silence();
        base.OnPause();
    }

    /// <summary>
    /// По всем описаниям, а не по строкам этой страницы: строка могла стать невидимой ровно тем
    /// касанием, из-за которого мы сюда попали, — выключенный звук прячет и выбор, и прослушивание.
    /// </summary>
    private void Silence()
    {
        foreach (var descriptor in _binder.Descriptors)
        {
            if (descriptor.Page == _page && descriptor.Kind == SettingKind.Slider)
            {
                _binder.Set(descriptor, "0", _viewScope);
            }
        }
    }

    private static string PageTitleKey(SettingsPage page) => page switch
    {
        SettingsPage.Wheel => "SettingsPageWheel",
        SettingsPage.Warnings => "SettingsPageWarnings",
        SettingsPage.Display => "SettingsPageDisplay",
        SettingsPage.Experimental => "SettingsPageExperimental",
        SettingsPage.WheelDevice => "SettingsPageWheelDevice",
        _ => "SettingsPageApplication",
    };

    /// <summary>
    /// Пояснение над списком — есть только у «Тестовых функций» (план 28), и это не украшение:
    /// страница названа по зрелости, а не по теме, и без одной фразы её название читается как
    /// «здесь можно что-то сломать». Сказать надо ровно обратное — строки работают полностью,
    /// страница <b>только помечает</b>.
    /// <para>
    /// Второе — у «Конфигурации колеса»: список выглядит как всякий другой, а правится, в отличие
    /// от всякого другого, ничем. Молчание об этом человек истолковал бы единственным доступным
    /// способом — «не нажимается, значит сломано».
    /// </para>
    /// <para>
    /// У остальных четырёх пояснения нет: их названия говорят сами за себя, а надпись над каждым
    /// списком — это строка, которую перестают читать.
    /// </para>
    /// </summary>
    private static string? PageNoticeKey(SettingsPage page) => page switch
    {
        SettingsPage.Experimental => "SettingsPageExperimentalNotice",
        SettingsPage.WheelDevice => "SettingsPageWheelDeviceNotice",
        _ => null,
    };

    /// <summary>
    /// Что сказать, когда показывать нечего. У «Конфигурации колеса» пустота — не «настроек нет», а
    /// состояние разговора с колесом, и слово у каждого состояния своё (план 34 §5): нет связи,
    /// колесо чужой марки, прошивка настроек не сообщает, ответа нет десять секунд. Серый список с
    /// пустыми значениями не показывается ни в одном из них — он обещал бы то, чего колесо не
    /// подтверждало (решение владельца, план 34 §12 п.1).
    /// <para>
    /// <c>null</c> — сказать пока нечего: первые секунды ожидания раздел молчит, потому что кадр
    /// идёт раз в 4 секунды и «значения не получены» на первой из них было бы не ответом, а
    /// нетерпением.
    /// </para>
    /// </summary>
    private static string? EmptyTextKey(SettingsPage page, WheelSettingsView view) =>
        page == SettingsPage.WheelDevice ? WheelDeviceSection.TextKey(view) : "SettingsEmpty";

    /// <summary>
    /// Причина, по которой настроек колеса не показать, — в журнал. Наружу у всех случаев один
    /// ответ, и разобрать жалобу «у меня пусто» без этой строки нечем.
    /// </summary>
    private static void LogSettingsUnavailable(WheelSettingsView view)
    {
        try
        {
            MainApplication.Services.GetRequiredService<ILogger<SettingsCategoryActivity>>()
                .LogInformation("{Event} — настройки колеса не показаны: {Reason} (состояние {View})",
                    LogEvents.WheelSettings.UnavailableName, WheelDeviceSection.Reason(view), view);
        }
        catch (Exception)
        {
            // Контейнера может не быть (экран поднят раньше приложения). Ронять показ из-за строки
            // журнала нельзя — она тут ради разбора, а не ради работы.
        }
    }

    // ---- Scope (общее / это колесо) ----------------------------------------------------------

    /// <summary>
    /// "Общее" and the wheel already selected — not a list of every wheel ever seen (variant B's
    /// deliberate cut from variant A, settings-redesign.md §4 "Цена"): смотреть можно только общий
    /// слой да слой выбранного колеса, и оба известны без поиска.
    /// <para>
    /// У «Конфигурации колеса» переключателя нет вовсе (план 34, шаг 2.5): её значения приходят от
    /// колеса и в наши слои не пишутся ни в какой области, так что переключать нечего. Долгое
    /// нажатие на строку там тоже молчит, и это не второй запрет, а тот же самый — лист «Где
    /// хранить» отсекает <see cref="HasLayers"/> по признаку
    /// <see cref="SettingDescriptor.ReportedByWheel"/>.
    /// </para>
    /// </summary>
    private View? BuildScopeRow()
    {
        if (_page == SettingsPage.WheelDevice) return null;

        var container = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        container.SetGravity(GravityFlags.CenterVertical);
        int padH = this.Dp(16);
        container.SetPadding(padH, this.Dp(10), padH, this.Dp(6));

        if (_wheel.Address.Length == 0)
        {
            // Ни одно колесо ещё не выбрано (первый запуск, до первого скана) — переключать
            // нечего, показывается то же, что раньше говорила пассивная надпись ScopeLabel.
            var onlyGlobal = new TextView(this) { Text = AppStrings.SettingsScopeGlobal };
            onlyGlobal.SetTextSize(ComplexUnitType.Sp, 13);
            onlyGlobal.Alpha = 0.7f;
            container.AddView(onlyGlobal);
            return container;
        }

        var group = new MaterialButtonToggleGroup(this) { SingleSelection = true, SelectionRequired = true };

        // Кегль 14sp и цель 48 dp (план настроек §3.1): двенадцатым читалось хуже, чем строки под
        // ним, а по высоте переключатель не дотягивал до наименьшей цели касания.
        _globalButton = new MaterialButton(this) { Text = AppStrings.SettingsLayerGlobal };
        _globalButton.SetTextSize(ComplexUnitType.Sp, 14);
        _globalButton.Click += (_, _) => SetScope(LayeredSettings.GlobalScope);
        group.AddView(_globalButton, new LinearLayout.LayoutParams(0, this.Dp(48), 1f));

        // «Колесо C8:3E», а не весь MAC: полный адрес занимал половину переключателя, а различают
        // колёса по первым парам не хуже.
        _wheelButton = new MaterialButton(this) { Text = WheelLayerName() };
        _wheelButton.SetTextSize(ComplexUnitType.Sp, 14);
        _wheelButton.Click += (_, _) => SetScope(_wheel.Address);
        group.AddView(_wheelButton, new LinearLayout.LayoutParams(0, this.Dp(48), 1f));

        container.AddView(group, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return container;
    }

    /// <summary>
    /// Переключить <b>взгляд</b>. Живым объектам от этого ни холодно ни жарко: они разрешаются по
    /// колесу всегда, и правка, сделанная в «Общем» поверх переопределения, честно не меняет ничего
    /// на дороге — рамка «переопределено» на строке это и объясняет (план 29 §29.3).
    /// </summary>
    private void SetScope(string scope)
    {
        _viewScope = scope;
        ShowScope();
        Rebuild();
    }

    private void ShowScope()
    {
        if (_globalButton is null || _wheelButton is null) return;

        bool onWheel = _viewScope.Length > 0;
        if (_globalButton.Checked == !onWheel && _wheelButton.Checked == onWheel) return;

        _globalButton.Checked = !onWheel;
        _wheelButton.Checked = onWheel;
    }

    // ---- Quick-jump chips ---------------------------------------------------------------------

    /// <summary>
    /// Полоса быстрого перехода по разделам — <b>закреплённая над списком, вне прокрутки</b>
    /// (вариант B, <c>docs/archive/settings-redesign.md</c>): её смысл в том и есть, что она
    /// доступна из любого места длинной страницы.
    /// <para>
    /// Раздаётся не одному «Отображению», как раньше, а всякой странице, где разделов больше трёх
    /// (план 30 §5): три помещаются на экран и без полосы, а на четвёртом начинается перелистывание
    /// вслепую. Третьего уровня меню это заменяет — переход к разделу без второго нажатия.
    /// </para>
    /// </summary>
    private View? BuildSectionChips()
    {
        var sections = _binder.Page(_page, _viewScope).Select(section => section.Key).Distinct().ToList();
        if (sections.Count <= 3) return null;

        var outer = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        int padH = this.Dp(16);
        outer.SetPadding(padH, 0, padH, this.Dp(4));

        var caption = new TextView(this) { Text = AppStrings.SettingsSectionsHint };
        caption.SetTextSize(ComplexUnitType.Sp, 11);
        caption.Alpha = 0.55f;
        outer.AddView(caption);

        var scrollX = new HorizontalScrollView(this);
        scrollX.HorizontalScrollBarEnabled = false;

        // Без явного «в одну строку»: у ChipGroup внутри HorizontalScrollView и так нет предела
        // ширины, чтобы переносить чипы на вторую строку — переносить их некуда.
        var group = new ChipGroup(this);
        foreach (string sectionKey in sections)
        {
            var chip = new Chip(this) { Text = TranslateExtension.Get(sectionKey) };
            chip.SetTextSize(ComplexUnitType.Sp, 12);
            chip.Clickable = true;
            chip.CheckedIconVisible = false;
            chip.Click += (_, _) => ScrollToSection(sectionKey);
            group.AddView(chip);
        }

        scrollX.AddView(group);
        outer.AddView(scrollX, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = this.Dp(2),
        });

        return outer;
    }

    private void ScrollToSection(string sectionKey)
    {
        if (_sectionAnchors.TryGetValue(sectionKey, out var anchor)) _scroll.SmoothScrollTo(0, anchor.Top);
    }

    // ---- The list itself, rebuilt on every edit (same reason as the MAUI original's Show()) -----

    private void Rebuild()
    {
        // Метка ставится ДО чтения строк: кадр, пришедший посреди сборки, вызовет тогда лишнее
        // перестроение, а не потеряется молча. Лишнее дешевле пропущенного.
        _shownAt = _session.LastSnapshot?.WheelSettings?.ReceivedAt;

        // Чем раздел отвечает прямо сейчас (план 34 §5). У прочих пяти страниц вопроса нет: их
        // значения лежат в наших слоях и никуда не деваются от обрыва связи.
        _shownView = _page == SettingsPage.WheelDevice
            ? WheelDeviceSection.Resolve(_session, _watchingSince, _clock)
            : WheelSettingsView.Values;
        bool showValues = WheelDeviceSection.ShowsValues(_shownView);

        // Ползунки прослушивания рисуются с нуля, значит и звучать после перестроения нечему.
        Silence();

        ShowScope();

        _content.RemoveAllViews();
        _sectionAnchors.Clear();
        _rowCards.Clear();

        // Внутри списка, а не над ним: прочитанное однажды пояснение должно уезжать вверх вместе с
        // прокруткой, а не занимать экран у каждой строки. Над пустым списком пояснения нет:
        // «так колесо настроено сейчас» говорят о значениях, а не об их отсутствии.
        if (showValues && PageNoticeKey(_page) is { } noticeKey)
        {
            var notice = new TextView(this) { Text = TranslateExtension.Get(noticeKey) };
            notice.SetTextSize(ComplexUnitType.Sp, 13);
            notice.Alpha = 0.75f;
            _content.AddView(notice, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = this.Dp(4),
            });
        }

        // Разделы — карточками, «дополнительные» — отдельной стопкой под свёрнутой строкой
        // (план настроек §3.2 и §3.3). Порядок и состав разделов прежние, из каталога.
        //
        // Строки «Конфигурации колеса» читают последний снимок сессии, и он переживает и обрыв
        // связи, и смену колеса. Поэтому список собирается ТОЛЬКО когда снимок свеж: иначе
        // вчерашние значения молча остались бы на экране за сегодняшние — то самое, чего этап 3 и
        // не допускает (план 34 §5).
        List<IGrouping<string, SettingDescriptor>> sections = showValues ? _binder.Page(_page, _viewScope).ToList() : [];
        var plain = sections.Where(section => !section.First().Advanced).ToList();
        var advanced = sections.Where(section => section.First().Advanced).ToList();

        foreach (var section in plain) _content.AddView(SectionCard(section), CardParams());

        if (advanced.Count > 0)
        {
            _content.AddView(AdvancedRow(advanced), CardParams());

            if (_advancedShown)
            {
                foreach (var section in advanced) _content.AddView(SectionCard(section), CardParams());
            }
        }

        if (sections.Count == 0 && EmptyTextKey(_page, _shownView) is { } emptyKey)
        {
            var empty = new TextView(this) { Text = TranslateExtension.Get(emptyKey) };
            empty.Alpha = 0.7f;
            _content.AddView(empty);

            // Человеку — одно «настройки недоступны», журналу — почему именно (решение владельца
            // 16.08.2026). Пишется на смене состояния, а не на каждой отрисовке: сюда приходят
            // только после Rebuild, а он идёт лишь когда состояние сменилось.
            if (_page == SettingsPage.WheelDevice) LogSettingsUnavailable(_shownView);
        }
    }

    private LinearLayout.LayoutParams CardParams() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(14) };

    /// <summary>
    /// Раздел — карточка с заголовком и строками через черту, а не жирная надпись посреди списка
    /// (план настроек §3.2). Заголовок был границей только на словах: границы у него не было, и на
    /// «Отображении» двадцать строк читались одной стеной.
    /// </summary>
    private View SectionCard(IGrouping<string, SettingDescriptor> section)
    {
        var card = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        card.Background = CardBackground();

        var header = new TextView(this) { Text = TranslateExtension.Get(section.Key).ToUpperInvariant() };
        header.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        header.SetTextSize(ComplexUnitType.Sp, 13);
        header.SetTextColor(this.TextSecondary());
        header.LetterSpacing = 0.09f;
        header.SetPadding(this.Dp(16), this.Dp(12), this.Dp(16), this.Dp(8));
        card.AddView(header);

        // Якорь прокрутки — сама карточка, а не заголовок внутри неё: чипы ведут к разделу, и
        // встать он должен своим верхним краем, а не серединой.
        _sectionAnchors[section.Key] = card;

        foreach (var descriptor in section)
        {
            card.AddView(RowLine(), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, this.Dp(1)));
            card.AddView(BuildRow(descriptor));
        }

        return card;
    }

    /// <summary>Черта между строками карточки. Своя, а не общая <c>UiKit.Divider</c>: у той свой цвет и своя альфа, а здесь линия задана макетом.</summary>
    private View RowLine()
    {
        var line = new View(this);
        line.SetBackgroundColor(this.RowDivider());
        return line;
    }

    /// <summary>
    /// «Дополнительно · N настроек» — одна свёрнутая строка вместо черты с подписью посреди списка:
    /// та делила список, но ничего не прятала (план настроек §3.3). Раскрытое состояние живёт до
    /// ухода со страницы и переживает перестроение.
    /// </summary>
    private View AdvancedRow(List<IGrouping<string, SettingDescriptor>> advanced)
    {
        int count = advanced.Sum(section => section.Count());

        var row = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(this.Dp(16), this.Dp(15), this.Dp(16), this.Dp(15));
        row.Background = FrameBackground();
        row.Clickable = true;
        row.Click += (_, _) =>
        {
            _advancedShown = !_advancedShown;
            Rebuild();
        };

        var words = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };

        var title = new TextView(this) { Text = AppStrings.SettingsAdvanced };
        title.SetTextSize(ComplexUnitType.Sp, 17);
        title.SetTextColor(this.TextTitle());
        words.AddView(title);

        var howMany = new TextView(this)
        {
            Text = Plural.Of(count,
                AppStrings.SettingsSummaryCount1, AppStrings.SettingsSummaryCount2, AppStrings.SettingsSummaryCount5),
        };
        howMany.SetTextSize(ComplexUnitType.Sp, 13);
        howMany.SetTextColor(this.Hint());
        words.AddView(howMany, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = this.Dp(4),
        });

        row.AddView(words, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var chevron = new TextView(this) { Text = _advancedShown ? "⌃" : "⌄" };
        chevron.SetTextSize(ComplexUnitType.Sp, 18);
        chevron.SetTextColor(this.Hint());
        row.AddView(chevron);

        return row;
    }

    private Drawable CardBackground()
    {
        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Rectangle);
        drawable.SetCornerRadius(this.Dp(14));
        drawable.SetColor(this.Card());
        drawable.SetStroke(this.Dp(1), this.CardBorder());
        return drawable;
    }

    /// <summary>Рамка без заливки — у свёрнутой строки «Дополнительно»: она не раздел, и весом выглядеть как раздел не должна.</summary>
    private Drawable FrameBackground()
    {
        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Rectangle);
        drawable.SetCornerRadius(this.Dp(14));
        drawable.SetStroke(this.Dp(1), this.CardBorder());
        return drawable;
    }

    /// <summary>
    /// One row: title and editor on top, then a layer/range line and, if there is one, the hint —
    /// both visible without a tap (settings-redesign.md §4, П3/П4/П6). The row menu (⋮) still needs a
    /// tap, but only reaching it does, not seeing that the value is not the default.
    /// </summary>
    private View BuildRow(SettingDescriptor descriptor)
    {
        var resolved = _binder.Read(descriptor, _viewScope);

        var card = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        card.SetPadding(this.Dp(16), this.Dp(12), this.Dp(16), this.Dp(14));

        var top = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        top.SetGravity(GravityFlags.CenterVertical);

        // Зависимая строка стоит с отбивкой и чертой слева: видно, чья она (план настроек §3.4).
        // Сама логика видимости не меняется — это только вид.
        if (descriptor.IsVisible is not null)
        {
            top.SetPadding(this.Dp(14), 0, 0, 0);
            top.Background = DependantEdge();
        }

        bool inPlace = EditsInPlace(descriptor);

        var words = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };

        var title = new TextView(this) { Text = TranslateExtension.Get(descriptor.LabelKey) };
        title.SetTextSize(ComplexUnitType.Sp, 17);
        title.SetTextColor(UiKit.PlainText(this));
        words.AddView(title);

        // У строки, правящейся на месте, подсказка уезжает под ползунок, к диапазону: наверху её
        // место занимают «−», значение и «+» (макет 2b).
        if (!inPlace && descriptor.HintKey is { } hintKey)
        {
            var hint = new TextView(this) { Text = TranslateExtension.Get(hintKey) };
            hint.SetTextSize(ComplexUnitType.Sp, 13);
            hint.SetTextColor(this.Hint());
            words.AddView(hint, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = this.Dp(4),
            });
        }

        top.AddView(words, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        // Ползунок прослушивания ведут пальцем во всю его длину, поэтому он идёт отдельной строкой
        // под подписью, а не в узкую колонку справа, где живут значения.
        bool ownRow = descriptor.Kind == SettingKind.Slider;

        if (inPlace) AddInPlaceNumber(card, top, descriptor, resolved);
        else if (!ownRow && !ShowsChoiceButtons(descriptor)) top.AddView(BuildEditor(descriptor, resolved));

        card.AddView(top, 0);

        if (ownRow)
        {
            card.AddView(BuildEditor(descriptor, resolved), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = this.Dp(4),
            });
        }

        // Выбор из двух-трёх показывается сразу кнопками под подписью — без диалога (план §3.5).
        if (ShowsChoiceButtons(descriptor))
        {
            card.AddView(ChoiceButtons(descriptor, resolved), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = this.Dp(10),
            });
        }

        // Предупреждение — под подсказкой и цветом: подсказка объясняет настройку всегда, а это
        // говорит о введённом числе и появляется, только если с ним что-то не так. Отменять выбор
        // человека мы не вправе, сказать о нём — обязаны.
        if (descriptor.Warning?.Invoke() is { Length: > 0 } warning)
        {
            var note = new TextView(this) { Text = warning };
            note.SetTextSize(ComplexUnitType.Sp, 12);
            note.SetTextColor(this.Warning());
            card.AddView(note, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = this.Dp(2),
            });
        }

        if (BuildRelatedLinks(descriptor) is { } links) card.AddView(links);

        // Слой меняется долгим нажатием по всей строке, а не только по окну значения: у
        // переключателя и у выбора кнопками окна значения нет вовсе, а меню «⋮» ушло (план §4.3).
        if (HasLayers(descriptor))
        {
            card.LongClick += (_, e) =>
            {
                ShowStorageSheet(descriptor, resolved);
                e.Handled = true;
            };
        }

        _rowCards[descriptor.Key] = card;
        return card;
    }

    /// <summary>
    /// Ссылки на связанные настройки — последней строкой карточки, ниже подсказки и предупреждения
    /// (план 30 §4): сперва «что это», потом «что с ним связано». Отдельной строкой списка ссылка не
    /// становится — одно значение остаётся одной строкой.
    /// <para>
    /// Отсеивание — на биндере (<see cref="SettingsBinder.RelatedTo"/>): ссылка на строку, которой
    /// в этой области сейчас нет, никуда не ведёт.
    /// </para>
    /// </summary>
    private View? BuildRelatedLinks(SettingDescriptor descriptor)
    {
        var targets = _binder.RelatedTo(descriptor, _viewScope).ToList();
        if (targets.Count == 0) return null;

        var row = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        row.SetPadding(0, this.Dp(4), 0, 0);

        foreach (var target in targets)
        {
            var link = new TextView(this) { Text = $"→ {TranslateExtension.Get(target.LabelKey)}" };
            link.SetTextSize(ComplexUnitType.Sp, 12);
            link.SetTextColor(this.Link());
            link.SetPadding(0, 0, this.Dp(16), 0);
            link.Clickable = true;
            link.Click += (_, _) => Reveal(target);
            row.AddView(link);
        }

        return row;
    }

    /// <summary>
    /// Правится ли строка на месте: «−», значение, «+» и ползунок прямо в строке (план §4.1).
    /// Мерка — сколько шагов в диапазоне: до сотни значение перебирается кнопкой за разумное время,
    /// дальше нужен лист с полем ввода. Смена порога стоила четырёх касаний, стала одного.
    /// </summary>
    private static bool EditsInPlace(SettingDescriptor descriptor) =>
        descriptor.Kind == SettingKind.Number
        && !descriptor.ReportedByWheel
        && descriptor.Step > 0
        && (descriptor.Maximum - descriptor.Minimum) / descriptor.Step <= 100;

    /// <summary>
    /// Правка числа на месте: кластер «− значение +» встаёт справа от подписи, ползунок и строка
    /// «подсказка · диапазон» — под ними, во всю ширину карточки.
    /// <para>
    /// Кнопки пишут сразу (<see cref="Commit"/>, то есть с перестроением), <b>ползунок — по
    /// отпусканию</b>: перестроение на каждый шаг оборвало бы сам жест. Это та же оговорка, что
    /// сделана для прослушивания, только там <c>Set</c> идёт вовсе мимо перестроения.
    /// </para>
    /// </summary>
    private void AddInPlaceNumber(LinearLayout card, LinearLayout top, SettingDescriptor descriptor, ResolvedSetting resolved)
    {
        double current = Math.Clamp(
            SettingsFormat.ParseNumber(resolved.Value ?? descriptor.Current()), descriptor.Minimum, descriptor.Maximum);
        double span = descriptor.Maximum - descriptor.Minimum;
        int steps = Math.Max(1, (int)Math.Round(span / descriptor.Step));

        var readout = new TextView(this) { Text = ValueWord(descriptor, current), Gravity = GravityFlags.Center };
        readout.SetTextSize(ComplexUnitType.Sp, 19);
        readout.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        readout.SetTextColor(resolved.IsOverridden ? this.Override() : UiKit.PlainText(this));
        readout.SetMinimumWidth(this.Dp(64));

        // Слой у такой строки меняется долгим нажатием на значение — тем же листом, но без ползунка
        // (план §4.3): меню «⋮» ушло, а место для ряда «Где хранить» в строке взять негде.
        readout.Clickable = true;
        readout.LongClick += (_, e) =>
        {
            ShowStorageSheet(descriptor, resolved);
            e.Handled = true;
        };

        var cluster = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        cluster.SetGravity(GravityFlags.CenterVertical);
        cluster.AddView(StepButton("−", () => StepBy(descriptor, current, -1)));
        cluster.AddView(readout, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            LeftMargin = this.Dp(10),
            RightMargin = this.Dp(10),
        });
        cluster.AddView(StepButton("+", () => StepBy(descriptor, current, +1)));
        top.AddView(cluster);

        var slider = new SeekBar(this)
        {
            Max = steps,
            Progress = span > 0 ? (int)Math.Round((current - descriptor.Minimum) / span * steps) : 0,
        };
        slider.SetMinimumHeight(this.Dp(48));
        slider.ProgressChanged += (_, e) =>
        {
            if (e.FromUser) readout.Text = ValueWord(descriptor, At(descriptor, e.Progress, steps));
        };
        slider.StopTrackingTouch += (_, e) =>
            Commit(descriptor, SettingsFormat.Store(descriptor, At(descriptor, e.SeekBar?.Progress ?? 0, steps)));

        card.AddView(slider, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = this.Dp(6),
        });

        var footer = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };

        var hint = new TextView(this)
        {
            Text = descriptor.HintKey is { } hintKey ? TranslateExtension.Get(hintKey) : "",
        };
        hint.SetTextSize(ComplexUnitType.Sp, 13);
        hint.SetTextColor(this.Hint());
        footer.AddView(hint, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var range = new TextView(this) { Text = RangeText(descriptor) };
        range.SetTextSize(ComplexUnitType.Sp, 13);
        range.SetTextColor(this.Hint());
        footer.AddView(range, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            LeftMargin = this.Dp(10),
        });

        card.AddView(footer, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = this.Dp(6),
        });
    }

    /// <summary>Значение под этим делением ползунка — округлённое по шагу и прижатое к границам.</summary>
    private static double At(SettingDescriptor descriptor, int progress, int steps)
    {
        double span = descriptor.Maximum - descriptor.Minimum;
        double raw = descriptor.Minimum + (double)progress / steps * span;
        return Math.Clamp(SettingsFormat.Snap(descriptor, raw), descriptor.Minimum, descriptor.Maximum);
    }

    private void StepBy(SettingDescriptor descriptor, double current, int direction)
    {
        double next = Math.Clamp(
            SettingsFormat.Snap(descriptor, current + direction * descriptor.Step), descriptor.Minimum, descriptor.Maximum);
        Commit(descriptor, SettingsFormat.Store(descriptor, next));
    }

    /// <summary>Квадратная кнопка шага. 38 dp — цель касания у ± по макету; ползунку своя, 48 dp.</summary>
    private View StepButton(string sign, Action tapped)
    {
        var button = new TextView(this) { Text = sign, Gravity = GravityFlags.Center };
        button.SetTextSize(ComplexUnitType.Sp, 22);
        button.SetTextColor(this.TextControl());
        button.Clickable = true;
        button.Click += (_, _) => tapped();
        button.Background = Framed(this.Dp(9), this.Dp(1), this.Border());
        button.LayoutParameters = new LinearLayout.LayoutParams(this.Dp(38), this.Dp(38));
        return button;
    }

    /// <summary>
    /// Чем сказано значение: числом с единицей либо словом «выкл.», если у ручки ноль выключает
    /// (<see cref="SettingDescriptor.ZeroDisables"/>). Правило одно на все шесть таких ручек и живёт
    /// в дескрипторе, а не списком исключений в разметке (план §3.4).
    /// </summary>
    private static string ValueWord(SettingDescriptor descriptor, double value) =>
        descriptor.ZeroDisables && value == 0 ? AppStrings.SettingsValueOff : SettingsFormat.Display(descriptor, value);

    private static string RangeText(SettingDescriptor descriptor)
    {
        string range = $"{SettingsFormat.Store(descriptor, descriptor.Minimum)}–{SettingsFormat.Store(descriptor, descriptor.Maximum)}";
        return descriptor.UnitKey is { } unit ? $"{range} {TranslateExtension.Get(unit)}" : range;
    }

    /// <summary>
    /// Отбивка зависимой строки: черта слева во всю её высоту. Обводка нужна одна, левая, — три
    /// остальные уводятся за край вставкой, потому что своей «границы слева» у
    /// <see cref="GradientDrawable"/> нет.
    /// </summary>
    private Drawable DependantEdge()
    {
        var bar = new GradientDrawable();
        bar.SetShape(ShapeType.Rectangle);
        bar.SetColor(Color.Transparent);
        bar.SetStroke(this.Dp(2), this.DependantBar());
        return new InsetDrawable(bar, 0, -this.Dp(2), -this.Dp(2), -this.Dp(2));
    }

    private View BuildEditor(SettingDescriptor descriptor, ResolvedSetting resolved)
    {
        string value = resolved.Value ?? descriptor.Current();

        // Сообщённое колесом показывается и не правится — переехало бы обратно само на следующем
        // подключении и выглядело бы правкой, которой никто не делал (то же рассуждение, что в
        // WheelTalk.App/Pages/SettingsListPage.xaml.cs).
        if (descriptor.ReportedByWheel)
        {
            // По виду настройки, а не только «Да/Нет»: сообщённым приходят и числа (страница Veteran —
            // 94, 200, 145), и варианты. Показывается как есть, без границ ручки и без «выкл.».
            var reported = new TextView(this) { Text = SettingsFormat.ReportedText(descriptor, value) };
            reported.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
            reported.SetTextSize(ComplexUnitType.Sp, 14);
            return reported;
        }

        switch (descriptor.Kind)
        {
            case SettingKind.Toggle:
                var toggle = new Switch(this) { Checked = SettingsFormat.ParseBool(value) };
                toggle.CheckedChange += (_, e) => Commit(descriptor, e.IsChecked.ToString());
                return toggle;

            case SettingKind.Number:
            {
                double current = Math.Clamp(SettingsFormat.ParseNumber(value), descriptor.Minimum, descriptor.Maximum);
                var readout = Outlined(descriptor, ValueWord(descriptor, current), resolved, descriptor.ZeroDisables && current == 0);
                readout.Click += (_, _) => ShowNumberSheet(descriptor, resolved, current);
                return ValueBlock(readout, descriptor, resolved);
            }

            case SettingKind.Choice:
            {
                var readout = Outlined(descriptor, SettingsFormat.ChoiceLabel(descriptor, value), resolved, dashed: false);
                readout.Click += (_, _) => EditChoice(descriptor, value);
                return ValueBlock(readout, descriptor, resolved);
            }

            case SettingKind.Text:
            {
                var readout = Outlined(descriptor, value.Length > 0 ? value : AppStrings.SettingsTextEmpty, resolved, dashed: false);
                readout.Click += (_, _) => EditText(descriptor, value);
                return ValueBlock(readout, descriptor, resolved);
            }

            case SettingKind.Slider:
            {
                // Правит на ходу и мимо слоёв: строка не хранит значения, она даёт услышать.
                // Каждое движение — сразу в живой объект, поэтому Set, а не Commit: перестроение
                // страницы на каждый шаг ползунка стоило бы дороже самого звука и оборвало бы его.
                var live = new SeekBar(this) { Max = (int)descriptor.Maximum, Progress = 0 };
                live.ProgressChanged += (_, e) =>
                {
                    if (e.FromUser) _binder.Set(descriptor, e.Progress.ToString(CultureInfo.InvariantCulture), _viewScope);
                };
                return live;
            }

            case SettingKind.Note:
            {
                // Тот же вид, что у сообщённого колесом: жирная надпись без правки и без меню.
                var note = new TextView(this) { Text = descriptor.Current() };
                note.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
                note.SetTextSize(ComplexUnitType.Sp, 14);
                return note;
            }

            case SettingKind.Action:
                var button = UiKit.CreateButton(this, descriptor.ActionLabelKey is { } actionLabel
                    ? TranslateExtension.Get(actionLabel)
                    : AppStrings.SettingsRun);
                button.SetTextSize(ComplexUnitType.Sp, 12);
                button.Click += (_, _) => RunAction(descriptor, button);
                return button;

            default:
                return new TextView(this) { Text = value };
        }
    }

    /// <summary>
    /// Окно значения. Переопределённое — обводка 2 dp янтарём (подпись «своё» добавляет
    /// <see cref="ValueBlock"/>), заводское — тонкая серая, «выключено» — пунктир и приглушённое
    /// слово: рамка сама говорит, что число тут не работает (план §3.4).
    /// </summary>
    private TextView Outlined(SettingDescriptor descriptor, string text, ResolvedSetting resolved, bool dashed)
    {
        var view = new TextView(this) { Text = text };
        view.SetTextSize(ComplexUnitType.Sp, dashed ? 15 : 17);
        view.SetTypeface(dashed ? Typeface.Default : Typeface.DefaultBold, dashed ? TypefaceStyle.Normal : TypefaceStyle.Bold);
        view.SetTextColor(dashed ? this.Hint() : UiKit.PlainText(this));
        view.SetPadding(this.Dp(12), this.Dp(7), this.Dp(12), this.Dp(7));
        view.Clickable = true;
        view.Background = EditorBackground(resolved.IsOverridden, dashed);

        // Слой меняется долгим нажатием на само значение: меню «⋮» ушло, а лист без ползунка даёт
        // тот же ряд «Где хранить» (план §4.3).
        view.LongClick += (_, e) =>
        {
            ShowStorageSheet(descriptor, resolved);
            e.Handled = true;
        };

        return view;
    }

    /// <summary>Значение и, если оно своё, подпись «своё» под ним — тем же янтарём, что обводка.</summary>
    private View ValueBlock(View readout, SettingDescriptor descriptor, ResolvedSetting resolved)
    {
        if (!resolved.IsOverridden) return readout;

        var block = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        block.SetGravity(GravityFlags.End);
        block.AddView(readout);

        var own = new TextView(this) { Text = AppStrings.SettingsValueOwn };
        own.SetTextSize(ComplexUnitType.Sp, 12);
        own.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        own.SetTextColor(this.Override());
        block.AddView(own, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = this.Dp(5),
            Gravity = GravityFlags.End,
        });

        return block;
    }

    private Drawable EditorBackground(bool overridden, bool dashed)
    {
        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Rectangle);
        drawable.SetCornerRadius(this.Dp(9));

        if (dashed)
        {
            drawable.SetStroke(this.Dp(1), this.Border(), this.Dp(4), this.Dp(3));
            return drawable;
        }

        drawable.SetStroke(this.Dp(overridden ? 2 : 1), overridden ? this.Override() : this.Border());
        return drawable;
    }

    /// <summary>Выбор показывается кнопками, пока их не больше трёх: диалог ради двух вариантов — три касания вместо одного (план §3.5).</summary>
    private static bool ShowsChoiceButtons(SettingDescriptor descriptor) =>
        descriptor.Kind == SettingKind.Choice && !descriptor.ReportedByWheel && descriptor.Choices.Count is > 0 and <= 3;

    /// <summary>
    /// Варианты кнопками в строку. У палитры к подписи добавляются три её же цвета
    /// (<see cref="DashboardPalette.Calm"/>/<see cref="DashboardPalette.Caution"/>/<see cref="DashboardPalette.Danger"/>):
    /// имя «Ванг» ничего не говорит тому, кто их не видел (план §3.5).
    /// </summary>
    private View ChoiceButtons(SettingDescriptor descriptor, ResolvedSetting resolved)
    {
        string value = resolved.Value ?? descriptor.Current();

        var row = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };

        foreach (string choice in descriptor.Choices)
        {
            bool picked = choice == value;

            var button = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
            button.SetGravity(GravityFlags.Center);
            button.SetPadding(this.Dp(10), this.Dp(10), this.Dp(10), this.Dp(10));
            button.Background = Framed(this.Dp(10), this.Dp(picked ? 2 : 1), picked ? this.Accent() : this.Border());
            button.Clickable = true;
            button.Click += (_, _) => Commit(descriptor, choice);

            if (DashboardPalette.All.FirstOrDefault(palette => palette.Name == choice) is { } swatch)
            {
                button.AddView(Swatches(swatch), new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
                {
                    RightMargin = this.Dp(8),
                });
            }

            var label = new TextView(this) { Text = SettingsFormat.ChoiceLabel(descriptor, choice) };
            label.SetTextSize(ComplexUnitType.Sp, 15);
            label.SetTextColor(picked ? UiKit.PlainText(this) : this.TextMuted());
            if (picked) label.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
            button.AddView(label);

            row.AddView(button, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
            {
                LeftMargin = row.ChildCount > 0 ? this.Dp(8) : 0,
            });
        }

        return row;
    }

    /// <summary>Три полоски цветов палитры — то, чем она отличается от соседки, показанное, а не названное.</summary>
    private View Swatches(DashboardPalette palette)
    {
        var strip = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };

        foreach (var color in new[] { palette.Calm, palette.Caution, palette.Danger })
        {
            var swatch = new View(this);
            var fill = new GradientDrawable();
            fill.SetShape(ShapeType.Rectangle);
            fill.SetCornerRadius(this.Dp(2));
            fill.SetColor(color);
            swatch.Background = fill;
            strip.AddView(swatch, new LinearLayout.LayoutParams(this.Dp(12), this.Dp(20))
            {
                LeftMargin = strip.ChildCount > 0 ? this.Dp(3) : 0,
            });
        }

        return strip;
    }

    /// <summary>Скруглённая рамка без заливки — общий вид кнопок листа, шага и вариантов.</summary>
    private Drawable Framed(int radius, int stroke, Color color)
    {
        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Rectangle);
        drawable.SetCornerRadius(radius);
        drawable.SetStroke(stroke, color);
        return drawable;
    }

    /// <summary>
    /// The dialog the plan asks for: a slider AND an EditText, kept in step with each other — the
    /// slider makes coarse edits fast, the field makes exact ones possible, same as the plan's
    /// Строка правится тем же диалогом, что и число, только без ползунка и границ: у текста их нет.
    /// Пустое значение — законное («имя колеса не задано, зовём как объявляется по Bluetooth»),
    /// поэтому очистить поле можно, и это не ошибка ввода.
    /// </summary>
    private void EditText(SettingDescriptor descriptor, string current)
    {
        var container = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        int padH = this.Dp(20);
        container.SetPadding(padH, this.Dp(8), padH, 0);

        if (descriptor.HintKey is { } hintKey)
        {
            var hint = new TextView(this) { Text = TranslateExtension.Get(hintKey) };
            hint.SetTextSize(ComplexUnitType.Sp, 13);
            hint.Alpha = 0.75f;
            container.AddView(hint, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                BottomMargin = this.Dp(8),
            });
        }

        var input = new EditText(this) { Text = current };
        input.InputType = InputTypes.ClassText;
        input.SetSingleLine(true);
        container.AddView(input, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        _windows.Show(new AlertDialog.Builder(this)!
            .SetTitle(TranslateExtension.Get(descriptor.LabelKey))!
            .SetView(container)!
            .SetPositiveButton(AppStrings.SettingsAccept, (_, _) => Commit(descriptor, input.Text?.Trim() ?? ""))!
            .SetNegativeButton(AppStrings.Cancel, (IDialogInterfaceOnClickListener?)null)!);
    }

    /// <summary>
    /// Лист правки числа — для широких диапазонов, где ± кнопкой не набрать (макет 2c, план §4.2):
    /// крупное значение, ползунок с подписями концов, поле для точного ввода и ряд «Где хранить».
    /// Диалога с меню «⋮» больше нет — слой выбирается здесь же.
    /// </summary>
    private void ShowNumberSheet(SettingDescriptor descriptor, ResolvedSetting resolved, double current)
    {
        double span = descriptor.Maximum - descriptor.Minimum;
        int steps = descriptor.Step > 0 ? Math.Max(1, (int)Math.Round(span / descriptor.Step)) : 100;
        double value = current;

        var sheet = SheetRoot();
        sheet.AddView(SheetGrabber());
        sheet.AddView(SheetTitle(TranslateExtension.Get(descriptor.LabelKey)));

        if (descriptor.HintKey is { } hintKey) sheet.AddView(SheetHint(TranslateExtension.Get(hintKey)));

        var readout = new TextView(this) { Text = ValueWord(descriptor, value), Gravity = GravityFlags.Center };
        readout.SetTextSize(ComplexUnitType.Sp, 44);
        readout.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        readout.SetTextColor(UiKit.PlainText(this));

        var slider = new SeekBar(this)
        {
            Max = steps,
            Progress = span > 0 ? (int)Math.Round((value - descriptor.Minimum) / span * steps) : 0,
        };

        var input = new EditText(this) { Text = SettingsFormat.Store(descriptor, value) };
        var inputType = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal;
        if (descriptor.Minimum < 0) inputType |= InputTypes.NumberFlagSigned;
        input.InputType = inputType;

        // Слайдер, поле и крупное число правят одно и то же — сторож, а не три источника правды,
        // иначе ввод в поле дёргал бы ползунок обратно на каждый символ. Перенесено из EditNumber
        // как есть, вместе с заменой запятой на точку и разбором по инвариантной культуре.
        bool syncing = false;

        void ShowValue(double next, bool fromInput)
        {
            value = Math.Clamp(next, descriptor.Minimum, descriptor.Maximum);
            readout.Text = ValueWord(descriptor, value);

            syncing = true;
            if (span > 0) slider.Progress = (int)Math.Round((value - descriptor.Minimum) / span * steps);
            if (!fromInput)
            {
                string text = SettingsFormat.Store(descriptor, value);
                input.Text = text;
                input.SetSelection(text.Length);
            }

            syncing = false;
        }

        var numberRow = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        numberRow.SetGravity(GravityFlags.CenterVertical);
        numberRow.AddView(SheetStep("−", () => ShowValue(SettingsFormat.Snap(descriptor, value - descriptor.Step), false)));
        numberRow.AddView(readout, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        numberRow.AddView(SheetStep("+", () => ShowValue(SettingsFormat.Snap(descriptor, value + descriptor.Step), false)));
        sheet.AddView(numberRow, SheetGap(this.Dp(22)));

        slider.ProgressChanged += (_, e) =>
        {
            if (e.FromUser && !syncing) ShowValue(At(descriptor, e.Progress, steps), false);
        };
        sheet.AddView(slider, SheetGap(this.Dp(16)));

        var ends = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };

        // У ручки, где ноль выключает, левый конец шкалы так и сказан словом: «0» там значит не
        // «мало», а «зоны нет вовсе».
        var low = new TextView(this)
        {
            Text = descriptor.ZeroDisables && descriptor.Minimum == 0
                ? $"0 — {AppStrings.SettingsValueOff}"
                : SettingsFormat.Display(descriptor, descriptor.Minimum),
        };
        low.SetTextSize(ComplexUnitType.Sp, 13);
        low.SetTextColor(this.Hint());
        ends.AddView(low, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var high = new TextView(this) { Text = SettingsFormat.Display(descriptor, descriptor.Maximum) };
        high.SetTextSize(ComplexUnitType.Sp, 13);
        high.SetTextColor(this.Hint());
        ends.AddView(high);
        sheet.AddView(ends, SheetGap(this.Dp(10)));

        input.TextChanged += (_, _) =>
        {
            if (syncing) return;
            ShowValue(SettingsFormat.ParseNumber((input.Text ?? "").Replace(',', '.')), true);
        };
        sheet.AddView(input, SheetGap(this.Dp(10)));

        sheet.AddView(StorageRow(descriptor, resolved), SheetGap(this.Dp(20)));

        sheet.AddView(SheetButtons(AppStrings.SettingsAccept, () =>
            Commit(descriptor, SettingsFormat.Store(descriptor, Math.Clamp(
                SettingsFormat.Snap(descriptor, value), descriptor.Minimum, descriptor.Maximum)))), SheetGap(this.Dp(22)));

        _windows.ShowSheet(this, sheet);
    }

    /// <summary>
    /// Тот же лист без числа — им меняют слой у строк, правящихся на месте, и у переключателей
    /// (план §4.3): меню «⋮» ушло, а ряд «Где хранить» показать всё равно где-то надо.
    /// </summary>
    /// <summary>
    /// Есть ли у строки слои вовсе. У сообщённого колесом, сеансовой, действия, прослушивания и
    /// справки значения в слоях нет — и хранить им нечего.
    /// </summary>
    private static bool HasLayers(SettingDescriptor descriptor) =>
        !descriptor.ReportedByWheel
        && !descriptor.Transient
        && descriptor.Kind is not (SettingKind.Action or SettingKind.Slider or SettingKind.Note);

    private void ShowStorageSheet(SettingDescriptor descriptor, ResolvedSetting resolved)
    {
        if (!HasLayers(descriptor)) return;

        var sheet = SheetRoot();
        sheet.AddView(SheetGrabber());
        sheet.AddView(SheetTitle(TranslateExtension.Get(descriptor.LabelKey)));
        sheet.AddView(SheetHint(SettingsFormat.ValueText(descriptor, resolved)));
        sheet.AddView(StorageRow(descriptor, resolved), SheetGap(this.Dp(20)));
        sheet.AddView(SheetButtons(AppStrings.SettingsAccept, () => { }), SheetGap(this.Dp(22)));

        _windows.ShowSheet(this, sheet);
    }

    /// <summary>
    /// Ряд «Где хранить»: <b>Заводское · Общее · Это колесо</b>, отмечен тот слой, откуда значение
    /// пришло (план §4.3). Команды — те же три, что были в меню строки, и запреты те же: у
    /// <see cref="SettingDescriptor.WheelOnly"/> нет «Общего», у <see cref="SettingDescriptor.GlobalOnly"/>
    /// — «Этого колеса».
    /// <para>
    /// «Заводское» снимает <b>один</b> слой, а не оба разом: своё значение возвращает к общему, и
    /// только у общего — к заводскому. Это семантика слоёв, и трогать её план запрещает.
    /// </para>
    /// </summary>
    private View StorageRow(SettingDescriptor descriptor, ResolvedSetting resolved)
    {
        var block = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };

        block.AddView(SheetSectionTitle(AppStrings.SettingsWhereToStore));

        var row = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        row.SetPadding(0, this.Dp(10), 0, 0);

        void Add(string caption, bool picked, Action tapped) =>
            row.AddView(StorageChip(caption, picked, tapped), new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WrapContent, 1f)
            {
                LeftMargin = row.ChildCount > 0 ? this.Dp(8) : 0,
            });

        Add(AppStrings.SettingsLayerFactory, resolved.Origin == SettingOrigin.Factory, () =>
        {
            if (resolved.IsOverridden) _binder.ClearOverride(descriptor, _viewScope);
            else _binder.ClearGlobal(descriptor);
            Rebuild();
        });

        if (!descriptor.WheelOnly)
        {
            Add(AppStrings.SettingsLayerGlobal, resolved.Origin == SettingOrigin.Global, () =>
            {
                if (resolved.IsOverridden) _binder.PromoteToGlobal(descriptor, _viewScope);
                else _binder.Set(descriptor, resolved.Value ?? descriptor.Current(), LayeredSettings.GlobalScope);
                Rebuild();
            });
        }

        if (!descriptor.GlobalOnly && _wheel.Address.Length > 0)
        {
            Add(WheelLayerName(), resolved.Origin == SettingOrigin.Wheel, () =>
            {
                _binder.Set(descriptor, resolved.Value ?? descriptor.Current(), _wheel.Address);
                Rebuild();
            });
        }

        block.AddView(row);
        return block;
    }

    /// <summary>
    /// Как зовётся слой колеса там, где места мало: «Колесо C8:3E» — тем же ключом, что и полное
    /// имя, но с укороченным адресом. Полный MAC в треть ряда не встаёт, а различают колёса по
    /// первым парам не хуже, чем по всем шести.
    /// </summary>
    private string WheelLayerName() =>
        string.Format(CultureInfo.CurrentCulture, AppStrings.SettingsLayerWheel, ShortMac(_wheel.Address));

    private static string ShortMac(string address) =>
        address.Length >= 5 ? address[..5] : address;

    private View StorageChip(string caption, bool picked, Action tapped)
    {
        var chip = new TextView(this) { Text = caption, Gravity = GravityFlags.Center };
        chip.SetTextSize(ComplexUnitType.Sp, 14);
        chip.SetTextColor(picked ? this.Override() : this.TextMuted());
        if (picked) chip.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        chip.SetPadding(0, this.Dp(11), 0, this.Dp(11));
        chip.Background = Framed(this.Dp(10), this.Dp(picked ? 2 : 1), picked ? this.Override() : this.Border());
        chip.Clickable = true;
        chip.Click += (_, _) =>
        {
            _windows.Close();
            tapped();
        };
        return chip;
    }

    // ---- Кирпичи листа ------------------------------------------------------------------------

    private LinearLayout SheetRoot()
    {
        var sheet = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        sheet.SetPadding(this.Dp(20), this.Dp(14), this.Dp(20), this.Dp(26));

        var background = new GradientDrawable();
        background.SetShape(ShapeType.Rectangle);
        float radius = this.Dp(22);
        background.SetCornerRadii([radius, radius, radius, radius, 0, 0, 0, 0]);
        background.SetColor(this.Card());
        sheet.Background = background;

        return sheet;
    }

    private View SheetGrabber()
    {
        var grabber = new View(this);
        var fill = new GradientDrawable();
        fill.SetShape(ShapeType.Rectangle);
        fill.SetCornerRadius(this.Dp(2));
        fill.SetColor(this.Border());
        grabber.Background = fill;
        grabber.LayoutParameters = new LinearLayout.LayoutParams(this.Dp(40), this.Dp(4))
        {
            Gravity = GravityFlags.CenterHorizontal,
            BottomMargin = this.Dp(18),
        };
        return grabber;
    }

    private TextView SheetTitle(string text)
    {
        var title = new TextView(this) { Text = text };
        title.SetTextSize(ComplexUnitType.Sp, 22);
        title.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        title.SetTextColor(UiKit.PlainText(this));
        return title;
    }

    private TextView SheetHint(string text)
    {
        var hint = new TextView(this) { Text = text };
        hint.SetTextSize(ComplexUnitType.Sp, 14);
        hint.SetTextColor(this.TextSecondary());
        hint.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = this.Dp(6),
        };
        return hint;
    }

    private TextView SheetSectionTitle(string text)
    {
        var caption = new TextView(this) { Text = text.ToUpperInvariant() };
        caption.SetTextSize(ComplexUnitType.Sp, 13);
        caption.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        caption.SetTextColor(this.TextSecondary());
        caption.LetterSpacing = 0.05f;
        return caption;
    }

    private View SheetStep(string sign, Action tapped)
    {
        var button = new TextView(this) { Text = sign, Gravity = GravityFlags.Center };
        button.SetTextSize(ComplexUnitType.Sp, 28);
        button.SetTextColor(this.TextControl());
        button.Clickable = true;
        button.Click += (_, _) => tapped();
        button.Background = Framed(this.Dp(12), this.Dp(1), this.Border());
        button.LayoutParameters = new LinearLayout.LayoutParams(this.Dp(54), this.Dp(54));
        return button;
    }

    /// <summary>«Отмена» и «Готово»: отмена только закрывает лист — значение пишется по «Готово».</summary>
    private View SheetButtons(string accept, Action accepted)
    {
        var row = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };

        var cancel = new TextView(this) { Text = AppStrings.Cancel, Gravity = GravityFlags.Center };
        cancel.SetTextSize(ComplexUnitType.Sp, 16);
        cancel.SetTextColor(this.TextMuted());
        cancel.SetPadding(0, this.Dp(15), 0, this.Dp(15));
        cancel.Clickable = true;
        cancel.Click += (_, _) => _windows.Close();
        row.AddView(cancel, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var done = new TextView(this) { Text = accept, Gravity = GravityFlags.Center };
        done.SetTextSize(ComplexUnitType.Sp, 16);
        done.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        done.SetTextColor(this.OnAccent());
        done.SetPadding(0, this.Dp(15), 0, this.Dp(15));
        done.Clickable = true;
        done.Click += (_, _) =>
        {
            _windows.Close();
            accepted();
        };

        var fill = new GradientDrawable();
        fill.SetShape(ShapeType.Rectangle);
        fill.SetCornerRadius(this.Dp(12));
        fill.SetColor(this.Accent());
        done.Background = fill;

        row.AddView(done, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1.4f)
        {
            LeftMargin = this.Dp(10),
        });

        return row;
    }

    private LinearLayout.LayoutParams SheetGap(int top) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = top };

    private void EditChoice(SettingDescriptor descriptor, string value)
    {
        var choices = descriptor.Choices;

        // Подписи — тем же правилом, что у строки (SettingsFormat.ChoiceLabel): ключ ресурса,
        // если он есть, иначе само значение. У палитры подписей-ключей нет — её варианты и есть
        // имена, — и меню, собранное только из ключей, открывалось пустым.
        string[] labels = [.. choices.Select(choice => SettingsFormat.ChoiceLabel(descriptor, choice))];
        int selected = SettingsFormat.IndexOfChoice(descriptor, value);
        int chosen = selected;

        _windows.Show(new AlertDialog.Builder(this)!
            .SetTitle(TranslateExtension.Get(descriptor.LabelKey))!
            .SetSingleChoiceItems(labels, selected, (_, e) => chosen = e.Which)!
            .SetPositiveButton(AppStrings.SettingsAccept, (_, _) =>
            {
                if (chosen >= 0 && chosen < choices.Count) Commit(descriptor, choices[chosen]);
            })!
            .SetNegativeButton(AppStrings.Cancel, (_, _) => { })!);
    }

    /// <summary>
    /// The one row kind that does something rather than store something — wrapped in a try because
    /// it runs off a tap, same rule as everywhere else event handlers can throw. Сообщение
    /// исключения показывается человеку: у действия, которому нечего сделать, это единственный
    /// способ ответить (кнопка ряда так и говорит, что считать не по чему).
    /// <para>
    /// Страница перестраивается после удачного действия: кнопка вправе изменить соседнюю строку —
    /// ряд ячеек она как раз и подставляет, — а незамеченная правка выглядит как ничего не
    /// сделавшая кнопка.
    /// </para>
    /// </summary>
    private void RunAction(SettingDescriptor descriptor, Button button)
    {
        button.Enabled = false;
        try
        {
            descriptor.Apply("");

            // Отчёт снимается после применения и до перестроения: он описывает то, что действие
            // только что сделало, а строка на экране к этому времени уже показывает новое значение.
            string? report = descriptor.Report?.Invoke();
            Rebuild();

            if (report is { Length: > 0 }) Announce(descriptor, report);
        }
        catch (Exception ex)
        {
            Announce(descriptor, ex.Message);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    /// <summary>
    /// Ответ действия человеку — один вид окна и на отказ, и на итог: обе вести приходят на то же
    /// нажатие, и разводить их по разным способам показа значило бы учить райдера двум языкам вместо
    /// одного.
    /// </summary>
    private void Announce(SettingDescriptor descriptor, string message) =>
        _windows.Show(new AlertDialog.Builder(this)!
            .SetTitle(TranslateExtension.Get(descriptor.LabelKey))!
            .SetMessage(message)!
            .SetPositiveButton(AppStrings.SettingsAccept, (_, _) => { })!);

    /// <summary>
    /// Ключ ручки «Тревога поверх других приложений» (<c>AlertsPage</c>) — зашит здесь одним местом,
    /// а не константой в общем коде: тумблеров, просящих системное разрешение при включении, пока
    /// один, и заводить под него общий механизм незачем (решение владельца 11.08.2026).
    /// </summary>
    private const string OverlayOtherAppsKey = "AlertSignals:OverlayOtherApps";

    private void Commit(SettingDescriptor descriptor, string value)
    {
        _binder.Set(descriptor, value, _viewScope);

        // Запрос — только на включении и только если системе ещё нечего показать: выключение и
        // повторное включение уже разрешённого канала никого никуда не отправляют. Не дадут
        // разрешение — флаг остаётся включённым, SystemAlertOverlay просто не покажется, пока его нет.
        if (descriptor.Key == OverlayOtherAppsKey
            && SettingsFormat.ParseBool(value)
            && !Android.Provider.Settings.CanDrawOverlays(this))
        {
            StartActivity(new Intent(
                Android.Provider.Settings.ActionManageOverlayPermission,
                Android.Net.Uri.Parse("package:" + PackageName)));
        }

        Rebuild();
    }

    // ---- Разметка ---------------------------------------------------------------------------

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        root.SetBackgroundColor(this.Surface());

        if (BuildScopeRow() is { } scope) root.AddView(scope);
        if (BuildSectionChips() is { } chips) root.AddView(chips);

        _scroll = new ScrollView(this);
        _content = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        int pad = this.Dp(16);
        _content.SetPadding(pad, this.Dp(4), pad, pad);
        _scroll.AddView(_content);

        root.AddView(_scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        return root;
    }
}
