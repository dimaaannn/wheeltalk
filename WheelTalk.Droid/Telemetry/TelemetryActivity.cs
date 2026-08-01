using System.Globalization;
using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Ports;
using WheelTalk.Core.Services;
using WheelTalk.Droid.Resources.Strings;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Droid.Ui;
using WheelTalk.Droid.Wheel;

using WheelTalk.Droid.App;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Settings;

namespace WheelTalk.Droid.Telemetry;

/// <summary>
/// «Данные»: каждое поле декодированного снэпшота плюс банки обоих пакетов BMS — по этому экрану
/// видно, что на самом деле отдаёт декодер, поэтому ничего не отфильтровано. Портировано с эталона
/// <c>WheelTalk.App/Pages/TelemetryPage.xaml(.cs)</c> (опись §1.3, §5): свайп вправо и кнопка
/// «На главный» оба ведут назад.
/// <para>
/// Вход — команда «Данные» в шторке главного экрана. Свайп влево, который вёл сюда раньше, убран
/// 31.07.2026 как бесполезный жест.
/// </para>
/// </summary>
[Activity]
public sealed class TelemetryActivity : Activity
{

    /// <summary>Ячеек в ряду. Шесть влезают на 720-пиксельный экран без переноса.</summary>
    private const int CellsPerRow = 6;

    private WheelSession _session = null!;
    private ITransport _transport = null!;
    private TimeProvider _timeProvider = null!;

    /// <summary>
    /// Таблица величин — общая с плеером (<see cref="PlaybackActivity"/>): там те же поля, только
    /// источник записанный, а не живой.
    /// </summary>
    private readonly TelemetryTable _table = new();

    private readonly TextView[][] _cells = [[], []];
    private int[] _cellCounts = [-1, -1];

    private TextView _statusLabel = null!;
    private LinearLayout _cellsLayout = null!;
    private Color _plain;

    private TelemetryRate? _rate;
    private IDisposable? _subscription;
    private IDisposable? _stateSubscription;

    private GestureDetector _gestureDetector = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Заголовок ставится кодом, а не атрибутом [Activity]: в атрибут можно положить только
        // константу, а строки живут в ресурсах (план 6, фаза 1 — вшитых строк не осталось).
        Title = string.Format(CultureInfo.CurrentCulture, AppStrings.ScreenTitleFormat, AppStrings.ScreenTitleTelemetry);

        _session = MainApplication.Services.GetRequiredService<WheelSession>();
        _transport = MainApplication.Services.GetRequiredService<ITransport>();
        _timeProvider = MainApplication.Services.GetRequiredService<TimeProvider>();

        _plain = UiKit.PlainText(this);
        _gestureDetector = new GestureDetector(this, new SwipeRightListener(GoBack));

