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
using WheelTalk.Core.Settings;
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

        // Пятой и последней: не тема наравне с четырьмя, а отметка зрелости (план 28). Стоит внизу
        // потому, что заходить туда незачем, пока не понадобилось именно новое.
        (SettingsPage.Experimental, "SettingsPageExperimental"),
    ];

    private SettingsBinder _binder = null!;
    private LayeredSettings _settings = null!;
    private WheelOptions _wheel = null!;

    private readonly List<TextView> _summaryLabels = new(Categories.Length);
    private TextView _scopeLabel = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Заголовок ставится кодом, а не атрибутом [Activity]: в атрибут можно положить только
        // константу, а строки живут в ресурсах (план 6, фаза 1 — вшитых строк не осталось).
        Title = string.Format(CultureInfo.CurrentCulture, AppStrings.ScreenTitleFormat, AppStrings.SettingsTitle);

        _binder = MainApplication.Services.GetRequiredService<SettingsBinder>();
        _settings = MainApplication.Services.GetRequiredService<LayeredSettings>();
        _wheel = MainApplication.Services.GetRequiredService<IOptions<WheelOptions>>().Value;

        var root = BuildLayout();
        SetContentView(root);
        EdgeToEdge.Apply(this, root);
    }

    protected override void OnStart()
    {
        base.OnStart();
        Show();
    }

    /// <summary>Summaries are recomputed on every appearance, not just once — a value changed on a category page has to show here on the way back, the same reason the MAUI root page re-read its scope label in OnAppearing.</summary>
    private void Show()
    {
        _scopeLabel.SetText(_settings.Scope.Length > 0
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, AppStrings.SettingsScopeWheel, _settings.Scope)
            : AppStrings.SettingsScopeGlobal);

        for (int i = 0; i < Categories.Length; i++)
        {
            _summaryLabels[i].SetText(SettingsFormat.Summarize(_binder, Categories[i].Page));
        }
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
        scroll.SetBackgroundColor(this.PageBackground());

        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        int pad = this.Dp(16);
        root.SetPadding(pad, pad, pad, pad);

        _scopeLabel = new TextView(this) { Text = "" };
        _scopeLabel.SetTextSize(ComplexUnitType.Sp, 12);
        _scopeLabel.Alpha = 0.7f;
        root.AddView(_scopeLabel, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            BottomMargin = this.Dp(12),
        });

        foreach (var (page, titleKey) in Categories)
        {
            root.AddView(CategoryCard(page, titleKey), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = this.Dp(10),
            });
        }

        var hint = new TextView(this) { Text = AppStrings.SettingsDisplayHint };
        hint.SetTextSize(ComplexUnitType.Sp, 12);
        hint.Alpha = 0.7f;
        root.AddView(hint, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = this.Dp(18),
        });

        scroll.AddView(root);
        return scroll;
    }

    /// <summary>Bold title, summary underneath — the whole card is the tap target (settings-redesign.md §4 "вся строка кликабельна", the same enlarged target the row menu got on the category screen).</summary>
    private View CategoryCard(SettingsPage page, string titleKey)
    {
        var card = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        card.Clickable = true;
        card.Click += (_, _) => Open(page);
        int padH = this.Dp(14), padV = this.Dp(12);
        card.SetPadding(padH, padV, padH, padV);
        card.Background = CardBackground();

        var title = new TextView(this) { Text = TranslateExtension.Get(titleKey) };
        title.SetTextSize(ComplexUnitType.Sp, 17);
        title.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        title.SetTextColor(UiKit.PlainText(this));
        card.AddView(title);

        var summary = new TextView(this) { Text = "" };
        summary.SetTextSize(ComplexUnitType.Sp, 13);
        summary.Alpha = 0.7f;
        card.AddView(summary, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = this.Dp(4),
        });
        _summaryLabels.Add(summary);

        return card;
    }

    private Drawable CardBackground()
    {
        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Rectangle);
        drawable.SetCornerRadius(this.Dp(10));
        drawable.SetStroke(this.Dp(1), Color.ParseColor("#30808080"));
        return drawable;
    }
}
