using System.Globalization;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Logging;
using WheelTalk.Droid.Resources.Strings;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Droid.Ui;
using WheelTalk.Storage;

using WheelTalk.Droid.App;
using WheelTalk.Droid.Rides;

namespace WheelTalk.Droid.Recording;

/// <summary>
/// Что пишется прямо сейчас и чем это включается. Портировано с эталона
/// <c>WheelTalk.App/Pages/RecordingPage.xaml(.cs)</c> (опись §1.3, §5): состояние истории поездки
/// обновляется раз в секунду (в MAUI — <c>Dispatcher.CreateTimer()</c>, здесь — <see cref="Handler"/>
/// на главном Looper'е), два переключателя пишут через <see cref="UserSettingsStore"/> — тот же
/// писатель, что у эталона.
/// </summary>
[Activity]
public sealed class RecordingActivity : Activity
{
    private const long TickIntervalMs = 1000;

    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly Action _tick;

    private RideRecorder _recorder = null!;
    private RawFrameRecorder _rawFrames = null!;
    private LoggingOptions _options = null!;
    private StorageOptions _storage = null!;
    private UserSettingsStore _settings = null!;

    private TextView _rideStateLabel = null!;
    private TextView _rideFileLabel = null!;
    private Button _recordButton = null!;
    private RadioGroup _telemetryGroup = null!;
    private RadioButton _telemetryAlways = null!;
    private RadioButton _telemetryRideOnly = null!;
    private RadioButton _telemetryNever = null!;
    private TextView _telemetryHint = null!;
    private TextView _telemetryRetentionLabel = null!;
    private Switch _autoStartSwitch = null!;
    private Switch _waitForMovingSwitch = null!;
    private TextView _autoStartHint = null!;
    private Switch _rawDumpSwitch = null!;
    private TextView _rawStateLabel = null!;
    private TextView _folderLabel = null!;

    /// <summary>Последний ненулевой порог — чтобы тумблер «ждать движения» возвращал выбранное число, а не заводское.</summary>
    private double _lastAboveKmh = 7;

    private bool _running;

    public RecordingActivity() => _tick = Tick;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Заголовок ставится кодом, а не атрибутом [Activity]: в атрибут можно положить только
        // константу, а строки живут в ресурсах (план 6, фаза 1 — вшитых строк не осталось).
        Title = string.Format(CultureInfo.CurrentCulture, AppStrings.ScreenTitleFormat, AppStrings.RecordingTitle);

        _recorder = MainApplication.Services.GetRequiredService<RideRecorder>();
        _rawFrames = MainApplication.Services.GetRequiredService<RawFrameRecorder>();
        _options = MainApplication.Services.GetRequiredService<IOptions<LoggingOptions>>().Value;
        // Срок хранения — дело WheelTalk.Storage, а не логирования: читается отдельным объектом
        // настроек, не переносится в LoggingOptions. Сведены только на экране (план 23 §5.7:
        // «человек видит одно: сколько и когда пишется»), не в коде.
        _storage = MainApplication.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
        _settings = MainApplication.Services.GetRequiredService<UserSettingsStore>();

