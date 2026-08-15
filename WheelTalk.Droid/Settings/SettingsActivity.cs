using System.Globalization;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WheelTalk.Core.Services;
using WheelTalk.Core.Settings;
using WheelTalk.Core.Settings.Device;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Resources.Strings;
using WheelTalk.Droid.Settings;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Droid.Ui;

using WheelTalk.Droid.App;

namespace WheelTalk.Droid.Settings;

/// <summary>
/// Root of settings — variant B from <c>docs/settings-redesign.md</c> §4 (the owner's decision,
/// 29.07.2026): four categories, unchanged from the original, but rows with a summary instead of
/// four twin buttons (settings-redesign.md §1.1 "П2" — the original could not say what was inside a
/// category without opening it). The summary itself is generic (<see cref="SettingsFormat.Summarize"/>)
/// rather than hand-written per category, so it stays correct if the catalogue's row order changes.
/// <para>
/// Reading and writing settings anywhere in this screen and <see cref="SettingsCategoryActivity"/>
/// goes through <see cref="SettingsBinder"/>/<see cref="LayeredSettings"/> only — the same singletons
/// <c>MainApplication</c> wires the rest of the app to. Nothing here changes what the 45 settings mean
/// (<c>WheelTalk.Droid/Configuration/SettingsCatalogue.cs</c>, ported unchanged from
/// <c>WheelTalk.App</c>) — only how they are shown and reached.
/// </para>
/// </summary>
[Activity]
public sealed class SettingsActivity : Activity
{
    private static readonly (SettingsPage Page, string TitleKey)[] Categories =
    [
        (SettingsPage.Wheel, "SettingsPageWheel"),
        (SettingsPage.Warnings, "SettingsPageWarnings"),
        (SettingsPage.Application, "SettingsPageApplication"),
        (SettingsPage.Display, "SettingsPageDisplay"),

        // Конфигурация колеса — последней из тем и перед «Тестовыми функциями» (решение владельца
        // 15.08.2026, план 34 §12 п.7). Внизу потому, что это не наши настройки: править там нечего,
        // читают её тогда, когда пришли смотреть именно на колесо.
        (SettingsPage.WheelDevice, "SettingsPageWheelDevice"),

        // Последней: не тема наравне с прочими, а отметка зрелости (план 28). Стоит внизу
        // потому, что заходить туда незачем, пока не понадобилось именно новое.
        (SettingsPage.Experimental, "SettingsPageExperimental"),
    ];

    /// <summary>
    /// Цвета корня — ролями из палитры документных экранов (план 33, <see cref="DocPalette"/>):
    /// янтарь «своё» и его подложка, карточка с обводкой, тихий текст, стрелка перехода и точка
    /// выбранного колеса. Значения читаются из ресурсов и меняются вместе с системной темой.
    /// </summary>
    private SettingsBinder _binder = null!;
    private LayeredSettings _settings = null!;
    private WheelOptions _wheel = null!;
    private WheelIdentity _identity = null!;
    private WheelSession _session = null!;

    private readonly List<TextView> _summaryLabels = new(Categories.Length);

    /// <summary>Ярлыки «N своих» по номеру карточки — переписываются на каждом появлении экрана вместе со сводками.</summary>
    private readonly List<TextView> _ownBadges = new(Categories.Length);

    /// <summary>Сами карточки по номеру — нужны одной: «Конфигурацию колеса» у колеса чужой марки прячут целиком.</summary>
    private readonly List<View> _cards = new(Categories.Length);

    private TextView _scopeLabel = null!;
    private View _scopeDot = null!;

    private TimeProvider _clock = null!;

    /// <summary>С какого мгновения экран ждёт ответа колеса — от появления, как и на самой странице раздела.</summary>
    private DateTimeOffset _watchingSince;

    /// <summary>
    /// Сторож молчания «Конфигурации колеса» — тот же, что на странице раздела, и по той же
    /// причине: связь может остаться живой, а кадр настроек перестать приходить, и тогда события,
    /// от которого перерисоваться, не будет вовсе. Сводка карточки обязана не врать так же, как
    /// список внутри неё.
    /// </summary>
    private readonly Handler _silenceWatch = new(Looper.MainLooper!);
    private readonly Action _tick;
    private const int SilenceTickMs = 1000;

