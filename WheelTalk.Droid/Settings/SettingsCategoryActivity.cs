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
using WheelTalk.Core.Settings;
using WheelTalk.Droid;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Resources.Strings;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Droid.Ui;

using WheelTalk.Droid.App;

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
/// above the list (<see cref="MaterialButtonToggleGroup"/> — <see cref="LayeredSettings.Scope"/> only
/// ever holds the global scope or the wheel already selected in <see cref="WheelOptions"/>, never an
/// arbitrary one, so two buttons are the whole of it); quick-jump chips for "Отображение" only, the
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

    private static readonly Color OverrideColor = Color.ParseColor("#FF8F00");
    private static readonly Color BorderColor = Color.ParseColor("#40808080");

    private SettingsBinder _binder = null!;
    private LayeredSettings _settings = null!;
    private WheelOptions _wheel = null!;
    private SettingsPage _page;

    private ScrollView _scroll = null!;
    private LinearLayout _content = null!;
    private MaterialButton? _globalButton;
    private MaterialButton? _wheelButton;

    private readonly Dictionary<string, View> _sectionAnchors = new(StringComparer.Ordinal);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Заголовок ставится кодом, а не атрибутом [Activity]: в атрибут можно положить только
        // константу, а строки живут в ресурсах (план 6, фаза 1 — вшитых строк не осталось).
        Title = string.Format(CultureInfo.CurrentCulture, AppStrings.ScreenTitleFormat, AppStrings.SettingsTitle);

        _binder = MainApplication.Services.GetRequiredService<SettingsBinder>();
        _settings = MainApplication.Services.GetRequiredService<LayeredSettings>();
        _wheel = MainApplication.Services.GetRequiredService<IOptions<WheelOptions>>().Value;
        _page = (SettingsPage)(Intent?.GetIntExtra(ExtraPage, (int)SettingsPage.Application) ?? (int)SettingsPage.Application);

        Title = TranslateExtension.Get(PageTitleKey(_page));

        var root = BuildLayout();
        SetContentView(root);
        EdgeToEdge.Apply(this, root);
    }

    protected override void OnStart()
    {
        base.OnStart();
        Rebuild();
    }

    private static string PageTitleKey(SettingsPage page) => page switch
    {
        SettingsPage.Wheel => "SettingsPageWheel",
        SettingsPage.Warnings => "SettingsPageWarnings",
        SettingsPage.Display => "SettingsPageDisplay",
        _ => "SettingsPageApplication",
    };

    // ---- Scope (общее / это колесо) ----------------------------------------------------------

    /// <summary>
    /// "Общее" and the wheel already selected — not a list of every wheel ever seen (variant B's
    /// deliberate cut from variant A, settings-redesign.md §4 "Цена"): <see cref="LayeredSettings.Scope"/>
    /// already equals <see cref="WheelOptions.Address"/> from startup, so there is nothing to look up.
    /// </summary>
    private View BuildScopeRow()
    {
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

        _globalButton = new MaterialButton(this) { Text = AppStrings.SettingsLayerGlobal };
        _globalButton.SetTextSize(ComplexUnitType.Sp, 12);
        _globalButton.Click += (_, _) => SetScope(LayeredSettings.GlobalScope);
        group.AddView(_globalButton, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        _wheelButton = new MaterialButton(this) { Text = _wheel.Address };
        _wheelButton.SetTextSize(ComplexUnitType.Sp, 12);
        _wheelButton.Click += (_, _) => SetScope(_wheel.Address);
        group.AddView(_wheelButton, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        container.AddView(group, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return container;
    }

    private void SetScope(string scope)
    {
        if (_settings.Scope != scope) _settings.Scope = scope;
        ShowScope();
        Rebuild();
    }

    private void ShowScope()
    {
        if (_globalButton is null || _wheelButton is null) return;

        bool onWheel = _settings.Scope.Length > 0;
        if (_globalButton.Checked == !onWheel && _wheelButton.Checked == onWheel) return;

        _globalButton.Checked = !onWheel;
        _wheelButton.Checked = onWheel;
    }

    // ---- Quick-jump chips (только «Отображение» — 20 из 45 ручек, план B §"Отображение") -------

    private View? BuildSectionChips()
    {
        if (_page != SettingsPage.Display) return null;

        var sections = _binder.Page(_page).Select(section => section.Key).Distinct().ToList();
        if (sections.Count == 0) return null;

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
        ShowScope();

        _content.RemoveAllViews();
        _sectionAnchors.Clear();

        bool dividedAdvanced = false;
        bool any = false;
        foreach (var section in _binder.Page(_page))
        {
            any = true;
            bool advanced = section.First().Advanced;
            if (advanced && !dividedAdvanced)
            {
                dividedAdvanced = true;
                _content.AddView(UiKit.Divider(this), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, this.Dp(1))
                {
                    TopMargin = this.Dp(16),
                });

                var advancedLabel = new TextView(this) { Text = AppStrings.SettingsAdvanced };
                advancedLabel.SetTextSize(ComplexUnitType.Sp, 12);
                advancedLabel.Alpha = 0.6f;
                _content.AddView(advancedLabel, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
                {
                    TopMargin = this.Dp(4),
                });
            }

            var header = new TextView(this) { Text = TranslateExtension.Get(section.Key) };
            header.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
            header.SetTextSize(ComplexUnitType.Sp, 15);
            header.SetTextColor(UiKit.PlainText(this));
            _content.AddView(header, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = this.Dp(16),
                BottomMargin = this.Dp(2),
            });
            _sectionAnchors[section.Key] = header;

            foreach (var descriptor in section) _content.AddView(BuildRow(descriptor));
        }

        if (!any)
        {
            var empty = new TextView(this) { Text = AppStrings.SettingsEmpty };
            empty.Alpha = 0.7f;
            _content.AddView(empty);
        }
    }

    /// <summary>
    /// One row: title and editor on top, then a layer/range line and, if there is one, the hint —
    /// both visible without a tap (settings-redesign.md §4, П3/П4/П6). The row menu (⋮) still needs a
    /// tap, but only reaching it does, not seeing that the value is not the default.
    /// </summary>
    private View BuildRow(SettingDescriptor descriptor)
    {
        var resolved = _binder.Read(descriptor);

        var card = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        card.SetPadding(0, this.Dp(8), 0, this.Dp(8));

        var top = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        top.SetGravity(GravityFlags.CenterVertical);

        var title = new TextView(this) { Text = TranslateExtension.Get(descriptor.LabelKey) };
        title.SetTextSize(ComplexUnitType.Sp, 15);
        title.SetTextColor(UiKit.PlainText(this));
        top.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        top.AddView(BuildEditor(descriptor, resolved));

        if (CanOpenMenu(descriptor, resolved))
        {
            var menu = new TextView(this) { Text = AppStrings.SettingsRowMenu };
            menu.SetTextSize(ComplexUnitType.Sp, 18);
            menu.Alpha = 0.6f;
            menu.SetPadding(this.Dp(12), 0, 0, 0);
            menu.Clickable = true;
            menu.Click += (_, _) => ShowRowMenu(descriptor, resolved);
            top.AddView(menu);
        }

        card.AddView(top);

        string? meta = BuildMeta(descriptor, resolved);
        if (meta is not null)
        {
            var metaLabel = new TextView(this) { Text = meta };
            metaLabel.SetTextSize(ComplexUnitType.Sp, 12);
            if (resolved.IsOverridden)
            {
                metaLabel.SetTextColor(OverrideColor);
                metaLabel.Alpha = 1f;
            }
            else
            {
                metaLabel.Alpha = 0.6f;
            }

            card.AddView(metaLabel, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = this.Dp(2),
            });
        }

        if (descriptor.HintKey is { } hintKey)
        {
            var hint = new TextView(this) { Text = TranslateExtension.Get(hintKey) };
            hint.SetTextSize(ComplexUnitType.Sp, 12);
            hint.Alpha = 0.55f;
            card.AddView(hint, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = this.Dp(2),
            });
        }

        return card;
    }

    /// <summary>Layer the value came from, plus — for numbers, always — the range: "70 % · Колесо AA:BB". Snap of МAUI's ScopeLabel/rows, minus the menu they needed a second glance to notice.</summary>
    private string? BuildMeta(SettingDescriptor descriptor, ResolvedSetting resolved)
    {
        if (descriptor.ReportedByWheel || descriptor.Kind == SettingKind.Action) return null;

        string origin = resolved.Origin switch
        {
            SettingOrigin.Wheel => string.Format(CultureInfo.CurrentCulture, AppStrings.SettingsLayerWheel, _wheel.Address),
            SettingOrigin.Global => AppStrings.SettingsLayerGlobal,
            _ => AppStrings.SettingsLayerFactory,
        };

        if (descriptor.Kind != SettingKind.Number) return origin;

        string range = $"{SettingsFormat.Store(descriptor, descriptor.Minimum)}–{SettingsFormat.Store(descriptor, descriptor.Maximum)}";
        return descriptor.UnitKey is { } unit
            ? $"{range} {TranslateExtension.Get(unit)} · {origin}"
            : $"{range} · {origin}";
    }

    private View BuildEditor(SettingDescriptor descriptor, ResolvedSetting resolved)
    {
        string value = resolved.Value ?? descriptor.Current();

        // Сообщённое колесом показывается и не правится — переехало бы обратно само на следующем
        // подключении и выглядело бы правкой, которой никто не делал (то же рассуждение, что в
        // WheelTalk.App/Pages/SettingsListPage.xaml.cs).
        if (descriptor.ReportedByWheel)
        {
            var reported = new TextView(this) { Text = SettingsFormat.ParseBool(value) ? AppStrings.Yes : AppStrings.No };
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
                var readout = Outlined(SettingsFormat.Display(descriptor, current), resolved.IsOverridden);
                readout.Click += (_, _) => EditNumber(descriptor, current);
                return readout;
            }

            case SettingKind.Choice:
            {
                var readout = Outlined(SettingsFormat.ChoiceLabel(descriptor, value), resolved.IsOverridden);
                readout.Click += (_, _) => EditChoice(descriptor, value);
                return readout;
            }

            case SettingKind.Text:
            {
                var readout = Outlined(value.Length > 0 ? value : AppStrings.SettingsTextEmpty, resolved.IsOverridden);
                readout.Click += (_, _) => EditText(descriptor, value);
                return readout;
            }

            case SettingKind.Action:
                var button = UiKit.CreateButton(this, AppStrings.SettingsRun);
                button.SetTextSize(ComplexUnitType.Sp, 12);
                button.Click += (_, _) => RunAction(descriptor, button);
                return button;

            default:
                return new TextView(this) { Text = value };
        }
    }

    private TextView Outlined(string text, bool overridden)
    {
        var view = new TextView(this) { Text = text };
        view.SetTextSize(ComplexUnitType.Sp, 14);
        view.SetTextColor(UiKit.PlainText(this));
        view.SetPadding(this.Dp(10), this.Dp(4), this.Dp(10), this.Dp(4));
        view.Clickable = true;
        view.Background = EditorBackground(overridden);
        return view;
    }

    private Drawable EditorBackground(bool overridden)
    {
        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Rectangle);
        drawable.SetCornerRadius(this.Dp(6));
        drawable.SetStroke(this.Dp(overridden ? 2 : 1), overridden ? OverrideColor : BorderColor);
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

        new AlertDialog.Builder(this)!
            .SetTitle(TranslateExtension.Get(descriptor.LabelKey))!
            .SetView(container)!
            .SetPositiveButton(AppStrings.SettingsAccept, (_, _) => Commit(descriptor, input.Text?.Trim() ?? ""))!
            .SetNegativeButton(AppStrings.Cancel, (IDialogInterfaceOnClickListener?)null)!
            .Show();
    }

    /// <summary>
    /// "диалог со слайдером/EditText" for wide ranges (settings-redesign.md §4).
    /// </summary>
    private void EditNumber(SettingDescriptor descriptor, double current)
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

        var range = new TextView(this)
        {
            Text = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.SettingsRange,
                SettingsFormat.Display(descriptor, descriptor.Minimum),
                SettingsFormat.Display(descriptor, descriptor.Maximum)),
        };
        range.SetTextSize(ComplexUnitType.Sp, 12);
        range.Alpha = 0.7f;
        container.AddView(range, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            BottomMargin = this.Dp(6),
        });

        var input = new EditText(this) { Text = SettingsFormat.Store(descriptor, current) };
        var inputType = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal;
        if (descriptor.Minimum < 0) inputType |= InputTypes.NumberFlagSigned;
        input.InputType = inputType;
        container.AddView(input, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        double span = descriptor.Maximum - descriptor.Minimum;
        int steps = descriptor.Step > 0 ? Math.Max(1, (int)Math.Round(span / descriptor.Step)) : 100;
        var slider = new SeekBar(this) { Max = steps };
        slider.Progress = span > 0 ? (int)Math.Round((current - descriptor.Minimum) / span * steps) : 0;
        container.AddView(slider, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = this.Dp(8),
        });

        // Слайдер и поле правят одно и то же число — guard, а не два независимых источника правды,
        // иначе ввод в поле дёргал бы слайдер обратно на каждый символ.
        bool syncing = false;
        slider.ProgressChanged += (_, e) =>
        {
            if (!e.FromUser || syncing || span <= 0) return;
            syncing = true;
            double v = descriptor.Minimum + (double)e.Progress / steps * span;
            string text = SettingsFormat.Store(descriptor, SettingsFormat.Snap(descriptor, v));
            input.Text = text;
            input.SetSelection(text.Length);
            syncing = false;
        };
        input.TextChanged += (_, _) =>
        {
            if (syncing || span <= 0) return;
            syncing = true;
            double v = SettingsFormat.ParseNumber((input.Text ?? "").Replace(',', '.'));
            double clamped = Math.Clamp(v, descriptor.Minimum, descriptor.Maximum);
            slider.Progress = (int)Math.Round((clamped - descriptor.Minimum) / span * steps);
            syncing = false;
        };

        new AlertDialog.Builder(this)!
            .SetTitle(TranslateExtension.Get(descriptor.LabelKey))!
            .SetView(container)!
            .SetPositiveButton(AppStrings.SettingsAccept, (_, _) =>
            {
                // Запятая — то, что даёт русская раскладка цифровой клавиатуры; разбор идёт по
                // инвариантной культуре (то же обоснование, что в MAUI-эталоне).
                double parsed = SettingsFormat.ParseNumber((input.Text ?? "").Replace(',', '.'));
                double snapped = Math.Clamp(SettingsFormat.Snap(descriptor, parsed), descriptor.Minimum, descriptor.Maximum);
                Commit(descriptor, SettingsFormat.Store(descriptor, snapped));
            })!
            .SetNegativeButton(AppStrings.Cancel, (_, _) => { })!
            .Show();
    }

    private void EditChoice(SettingDescriptor descriptor, string value)
    {
        var choices = descriptor.Choices;

        // Подписи — тем же правилом, что у строки (SettingsFormat.ChoiceLabel): ключ ресурса,
        // если он есть, иначе само значение. У палитры подписей-ключей нет — её варианты и есть
        // имена, — и меню, собранное только из ключей, открывалось пустым.
        string[] labels = [.. choices.Select(choice => SettingsFormat.ChoiceLabel(descriptor, choice))];
        int selected = SettingsFormat.IndexOfChoice(descriptor, value);
        int chosen = selected;

        new AlertDialog.Builder(this)!
            .SetTitle(TranslateExtension.Get(descriptor.LabelKey))!
            .SetSingleChoiceItems(labels, selected, (_, e) => chosen = e.Which)!
            .SetPositiveButton(AppStrings.SettingsAccept, (_, _) =>
            {
                if (chosen >= 0 && chosen < choices.Count) Commit(descriptor, choices[chosen]);
            })!
            .SetNegativeButton(AppStrings.Cancel, (_, _) => { })!
            .Show();
    }

    /// <summary>The one row kind that does something rather than store something — wrapped in a try because it runs off a tap, same rule as everywhere else event handlers can throw.</summary>
    private void RunAction(SettingDescriptor descriptor, Button button)
    {
        button.Enabled = false;
        try
        {
            descriptor.Apply("");
        }
        catch (Exception ex)
        {
            new AlertDialog.Builder(this)!
                .SetTitle(TranslateExtension.Get(descriptor.LabelKey))!
                .SetMessage(ex.Message)!
                .SetPositiveButton(AppStrings.Cancel, (_, _) => { })!
                .Show();
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private bool CanOpenMenu(SettingDescriptor descriptor, ResolvedSetting resolved) =>
        !descriptor.ReportedByWheel && resolved.Origin != SettingOrigin.Factory;

    /// <summary>Same two commands and the same "one layer down, not straight to factory" semantics as the MAUI original's OpenRowMenu.</summary>
    private void ShowRowMenu(SettingDescriptor descriptor, ResolvedSetting resolved)
    {
        bool overridden = resolved.IsOverridden;
        string restore = overridden ? AppStrings.SettingsUseGlobal : AppStrings.SettingsUseFactory;
        string[] actions = overridden ? [restore, AppStrings.SettingsMakeGlobal] : [restore];
        string origin = overridden ? AppStrings.SettingsOverridden : AppStrings.SettingsGlobalValue;

        new AlertDialog.Builder(this)!
            .SetTitle($"{TranslateExtension.Get(descriptor.LabelKey)} — {origin}")!
            .SetItems(actions, (_, e) =>
            {
                string chosen = actions[e.Which];
                if (chosen == restore && overridden) _binder.ClearOverride(descriptor);
                else if (chosen == restore) _binder.ClearGlobal(descriptor);
                else if (chosen == AppStrings.SettingsMakeGlobal) _binder.PromoteToGlobal(descriptor);
                Rebuild();
            })!
            .SetNegativeButton(AppStrings.Cancel, (_, _) => { })!
            .Show();
    }

    private void Commit(SettingDescriptor descriptor, string value)
    {
        _binder.Set(descriptor, value);
        Rebuild();
    }

    // ---- Разметка ---------------------------------------------------------------------------

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        root.SetBackgroundColor(this.PageBackground());

        root.AddView(BuildScopeRow());
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