        var root = BuildLayout();
        SetContentView(root);
        EdgeToEdge.Apply(this, root);
    }

    protected override void OnStart()
    {
        base.OnStart();

        _autoStartSwitch.Checked = _options.AutoStartRide;
        _rawDumpSwitch.Checked = _options.RawDump;
        if (_options.AutoStartAboveKmh > 0) _lastAboveKmh = _options.AutoStartAboveKmh;
        ShowTelemetryRecording();
        ShowAutoStart();
        _folderLabel.SetText(RideFiles.Root);

        Show();

        // Строк прибывает по пять в секунду, а событие о старте — одно; проще перерисовывать раз в
        // секунду, пока экран открыт, чем заводить отдельную подписку на каждую запись.
        _running = true;
        _handler.PostDelayed(_tick, TickIntervalMs);
    }

    protected override void OnStop()
    {
        _running = false;
        _handler.RemoveCallbacks(_tick);
        base.OnStop();
    }

    private void Tick()
    {
        if (!_running) return;
        Show();
        _handler.PostDelayed(_tick, TickIntervalMs);
    }

    /// <summary>«Никогда» не пишет ничего вообще — раздетый общей записи нечего ни начинать вручную, ни автоматически (план 23 §5.7).</summary>
    private bool CanRecordTelemetry => _options.TelemetryRecording != TelemetryRecording.Never;

    private void Show()
    {
        _recordButton.SetText(_recorder.IsRecording ? AppStrings.RecordingStop : AppStrings.RecordingStart);
        _recordButton.Enabled = CanRecordTelemetry;
        ShowRawDump();

        if (!_recorder.IsRecording)
        {
            _rideStateLabel.SetText(AppStrings.RecordingIdle);
            _rideFileLabel.SetText("");
            return;
        }

        _rideStateLabel.SetText(_recorder.RideId == 0
            ? AppStrings.RecordingWaiting
            : string.Format(AppStrings.RecordingActive, _recorder.RowsWritten));
        _rideFileLabel.SetText(_recorder.RideId == 0 ? "" : $"#{_recorder.RideId}");
    }

    private void ShowRawDump()
    {
        _rawStateLabel.SetText(!_rawFrames.IsRecording
            ? AppStrings.SwitchOff
            : _rawFrames.FileName ?? AppStrings.RecordingWaiting);
    }

    private void OnRecordClicked()
    {
        _recorder.Toggle();
        Show();
    }

    private void OnRidesClicked() => StartActivity(new Intent(this, typeof(RidesActivity)));

    private void OnAutoStartToggled(bool value)
    {
        if (value == _options.AutoStartRide) return;

        _options.AutoStartRide = value;
        _settings.SaveLogging(_options);
        ShowAutoStart();
    }

    private void OnWaitForMovingToggled(bool value)
    {
        double wanted = value ? _lastAboveKmh : 0;
        if (Math.Abs(wanted - _options.AutoStartAboveKmh) < 0.01) return;

        // Выключая, запоминаем порог: включат обратно — вернётся выбранное число, а не заводское.
        if (!value && _options.AutoStartAboveKmh > 0) _lastAboveKmh = _options.AutoStartAboveKmh;

        _options.AutoStartAboveKmh = wanted;
        _settings.SaveLogging(_options);
        ShowAutoStart();
    }

    /// <summary>
    /// Подпись под «Включать запись автоматически» обязана говорить, что случится на самом деле:
    /// с порогом это «когда поедет быстрее N», без порога — «при подключении». Здесь же гасится
    /// тумблер порога, когда автозапись выключена совсем: ждать начала движения для записи,
    /// которая не включается сама, нечего.
    /// </summary>
    private void ShowAutoStart()
    {
        bool waits = _options.AutoStartAboveKmh > 0;

        _autoStartHint.Text = waits
            ? string.Format(CultureInfo.CurrentCulture, AppStrings.RecordingAutoStartMovingHint, _options.AutoStartAboveKmh)
            : AppStrings.RecordingAutoStartHint;

        if (_waitForMovingSwitch.Checked != waits) _waitForMovingSwitch.Checked = waits;
        _autoStartSwitch.Enabled = CanRecordTelemetry;
        _waitForMovingSwitch.Enabled = CanRecordTelemetry && _options.AutoStartRide;
    }

    /// <summary>
    /// Отражает выбранное положение переключателя (радиокнопка) и подпись под ним — то же деление
    /// «состояние → подпись», что у <see cref="ShowAutoStart"/>. Радиокнопки читаются событием
    /// самой <see cref="RadioGroup"/>, а не отдельных <see cref="RadioButton"/>: группа сама
    /// подписывается на каждую кнопку, и второй слушатель на кнопке эту подписку перебивает.
    /// </summary>
    private void ShowTelemetryRecording()
    {
        var current = _options.TelemetryRecording switch
        {
            TelemetryRecording.Always => _telemetryAlways,
            TelemetryRecording.Never => _telemetryNever,
            _ => _telemetryRideOnly,
        };
        if (!current.Checked) current.Checked = true;

        _telemetryHint.Text = _options.TelemetryRecording switch
        {
            TelemetryRecording.Always => AppStrings.RecordingTelemetryAlwaysHint,
            TelemetryRecording.Never => AppStrings.RecordingTelemetryNeverHint,
            _ => AppStrings.RecordingTelemetryRideOnlyHint,
        };

        _telemetryRetentionLabel.Text = string.Format(CultureInfo.CurrentCulture,
            AppStrings.RecordingTelemetryRetention, (int)Math.Round(_storage.TelemetryRetention.TotalHours));
    }

    private void OnTelemetryRecordingChanged(TelemetryRecording value)
    {
        if (value == _options.TelemetryRecording) return;

        _options.TelemetryRecording = value;
        _settings.SaveLogging(_options);
        ShowTelemetryRecording();
        ShowAutoStart();
        Show();
    }

    private void OnRawDumpToggled(bool value)
    {
        if (value == _options.RawDump) return;

        _options.RawDump = value;
        _settings.SaveLogging(_options);
        _rawFrames.Apply();
        ShowRawDump();
    }

    // ---- Разметка ---------------------------------------------------------------------------

    private View BuildLayout()
    {
        var scroll = new ScrollView(this);
        scroll.SetBackgroundColor(this.PageBackground());

        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        int pad = this.Dp(16);
        root.SetPadding(pad, pad, pad, pad);

        root.AddView(RideSection());
        root.AddView(Spaced(RecordButtonView()));
        root.AddView(Spaced(RidesButtonView()));
        root.AddView(Spaced(TelemetryRecordingSection()));
        root.AddView(Spaced(AutoStartRow()));
        root.AddView(Spaced(WaitForMovingRow()));
        root.AddView(Spaced(UiKit.Divider(this)));
        root.AddView(Spaced(RawDumpSection()));
        root.AddView(Spaced(UiKit.Divider(this)));
        root.AddView(Spaced(FolderSection()));

        scroll.AddView(root);
        return scroll;
    }

    private View Spaced(View view)
    {
        var p = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = this.Dp(18),
        };
        view.LayoutParameters = p;
        return view;
    }

    private View RideSection()
    {
        var section = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };

        var title = Bold(AppStrings.RecordingRideSection, 17);
        section.AddView(title);

        _rideStateLabel = new TextView(this) { Text = "—" };
        _rideStateLabel.SetTextSize(ComplexUnitType.Sp, 14);
        _rideStateLabel.SetTextColor(UiKit.PlainText(this));
        section.AddView(_rideStateLabel);

        _rideFileLabel = new TextView(this) { Text = "" };
        _rideFileLabel.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal);
        _rideFileLabel.SetTextSize(ComplexUnitType.Sp, 12);
        _rideFileLabel.Alpha = 0.7f;
        section.AddView(_rideFileLabel);

        return section;
    }

    private View RecordButtonView()
    {
        _recordButton = UiKit.CreateButton(this, AppStrings.RecordingStart);
        _recordButton.Click += (_, _) => OnRecordClicked();
        return _recordButton;
    }

    private View RidesButtonView()
    {
        var button = UiKit.CreateButton(this, AppStrings.RidesTitle);
        button.Click += (_, _) => OnRidesClicked();
        return button;
    }

    /// <summary>
    /// Три положения записи потока (план 23 §5.7): подпись под группой объясняет выбранное —
    /// названия кнопок сами по себе этого не несут (особенно у «Всегда», где важен срок хранения).
    /// </summary>
    private View TelemetryRecordingSection()
    {
        var section = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };

        var title = new TextView(this) { Text = AppStrings.RecordingTelemetryTitle };
        title.SetTextSize(ComplexUnitType.Sp, 15);
        title.SetTextColor(UiKit.PlainText(this));
        section.AddView(title);

        _telemetryAlways = TelemetryOption(AppStrings.RecordingTelemetryAlways);
        _telemetryRideOnly = TelemetryOption(AppStrings.RecordingTelemetryRideOnly);
        _telemetryNever = TelemetryOption(AppStrings.RecordingTelemetryNever);

        _telemetryGroup = new RadioGroup(this) { Orientation = Android.Widget.Orientation.Vertical };
        _telemetryGroup.AddView(_telemetryAlways);
        _telemetryGroup.AddView(_telemetryRideOnly);
        _telemetryGroup.AddView(_telemetryNever);
        _telemetryGroup.CheckedChange += (_, e) => OnTelemetryRecordingChanged(
            e.CheckedId == _telemetryAlways.Id ? TelemetryRecording.Always
            : e.CheckedId == _telemetryNever.Id ? TelemetryRecording.Never
            : TelemetryRecording.RideOnly);
        section.AddView(_telemetryGroup, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(4) });

        _telemetryHint = new TextView(this);
        _telemetryHint.SetTextSize(ComplexUnitType.Sp, 12);
        _telemetryHint.Alpha = 0.7f;
        section.AddView(_telemetryHint, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(4) });

        // Срок хранения — не про выбранное положение, а про весь поток вообще (§5.1 п. 6: чистка
        // не смотрит на разметку), поэтому строка одна и висит под группой всегда, а не только у
        // «Всегда» — единственного места, которое раньше вообще называло срок.
        _telemetryRetentionLabel = new TextView(this);
        _telemetryRetentionLabel.SetTextSize(ComplexUnitType.Sp, 12);
        _telemetryRetentionLabel.Alpha = 0.7f;
        section.AddView(_telemetryRetentionLabel, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(2) });

        return section;
    }

    /// <summary>
    /// A bare id, not the default (unset) one <see cref="RadioGroup.CheckedChange"/> would otherwise
    /// report as -1 for every button — <see cref="View.GenerateViewId"/> gives each option something
    /// the group's event can tell apart.
    /// </summary>
    private RadioButton TelemetryOption(string text)
    {
        var button = new RadioButton(this) { Id = View.GenerateViewId(), Text = text };
        button.SetTextColor(UiKit.PlainText(this));
        return button;
    }

    private View AutoStartRow()
    {
        _autoStartSwitch = new Switch(this);
        _autoStartSwitch.CheckedChange += (_, e) => OnAutoStartToggled(e.IsChecked);

        _autoStartHint = new TextView(this);
        _autoStartHint.SetTextSize(ComplexUnitType.Sp, 12);
        _autoStartHint.Alpha = 0.7f;

        return SwitchRow(_autoStartSwitch, AppStrings.RecordingAutoStart, _autoStartHint);
    }

    /// <summary>
    /// «Ждать начала движения» — порог <see cref="LoggingOptions.AutoStartAboveKmh"/> как
    /// переключатель: у оригинала это ползунок, но выбирают на нём всегда одно из двух — писать с
    /// подключения (ноль) или дождаться, когда колесо поедет. Само число правится в настройках
    /// файлом; тумблер помнит его в <see cref="_lastAboveKmh"/>, чтобы выключение и включение не
    /// теряли выбранный порог.
    /// </summary>
    private View WaitForMovingRow()
    {
        _waitForMovingSwitch = new Switch(this);
        _waitForMovingSwitch.CheckedChange += (_, e) => OnWaitForMovingToggled(e.IsChecked);

        return SwitchRow(_waitForMovingSwitch, AppStrings.RecordingWaitForMoving, hint: null);
    }

    /// <summary>Строка «тумблер + подпись (+ подсказка)» — общая у обоих переключателей этого экрана.</summary>
    private View SwitchRow(Switch toggle, string labelText, TextView? hint)
    {
        var row = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.AddView(toggle);

        var texts = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        var textParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = this.Dp(12) };

        var label = new TextView(this) { Text = labelText };
        label.SetTextSize(ComplexUnitType.Sp, 15);
        label.SetTextColor(UiKit.PlainText(this));
        texts.AddView(label);

        if (hint is not null) texts.AddView(hint);

        row.AddView(texts, textParams);
        return row;
    }

    private View RawDumpSection()
    {
        var section = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };

        section.AddView(Bold(AppStrings.RecordingRawSection, 17));

        var row = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);

        _rawDumpSwitch = new Switch(this);
        _rawDumpSwitch.CheckedChange += (_, e) => OnRawDumpToggled(e.IsChecked);
        row.AddView(_rawDumpSwitch);

        _rawStateLabel = new TextView(this) { Text = "—" };
        _rawStateLabel.SetTextSize(ComplexUnitType.Sp, 14);
        _rawStateLabel.SetTextColor(UiKit.PlainText(this));
        var stateParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = this.Dp(12) };
        row.AddView(_rawStateLabel, stateParams);

        section.AddView(row, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(4) });

        var hint = new TextView(this) { Text = AppStrings.RecordingRawHint };
        hint.SetTextSize(ComplexUnitType.Sp, 12);
        hint.Alpha = 0.7f;
        section.AddView(hint, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(4) });

        return section;
    }

    private View FolderSection()
    {
        var section = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };

        section.AddView(Bold(AppStrings.RecordingFolderSection, 17));

        _folderLabel = new TextView(this) { Text = "" };
        _folderLabel.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal);
        _folderLabel.SetTextSize(ComplexUnitType.Sp, 11);
        _folderLabel.Alpha = 0.7f;
        section.AddView(_folderLabel);

        return section;
    }

    private TextView Bold(string text, float sp)
    {
        var label = new TextView(this) { Text = text };
        label.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        label.SetTextSize(ComplexUnitType.Sp, sp);
        label.SetTextColor(UiKit.PlainText(this));
        return label;
    }
}