    public SettingsActivity() => _tick = Tick;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Заголовок ставится кодом, а не атрибутом [Activity]: в атрибут можно положить только
        // константу, а строки живут в ресурсах (план 6, фаза 1 — вшитых строк не осталось).
        Title = string.Format(CultureInfo.CurrentCulture, AppStrings.ScreenTitleFormat, AppStrings.SettingsTitle);

        _binder = MainApplication.Services.GetRequiredService<SettingsBinder>();
        _settings = MainApplication.Services.GetRequiredService<LayeredSettings>();
        _wheel = MainApplication.Services.GetRequiredService<IOptions<WheelOptions>>().Value;
        _identity = MainApplication.Services.GetRequiredService<WheelIdentity>();
        _session = MainApplication.Services.GetRequiredService<WheelSession>();
        _clock = MainApplication.Services.GetRequiredService<TimeProvider>();

        var root = BuildLayout();
        SetContentView(root);
        EdgeToEdge.Apply(this, root);
    }

    protected override void OnStart()
    {
        base.OnStart();
        _watchingSince = _clock.GetUtcNow();
        Show();

        _silenceWatch.RemoveCallbacks(_tick);
        _silenceWatch.PostDelayed(_tick, SilenceTickMs);
    }

    protected override void OnStop()
    {
        _silenceWatch.RemoveCallbacks(_tick);
        base.OnStop();
    }

    /// <summary>
    /// Тикает одна карточка из шести — «Конфигурация колеса»: её сводку меняет не человек, а
    /// колесо, и молчанием тоже. Остальным пяти тикать незачем, их значения лежат в наших слоях.
    /// </summary>
    private void Tick()
    {
        ShowWheelDeviceCard();
        _silenceWatch.PostDelayed(_tick, SilenceTickMs);
    }

    /// <summary>Summaries are recomputed on every appearance, not just once — a value changed on a category page has to show here on the way back, the same reason the MAUI root page re-read its scope label in OnAppearing.</summary>
    private void Show()
    {
        // Боевая область, и только она: строка отвечает «чем живёт приложение», а не «что смотрели
        // на прошлой странице». С планом 29 §29.3 второе перестало на неё влиять вовсе.
        //
        // Имя плюс адрес, а не один адрес: MAC различает колёса, а зовут их именем — тем же, каким
        // подписана панель (WheelIdentity, алиас либо имя анонса).
        bool picked = _settings.Scope.Length > 0;
        string name = picked ? _identity.Resolve(_wheel.Address) : "";
        _scopeLabel.SetText(picked
            ? name.Length > 0 ? $"{name} · {_wheel.Address}" : _wheel.Address
            : AppStrings.SettingsScopeGlobal);
        _scopeDot.Background = Dot(picked ? this.Picked() : this.TextSecondary());

        // Как колесо себя назвало — из последнего кадра, а не из алиаса: сводка «Конфигурации
        // колеса» говорит о модели, которую опознал протокол, и подменять её именем, данным
        // человеком, значило бы приписать чужие настройки не тому колесу.
        string model = _session.LastSnapshot?.Model ?? "";

        for (int i = 0; i < Categories.Length; i++)
        {
            if (Categories[i].Page == SettingsPage.WheelDevice) continue;

            _summaryLabels[i].SetText(SettingsFormat.Summarize(_binder, Categories[i].Page, model));
            ShowOwnCount(_ownBadges[i], Categories[i].Page);
        }

        ShowWheelDeviceCard();
    }

    /// <summary>
    /// Карточка «Конфигурации колеса» — единственная, которой может не быть вовсе: у колеса не той
    /// марки, чьи настройки мы читать умеем, раздела нет ни серого, ни пустого, а никакого (решение
    /// владельца 16.08.2026, план 34 §12.0 п. 4). Вход в раздел решает пара производитель-модель, и
    /// решается это здесь, до всякого касания.
    /// <para>
    /// Сводка у остальных состояний — тем же словом, каким объяснится и сама страница: карточка,
    /// пообещавшая «Sherman L · 14 значений» на оборванной связи, соврала бы раньше, чем человек
    /// её откроет — значения-то в последнем снимке сессии лежат и обрыв переживают.
    /// </para>
    /// </summary>
    private void ShowWheelDeviceCard()
    {
        int i = Array.FindIndex(Categories, category => category.Page == SettingsPage.WheelDevice);
        if (i < 0 || _cards.Count <= i) return;

        var view = WheelDeviceSection.Resolve(_session, _watchingSince, _clock);

        _cards[i].Visibility = view == WheelSettingsView.OtherBrand ? ViewStates.Gone : ViewStates.Visible;
        if (view == WheelSettingsView.OtherBrand) return;

        // Пустая сводка — только у ожидания короче десяти секунд: сказать о разговоре, начавшемся
        // секунду назад, пока нечего, а через секунду карточку перепишет сторож.
        _summaryLabels[i].SetText(WheelDeviceSection.TextKey(view) is { } key
            ? TranslateExtension.Get(key)
            : WheelDeviceSection.ShowsValues(view)
                ? SettingsFormat.Summarize(_binder, SettingsPage.WheelDevice, _session.LastSnapshot?.Model ?? "")
                : "");

        ShowOwnCount(_ownBadges[i], SettingsPage.WheelDevice);
    }

    /// <summary>
    /// «N своих» — сколько строк категории переопределено у <b>этого</b> колеса (план настроек §2.3).
    /// Считается по боевой области, как и сводка: ярлык отвечает на «есть ли тут моё», а моё — это
    /// то, по чему приложение едет. Ноль — ярлыка нет вовсе: пустой ярлык говорил бы «смотри сюда»
    /// там, где смотреть не на что.
    /// </summary>
    private void ShowOwnCount(TextView badge, SettingsPage page)
    {
        string scope = _binder.LiveScope;
        int own = scope.Length == 0
            ? 0
            : _binder.Page(page, scope)
                .SelectMany(section => section)
                .Count(descriptor => !descriptor.ReportedByWheel && _binder.Read(descriptor, scope).IsOverridden);

        badge.Visibility = own > 0 ? ViewStates.Visible : ViewStates.Gone;
        if (own > 0) badge.SetText(string.Format(CultureInfo.CurrentCulture, AppStrings.SettingsOwnValuesCount, own));
    }

    private void Open(SettingsPage page)
    {
        var intent = new Intent(this, typeof(SettingsCategoryActivity));
        intent.PutExtra(SettingsCategoryActivity.ExtraPage, (int)page);
        StartActivity(intent);
    }

    // ---- Разметка ---------------------------------------------------------------------------

    private View BuildLayout()
    {
        var scroll = new ScrollView(this);
        scroll.SetBackgroundColor(this.Surface());

        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        int pad = this.Dp(16);
        root.SetPadding(pad, this.Dp(20), pad, pad);

        var heading = new TextView(this) { Text = AppStrings.SettingsTitle };
        heading.SetTextSize(ComplexUnitType.Sp, 30);
        heading.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        heading.SetTextColor(UiKit.PlainText(this));
        root.AddView(heading);

        root.AddView(BuildWheelLine(), new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = this.Dp(8),
            BottomMargin = this.Dp(10),
        });

        foreach (var (page, titleKey) in Categories)
        {
            // «Тестовые функции» — пятой, но не такой же: пунктирная рамка и приглушённая подпись
            // (план 28 и §2.4 плана настроек). Она отметка зрелости, а не тема наравне с четырьмя,
            // и выглядеть как они не должна.
            bool experimental = page == SettingsPage.Experimental;

            if (experimental)
            {
                root.AddView(UiKit.Divider(this), new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, this.Dp(1))
                {
                    TopMargin = this.Dp(18),
                });
            }

            root.AddView(CategoryCard(page, titleKey, experimental), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = this.Dp(experimental ? 16 : 10),
            });
        }

        var hint = new TextView(this) { Text = AppStrings.SettingsDisplayHint };
        hint.SetTextSize(ComplexUnitType.Sp, 13);
        hint.SetTextColor(this.HintDim());
        root.AddView(hint, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = this.Dp(18),
        });

        scroll.AddView(root);
        return scroll;
    }

    /// <summary>
    /// Колесо, чьи слои показывает экран: точка состояния и «имя · MAC». Заменяет прежнюю
    /// пассивную <c>_scopeLabel</c> 12sp (§2.1) — она говорила то же самое, но словами «Область:».
    /// </summary>
    private View BuildWheelLine()
    {
        var line = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        line.SetGravity(GravityFlags.CenterVertical);

        _scopeDot = new View(this);
        line.AddView(_scopeDot, new LinearLayout.LayoutParams(this.Dp(7), this.Dp(7)) { RightMargin = this.Dp(8) });

        _scopeLabel = new TextView(this) { Text = "" };
        _scopeLabel.SetTextSize(ComplexUnitType.Sp, 14);
        _scopeLabel.SetTextColor(this.TextSecondary());
        line.AddView(_scopeLabel);

        return line;
    }

    private Drawable Dot(Color color)
    {
        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Oval);
        drawable.SetColor(color);
        return drawable;
    }

    /// <summary>Подпись, сводка и — если у этого колеса тут есть своё — янтарный ярлык. Вся карточка есть цель касания.</summary>
    private View CategoryCard(SettingsPage page, string titleKey, bool experimental)
    {
        var card = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        card.SetGravity(GravityFlags.CenterVertical);
        card.Clickable = true;
        card.Click += (_, _) => Open(page);
        int padH = this.Dp(16), padV = this.Dp(14);
        card.SetPadding(padH, padV, padH, padV);
        card.Background = CardBackground(experimental);

        var words = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };

        var title = new TextView(this) { Text = TranslateExtension.Get(titleKey) };
        title.SetTextSize(ComplexUnitType.Sp, 19);
        title.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        title.SetTextColor(experimental ? this.TextTitle() : UiKit.PlainText(this));
        words.AddView(title);

        var summary = new TextView(this) { Text = "" };
        summary.SetTextSize(ComplexUnitType.Sp, 14);
        summary.SetTextColor(experimental ? this.Hint() : this.TextSecondary());
        words.AddView(summary, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = this.Dp(4),
        });
        _summaryLabels.Add(summary);

        card.AddView(words, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var badge = new TextView(this) { Text = "", Visibility = ViewStates.Gone };
        badge.SetTextSize(ComplexUnitType.Sp, 12);
        badge.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        badge.SetTextColor(this.Override());
        badge.SetSingleLine(true);
        badge.SetPadding(this.Dp(8), this.Dp(4), this.Dp(8), this.Dp(4));
        badge.Background = BadgeBackground();
        card.AddView(badge, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            LeftMargin = this.Dp(12),
        });
        _ownBadges.Add(badge);

        var chevron = new TextView(this) { Text = "›" };
        chevron.SetTextSize(ComplexUnitType.Sp, 22);
        chevron.SetTextColor(this.Chevron());
        card.AddView(chevron, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            LeftMargin = this.Dp(12),
        });

        // Карточка запоминается целиком, а не одной своей сводкой: «Конфигурацию колеса» прячут
        // вместе с отступом, а спрятанный ребёнок LinearLayout не занимает ни высоты, ни поля.
        _cards.Add(card);

        return card;
    }

    private Drawable CardBackground(bool experimental)
    {
        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Rectangle);
        drawable.SetCornerRadius(this.Dp(14));

        if (experimental)
        {
            // Пунктиром и без заливки: карточка на месте, но видом говорит «это не четвёртая тема».
            drawable.SetStroke(this.Dp(1), this.Border(), this.Dp(5), this.Dp(4));
            return drawable;
        }

        drawable.SetColor(this.Card());
        drawable.SetStroke(this.Dp(1), this.CardBorder());
        return drawable;
    }

    private Drawable BadgeBackground()
    {
        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Rectangle);
        drawable.SetCornerRadius(this.Dp(6));
        drawable.SetColor(this.OverrideFill());
        return drawable;
    }
}
