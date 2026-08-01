using System.Globalization;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WheelTalk.Core.Alerts;
using WheelTalk.Core.Playback;
using WheelTalk.Core.Services;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Layouts;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.App;
using WheelTalk.Droid.Main;
using WheelTalk.Droid.Telemetry;
using WheelTalk.Droid.Resources.Strings;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Dashboard.Droid.Widgets;
using WheelTalk.Droid.Ui;
using WheelTalk.Storage;

namespace WheelTalk.Droid.Playback;

/// <summary>
/// Проигрыватель записанной поездки: та же панель, что на главном экране, плюс полоса прокрутки,
/// пауза и множитель хода. Открывается с экрана поездки.
/// <para>
/// Экран ничего не знает про связь с колесом и ничего в ней не меняет: источник кадров —
/// <see cref="RidePlayer"/> поверх строк базы, а не <c>WheelSession</c>. Поэтому воспроизведение
/// не рвёт соединение, не пишет поездку и не поднимает тревоги — за это отвечают подписки уровня
/// приложения, а они смотрят на сессию, не сюда.
/// </para>
/// <para>
/// След поездки здесь <b>свой</b>, а не общий с главным экраном: <c>RideTrace</c> копит минимумы,
/// максимумы и опорное напряжение, и подмешать в него чужую поездку значило бы испортить показания
/// живой — той, которая едет прямо сейчас.
/// </para>
/// </summary>
[Activity(
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class PlaybackActivity : Activity
{
    public const string ExtraRideId = "ride_id";

    /// <summary>Множители хода. Обратно медленнее настоящего тоже нужно: на пределе ШИМ секунда решает.</summary>
    private static readonly double[] Speeds = [0.5, 1, 2, 4];

    /// <summary>Доля высоты панели под кегль времени, и его пределы: мельче не прочитать, крупнее — спорит со скоростью.</summary>
    private const float StampOfHeight = 0.05f;

    private const float MinStampSp = 20;
    private const float MaxStampSp = 40;

    /// <summary>
    /// Сколько высоты экрана отдано списку данных. Панель остаётся главной — ленты в разборе
    /// нужны (просадка и тренд видны формой, а не числом), но список должен быть виден без
    /// прокрутки хотя бы наполовину.
    /// </summary>
    private const float DataShare = 0.36f;

    private RideExporter _exporter = null!;
    private DashboardOptions _options = null!;
    private TimeProvider _timeProvider = null!;

    private RideTrace _trace = null!;
    private RidePlayer? _player;
    private IDisposable? _telemetry;

    private TwinTapesDashboard _dashboard = null!;
    private SeekBar _scrubber = null!;
    private Button _playButton = null!;
    private Button _speedButton = null!;
    private TextView _timeLabel = null!;
    private TextView _statusLabel = null!;
    private TextView _stampLabel = null!;

    private AlertOptions _alertOptions = null!;

    /// <summary>Тот же список величин, что на экране «Данные», — источник другой, поля те же.</summary>
    private readonly TelemetryTable _table = new();

    private int _speedIndex = 1;
    private bool _scrubbing;
    private bool _crossesMidnight;
    /// <summary>
    /// Куда перематывали в прошлый раз по ходу протяжки; <c>null</c> — протяжка ещё не начиналась.
    /// Именно <c>null</c>, а не «невозможное значение»: <c>TimeSpan.MinValue</c> в роли пустого
    /// переполнял вычитание, исключение проглатывалось обработчиком касания, и перемотка молча не
    /// работала вовсе.
    /// </summary>
    private TimeSpan? _lastScrubSeek;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Заголовок ставится кодом, а не атрибутом [Activity]: в атрибут можно положить только
        // константу, а строки живут в ресурсах (план 6, фаза 1 — вшитых строк не осталось).
        Title = string.Format(CultureInfo.CurrentCulture, AppStrings.ScreenTitleFormat, AppStrings.PlaybackTitle);

        _exporter = MainApplication.Services.GetRequiredService<RideExporter>();
        _options = MainApplication.Services.GetRequiredService<DashboardOptions>();
        _alertOptions = MainApplication.Services.GetRequiredService<IOptions<AlertOptions>>().Value;
        _timeProvider = MainApplication.Services.GetRequiredService<TimeProvider>();
        _trace = new RideTrace(_timeProvider);

        // Источник вместо покадрового зеркала (план 19 Б5): след поездки сам берёт живую настройку
        // сглаживания у панели, когда она ему понадобится.
        _trace.SmoothingSecondsSource = () => _options.SmoothingSeconds;

        // Разбор записи — это «смотрю и думаю», а не «трогаю экран»: без этого телефон засыпает
        // на середине поездки. Ручка та же, что на главном экране («Отображение» → «Не гасить
        // экран»): одна настройка на оба места, иначе она гасила бы экран в одном и не гасила в
        // другом.
        if (MainApplication.Services.GetRequiredService<IOptions<ScreenOptions>>().Value.KeepOn)
        {
            Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        }

        SetContentView(BuildLayout());

        long rideId = Intent?.GetLongExtra(ExtraRideId, 0) ?? 0;
        _ = LoadAsync(rideId);
    }

    /// <summary>
    /// Поездка читается в фоне: час записи — это тысячи строк, и разбирать их на потоке разметки
    /// значило бы показать чёрный экран вместо панели.
    /// </summary>
    private async Task LoadAsync(long rideId)
    {
        try
        {
            var samples = await Task.Run(() => _exporter.Samples(rideId));
            if (IsFinishing || IsDestroyed) return;

            if (samples.Count == 0)
            {
                _statusLabel.SetText(AppStrings.PlaybackEmpty);
                return;
            }

            _player = new RidePlayer(samples, _timeProvider) { Speed = Speeds[_speedIndex] };
            _player.Changed += OnPlayerChanged;

            // Перемотка обнуляет след: минимумы и максимумы на лентах набраны из другого места
            // записи, а после прыжка назад — вообще из того, которое ещё не наступило
            // (docs/playback-plan.md §2.2).
            _player.Jumped += () => RunOnUiThread(_trace.Reset);

            _telemetry = _player.Telemetry.Subscribe(s => RunOnUiThread(() =>
            {
                _trace.Push(s);

                // Тревожные полосы в записи показываются глазами, но не звучат: запись открывают
                // чаще всего затем, чтобы разобрать опасный момент, и погашенные полосы врали бы
                // о нём молчанием. Формула — общая с живой тревогой, второй копии нет.
                double intensity = AlertEvaluator.Intensity(Math.Abs(_trace.Pwm), _alertOptions);
                _dashboard.Show(DashboardFrame.From(s, _trace, intensity));
                _table.Show(s);
            }));

            _crossesMidnight = samples[0].Stamp.Date != samples[^1].Stamp.Date;
            _scrubber.Max = (int)_player.Duration.TotalMilliseconds;
            _scrubber.Enabled = true;
            _playButton.Enabled = true;
            _statusLabel.SetShown(false);

            // Первый кадр показывается сразу, до нажатия «Пуск»: пустая панель под полосой прокрутки
            // читается как «поездка не открылась».
            _player.Seek(TimeSpan.Zero);
            ShowPosition();
        }
        catch (Exception ex)
        {
            _statusLabel.SetText(string.Format(System.Globalization.CultureInfo.CurrentCulture,
                AppStrings.PlaybackFailed, ex.Message));
        }
    }

    protected override void OnStop()
    {
        // Уход с экрана останавливает воспроизведение: проигрыватель, тикающий в кармане, тратит
        // батарею и по возвращении оказывается где-то далеко от того места, где его оставили.
        _player?.Pause();
        base.OnStop();
    }

    protected override void OnDestroy()
    {
        _telemetry?.Dispose();
        if (_player is not null) _player.Changed -= OnPlayerChanged;
        _player?.Dispose();
        base.OnDestroy();
    }

    private void OnPlayerChanged() => RunOnUiThread(ShowPosition);

    private void ShowPosition()
    {
        if (_player is null) return;

        if (!_scrubbing) _scrubber.Progress = (int)_player.Position.TotalMilliseconds;

        _timeLabel.SetText($"{Clock(_player.Position)} / {Clock(_player.Duration)}");
        _playButton.SetText(_player.IsPlaying ? AppStrings.PlaybackPause : AppStrings.PlaybackPlay);

        if (_player.Current is { } current) _stampLabel.SetText(Stamp(current.Stamp));
    }

    private static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    /// <summary>
    /// Настенное время кадра. Секунды обязательны: отсчёты идут пять раз в секунду, и без них
    /// время стояло бы на месте дюжину кадров подряд. Доли секунды не показываем — мигают и не
    /// читаются. Дата появляется, только если запись перешла через полночь: в остальных случаях
    /// она известна из карточки поездки и на панели была бы шумом.
    /// </summary>
    private string Stamp(DateTimeOffset at) =>
        _crossesMidnight
            ? at.ToString("d MMM HH:mm:ss", CultureInfo.CurrentCulture)
            : at.ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    private void OnPlayPauseClicked()
    {
        if (_player is null) return;

        if (_player.IsPlaying) _player.Pause();
        else _player.Play();
    }

    private void OnSpeedClicked()
    {
        _speedIndex = (_speedIndex + 1) % Speeds.Length;
        if (_player is not null) _player.Speed = Speeds[_speedIndex];
        _speedButton.SetText(SpeedCaption());
    }

    private string SpeedCaption()
    {
        double speed = Speeds[_speedIndex];
        return speed == (int)speed ? $"×{(int)speed}" : $"×{speed:0.#}";
    }

    // ---- Разметка -------------------------------------------------------------------------------

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        root.SetBackgroundColor(_options.Palette.Background);

        // Время кадра лежит НАД панелью отдельным TextView, а не внутри неё. Панель общая с главным
        // экраном, и надпись внутри неё стала бы новой ручкой в DashboardOptions, новым случаем в
        // отрисовке и риском для живого прибора ради чужого сценария (docs/playback-plan.md §1).
        var panel = new FrameLayout(this);
        _dashboard = new TwinTapesDashboard(this, _options);
        panel.AddView(_dashboard, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        _stampLabel = new TextView(this) { Text = "" };
        _stampLabel.SetTextColor(_options.Palette.Ink);
        _stampLabel.Gravity = GravityFlags.Center;
        panel.AddView(_stampLabel, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        { Gravity = GravityFlags.Top | GravityFlags.CenterHorizontal });

        // Кегль и место времени — от высоты панели, а не числом: панель в плеере делит экран со
        // списком данных, и на коротком экране фиксированный кегль либо наехал бы на скорость,
        // либо потерялся. Полосу над скоростью называет сама панель
        // (SpeedBlockDrawable.SpaceAboveSpeed), и время встаёт по её середине: воздух сверху и
        // снизу выходит одинаковый на любой высоте панели, а не только на полном экране, где
        // прежняя доля 3,5 % случайно попадала (playback-plan.md §0.2).
        _dashboard.LayoutChange += (_, e) =>
        {
            int height = e.Bottom - e.Top;
            if (height <= 0) return;

            float density = Resources!.DisplayMetrics!.Density;
            float sp = Math.Clamp(height * StampOfHeight / density, MinStampSp, MaxStampSp);
            if (Math.Abs(_stampLabel.TextSize / density - sp) > 0.5f)
            {
                _stampLabel.SetTextSize(ComplexUnitType.Sp, sp);
            }

            var lp = (FrameLayout.LayoutParams)_stampLabel.LayoutParameters!;
            float band = height * SpeedBlockDrawable.SpaceAboveSpeed;
            int wanted = Math.Max(0, (int)((band - _stampLabel.LineHeight) / 2));
            if (lp.TopMargin == wanted) return;

            lp.TopMargin = wanted;
            _stampLabel.LayoutParameters = lp;
        };

        root.AddView(panel, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1 - DataShare));

        // Список величин под панелью, во всю ширину. В центре панели ему места нет: колонка между
        // лентами — около сотни dp, и подробный список там выродился бы в те же две-три величины,
        // только мельче (docs/playback-plan.md §6). Экран «Данные» показывает этим же компонентом
        // живое колесо — поля обязаны совпадать, источник разный.
        var dataScroll = new ScrollView(this);
        dataScroll.SetBackgroundColor(_options.Palette.Background);
        int dataPad = this.Dp(12);
        dataScroll.SetPadding(dataPad, this.Dp(6), dataPad, 0);
        dataScroll.AddView(_table.Build(this, nameSp: 11, valueSp: 12, ink: _options.Palette.Ink));
        root.AddView(dataScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, DataShare));

        _statusLabel = new TextView(this) { Text = AppStrings.PlaybackLoading };
        _statusLabel.SetTextSize(ComplexUnitType.Sp, 13);
        _statusLabel.SetTextColor(Color.White);
        _statusLabel.Alpha = 0.7f;
        int pad = this.Dp(12);
        _statusLabel.SetPadding(pad, pad, pad, 0);
        root.AddView(_statusLabel);

        root.AddView(BuildTransport());

        var page = new FrameLayout(this);
        page.SetBackgroundColor(_options.Palette.Background);
        page.AddView(root);
        EdgeToEdge.Apply(this, page);
        return page;
    }

    /// <summary>Полоса управления: пуск-пауза, прокрутка, время, множитель хода.</summary>
    private View BuildTransport()
    {
        var bar = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        int pad = this.Dp(12);
        bar.SetPadding(pad, this.Dp(8), pad, pad);

        // Полоса прокрутки — самый частый жест на этом экране, и тонкая линия для пальца (тем более
        // в перчатке) — плохая цель. Вертикальные отступы поднимают область касания до тех же
        // 48 dp, что и у кнопок.
        _scrubber = new SeekBar(this) { Enabled = false, Max = 1 };
        int touch = this.Dp(14);
        _scrubber.SetPadding(_scrubber.PaddingLeft, touch, _scrubber.PaddingRight, touch);
        // Протяжка пальцем сыплет шагами по пикселю, и перемотка на каждый — это поиск, кадр и
        // сброс следа по десять раз в секунду впустую. Кадр под пальцем показывать надо, поэтому
        // шаги не отбрасываются, а прореживаются: четверть секунды записи — предел различимого
        // на глаз при протяжке (docs/playback-plan.md §2.4).
        _scrubber.ProgressChanged += (_, e) =>
        {
            if (!e.FromUser || _player is null) return;

            var wanted = TimeSpan.FromMilliseconds(e.Progress);
            if (_scrubbing && _lastScrubSeek is { } last
                && (wanted - last).Duration() < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            _lastScrubSeek = wanted;
            _player.Seek(wanted);
        };
        _scrubber.StartTrackingTouch += (_, _) => _scrubbing = true;
        _scrubber.StopTrackingTouch += (_, e) =>
        {
            _scrubbing = false;

            // Точное место — по отпусканию: прореживание выше могло не донести последние шаги.
            _lastScrubSeek = null;
            _player?.Seek(TimeSpan.FromMilliseconds(e.SeekBar!.Progress));
        };
        bar.AddView(_scrubber, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var row = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);

        _playButton = UiKit.CreateButton(this, AppStrings.PlaybackPlay);
        _playButton.Enabled = false;
        _playButton.Click += (_, _) => OnPlayPauseClicked();
        row.AddView(_playButton, new LinearLayout.LayoutParams(this.Dp(120), ViewGroup.LayoutParams.WrapContent));

        _timeLabel = new TextView(this) { Text = "0:00 / 0:00" };
        _timeLabel.SetTextSize(ComplexUnitType.Sp, 15);
        _timeLabel.SetTextColor(Color.White);
        _timeLabel.Gravity = GravityFlags.Center;
        row.AddView(_timeLabel, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        _speedButton = UiKit.CreateButton(this, SpeedCaption());
        _speedButton.Click += (_, _) => OnSpeedClicked();
        row.AddView(_speedButton, new LinearLayout.LayoutParams(this.Dp(84), ViewGroup.LayoutParams.WrapContent));

        bar.AddView(row, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(4) });

        return bar;
    }

    public static void Open(Context context, long rideId)
    {
        var intent = new Intent(context, typeof(PlaybackActivity));
        intent.PutExtra(ExtraRideId, rideId);
        context.StartActivity(intent);
    }
}