        var root = BuildLayout();
        SetContentView(root);
        EdgeToEdge.Apply(this, root);
    }

    protected override void OnStart()
    {
        base.OnStart();

        // «Не гасить экран» — одна настройка на все экраны, где человек смотрит и думает: иначе она
        // держала бы экран на панели и отпускала здесь, в одном свайпе от неё. Ставится на OnStart,
        // а не на OnCreate: переключить её могли кнопкой в шторке уже после того, как этот экран
        // создался и ушёл в стек.
        // Поверх замка этот экран **не показывается** и не должен: там работает панель и её шторка,
        // а всё остальное — после разблокировки (план 16, MainActivity.OpenScreen).
        if (MainApplication.Services.GetRequiredService<IOptions<ScreenOptions>>().Value.KeepOn)
        {
            Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        }
        else
        {
            Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
        }

        if (_session.LastSnapshot is { } snapshot) Render(snapshot);

        // Каждый снэпшот рисуется без прореживания: на обоих поддержанных колёсах поток идёт около
        // 5 Гц (собственная частота телеметрии колеса), и сэмплирование на этой же частоте только
        // теряло бы кадры без всякой экономии — перенесено из эталона как есть.
        _rate = new TelemetryRate(_transport, _timeProvider);
        _subscription = _session.Telemetry.Subscribe(s => RunOnUiThread(() => Render(s)));
        _stateSubscription = _session.State.Subscribe(_ => RunOnUiThread(ShowStatus));
        ShowStatus();
    }

    protected override void OnStop()
    {
        _subscription?.Dispose();
        _subscription = null;
        _stateSubscription?.Dispose();
        _stateSubscription = null;
        _rate?.Dispose();
        _rate = null;

        base.OnStop();
    }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        if (ev is not null) _gestureDetector.OnTouchEvent(ev);
        return base.DispatchTouchEvent(ev);
    }

    private void GoBack() => Finish();

    /// <summary>Values are left as they are when the link drops — only the status line changes.</summary>
    private void ShowStatus()
    {
        _statusLabel.SetText(_session.CurrentState switch
        {
            // Протокол опознаётся первым кадром, поэтому в первые доли секунды его ещё нет.
            ConnectionState.Connected => $"{_session.Protocol?.ToString() ?? "…"} · {_rate?.Describe()}",
            ConnectionState.Connecting => AppStrings.StateConnecting,
            ConnectionState.Reconnecting => string.Format(AppStrings.TelemetryNoLink, _session.Address, _rate?.Snapshots),
            _ => AppStrings.TelemetryNoConnection,
        });
    }

    private void Render(TelemetrySnapshot snapshot)
    {
        _rate?.CountSnapshot();
        _table.Show(snapshot);

        ShowCells(snapshot);
        ShowStatus();
    }

    /// <summary>
    /// Все банки обоих пакетов. Сколько банок, говорит только колесо: сетка строится по первому
    /// непустому пакету и не пересобирается — перенесено из эталона как есть, включая причину в
    /// комментарии (счёт берётся у пакета, а не по ненулевым значениям).
    /// </summary>
    private void ShowCells(TelemetrySnapshot snapshot)
    {
        SmartBms[] packs = [snapshot.Bms1, snapshot.Bms2];
        int[] counts = [.. packs.Select(p => p.CellCount)];

        if (!counts.SequenceEqual(_cellCounts)) BuildCells(counts);

        for (int pack = 0; pack < packs.Length; pack++)
        {
            double min = packs[pack].MinCell;
            double max = packs[pack].MaxCell;

            for (int i = 0; i < _cells[pack].Length; i++)
            {
                double volts = packs[pack].Cells[i];
                var label = _cells[pack][i];
                label.SetText($"{i + 1,2}:{volts:F3}");

                var color = max - min < 0.001 ? _plain
                    : volts <= min ? Color.OrangeRed
                    : volts >= max ? Color.MediumSeaGreen
                    : _plain;
                if (label.CurrentTextColor != color) label.SetTextColor(color);
            }
        }
    }

    private void BuildCells(int[] counts)
    {
        _cellCounts = counts;
        _cellsLayout.RemoveAllViews();

        for (int pack = 0; pack < counts.Length; pack++)
        {
            if (counts[pack] == 0)
            {
                _cells[pack] = [];
                continue;
            }

            var header = new TextView(this) { Text = string.Format(AppStrings.TelemetryPackCells, pack + 1, counts[pack]) };
            header.SetTextSize(ComplexUnitType.Sp, 13);
            header.Alpha = 0.7f;
            _cellsLayout.AddView(header);

            var labels = new TextView[counts[pack]];
            LinearLayout? row = null;
            for (int i = 0; i < counts[pack]; i++)
            {
                if (i % CellsPerRow == 0)
                {
                    row = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
                    _cellsLayout.AddView(row, new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(2) });
                }

                var cell = new TextView(this) { Text = "—" };
                cell.SetTextSize(ComplexUnitType.Sp, 11);
                cell.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal);
                labels[i] = cell;
                row!.AddView(cell, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            }

            // Последняя неполная строка добивается пустыми ячейками того же веса — иначе её колонки
            // растянулись бы шире полных строк и банки перестали бы стоять друг под другом.
            int trailing = counts[pack] % CellsPerRow;
            if (trailing != 0 && row is not null)
            {
                for (int i = trailing; i < CellsPerRow; i++)
                {
                    row.AddView(new TextView(this), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
                }
            }

            _cells[pack] = labels;
        }
    }

    // ---- Разметка ---------------------------------------------------------------------------

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        root.SetBackgroundColor(this.PageBackground());
        int pad = this.Dp(16);
        root.SetPadding(pad, pad, pad, pad);

        _statusLabel = new TextView(this) { Text = AppStrings.TelemetryWaiting };
        _statusLabel.SetTextColor(_plain);
        _statusLabel.SetTextSize(ComplexUnitType.Sp, 14);
        root.AddView(_statusLabel);

        var buttons = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        var backButton = UiKit.CreateButton(this, AppStrings.TelemetryBack);
        backButton.Click += (_, _) => GoBack();
        buttons.AddView(backButton, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var settingsButton = UiKit.CreateButton(this, AppStrings.SettingsOpen);
        settingsButton.Click += (_, _) => StartActivity(new Android.Content.Intent(this, typeof(SettingsActivity)));
        var settingsParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) { LeftMargin = this.Dp(8) };
        buttons.AddView(settingsButton, settingsParams);

        root.AddView(buttons, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(12) });

        var scroll = new ScrollView(this);
        var content = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };

        content.AddView(_table.Build(this));

        _cellsLayout = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        content.AddView(_cellsLayout, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(16) });

        scroll.AddView(content);
        root.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1f) { TopMargin = this.Dp(12) });

        return root;
    }

    /// <summary>
    /// Свайп вправо по всему экрану — «назад». Парного свайпа влево на главном экране больше нет:
    /// убран 31.07.2026 как бесполезный, и входа на этот экран сейчас нет вовсе.
    /// </summary>
    private sealed class SwipeRightListener(Action onSwipeRight) : GestureDetector.SimpleOnGestureListener
    {
        private const int MinDistanceDp = 60;
        private const int MinVelocity = 200;

        public override bool OnFling(MotionEvent? e1, MotionEvent? e2, float velocityX, float velocityY)
        {
            if (e1 is null || e2 is null) return false;

            float dx = e2.GetX() - e1.GetX();
            float dy = e2.GetY() - e1.GetY();
            if (dx > MinDistanceDp && Math.Abs(dx) > Math.Abs(dy) && Math.Abs(velocityX) > MinVelocity)
            {
                onSwipeRight();
                return true;
            }

            return false;
        }
    }
}
