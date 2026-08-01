using System.Globalization;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WheelTalk.Droid.Logging;
using WheelTalk.Droid.Resources.Strings;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Droid.Ui;
using WheelTalk.Storage;

using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.App;

namespace WheelTalk.Droid.Rides;

/// <summary>
/// Каждая записанная поездка, свежие сверху. Портировано с эталона
/// <c>WheelTalk.App/Pages/RidesPage.xaml(.cs)</c> (опись §1.3, §5): ничего не считается здесь —
/// итоги посчитаны один раз при закрытии поездки (план 8 §3.1), список лишь читает их.
/// <para>
/// Отличие от эталона (допустимо планом 12 §0.3): там открывает поездку тап по строке, а меню
/// действий — отдельная иконка «⋮». Здесь — тап открывает, долгий тап показывает меню: тот же
/// набор из двух действий, без второй зоны нажатия на тесной строке списка.
/// </para>
/// </summary>
[Activity]
public sealed class RidesActivity : Activity
{
    private RideExporter _exporter = null!;
    private RideStore _store = null!;
    private TimeProvider _timeProvider = null!;

    private readonly List<RideRow> _rides = [];
    private RideAdapter _adapter = null!;
    private TextView _statusLabel = null!;

    private bool _busy;
    private WheelOptions _wheel = null!;
    private WheelIdentity _identity = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _wheel = MainApplication.Services.GetRequiredService<IOptions<WheelOptions>>().Value;
        _identity = MainApplication.Services.GetRequiredService<WheelIdentity>();

        // Заголовок ставится кодом, а не атрибутом [Activity]: в атрибут можно положить только
        // константу, а строки живут в ресурсах (план 6, фаза 1 — вшитых строк не осталось).
        Title = string.Format(CultureInfo.CurrentCulture, AppStrings.ScreenTitleFormat, AppStrings.RidesTitle);

        _exporter = MainApplication.Services.GetRequiredService<RideExporter>();
        _store = MainApplication.Services.GetRequiredService<RideStore>();
        _timeProvider = MainApplication.Services.GetRequiredService<TimeProvider>();

        var root = BuildLayout();
        SetContentView(root);
        EdgeToEdge.Apply(this, root);
    }

    protected override void OnStart()
    {
        base.OnStart();

        // Перечитываем при каждом появлении, а не один раз: сюда возвращаются после удаления, после
        // экрана поездки и посреди записи, и в каждом из трёх случаев список уже другой.
        _ = Reload();
    }

    private async Task Reload()
    {
        _statusLabel.SetText(AppStrings.RidesLoading);

        var now = _timeProvider.GetLocalNow();
        var rows = await Task.Run(() => _exporter.Rides().Select(ride => Describe(ride, now)).ToList());

        _rides.Clear();
        _rides.AddRange(rows);
        _adapter.NotifyDataSetChanged();

        _statusLabel.SetText(rows.Count == 0
            ? AppStrings.RidesEmpty
            : string.Format(CultureInfo.CurrentCulture, AppStrings.RidesCount, rows.Count));
    }

    private RideRow Describe(RideSummary ride, DateTimeOffset now)
    {
        string details = ride.Totals is { } totals
            ? RideFormat.Summary(totals)
            : string.Format(CultureInfo.CurrentCulture, AppStrings.RidesRecording, ride.Rows);

        // Начало и конец, а не одно начало: по списку выбирают поездку, а «та, что после обеда до
        // пяти» — это два времени. Дистанция стоит первой в строке итогов под ними.
        return new RideRow(
            ride,
            RideFormat.Interval(ride.StartedAt, ride.EndedAt, now),
            RideFormat.WheelName(ride, _wheel, _identity),
            details);
    }

    private void OnRideTapped(RideRow row)
    {
        var intent = new Intent(this, typeof(RideActivity));
        intent.PutExtra(RideActivity.ExtraRideId, row.Ride.Id);
        StartActivity(intent);
    }

    /// <summary>The row's own commands. Delete is refused for the ride being written — it is not finished being written.</summary>
    private void OnRideLongPressed(RideRow row)
    {
        string[] actions = row.Ride.IsOpen
            ? [AppStrings.RidesExport]
            : [AppStrings.RidesExport, AppStrings.RidesDelete];

        new AlertDialog.Builder(this)!
            .SetTitle(row.When)!
            .SetItems(actions, (_, e) =>
            {
                if (actions[e.Which] == AppStrings.RidesExport) _ = Export(row.Ride);
                else if (actions[e.Which] == AppStrings.RidesDelete) _ = ConfirmAndDelete(row.Ride);
            })!
            .SetNegativeButton(AppStrings.Cancel, (_, _) => { })!
            .Show();
    }

    private async Task Export(RideSummary ride)
    {
        if (_busy) return;

        _busy = true;
        try
        {
            _statusLabel.SetText(AppStrings.RidesExporting);
            // Двадцать тысяч строк через форматирование — не то, что делают на потоке разметки.
            string path = await Task.Run(() => RideCsvExport.Write(_exporter, ride));
            _statusLabel.SetText(string.Format(
                CultureInfo.CurrentCulture, AppStrings.RecordingExportDone, Path.GetFileName(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _statusLabel.SetText(string.Format(CultureInfo.CurrentCulture, AppStrings.RecordingExportFailed, ex.Message));
        }
        catch (Exception ex)
        {
            _statusLabel.SetText(AppStrings.ActionFailed);
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ConfirmAndDelete(RideSummary ride)
    {
        bool confirmed = await ConfirmAsync(
            AppStrings.RidesDelete,
            string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.RidesDeleteConfirm,
                RideFormat.Interval(ride.StartedAt, ride.EndedAt, _timeProvider.GetLocalNow()),
                ride.Rows),
            AppStrings.RidesDelete,
            AppStrings.Cancel);

        if (!confirmed || _busy) return;

        _busy = true;
        try
        {
            await _store.DeleteRideAsync(ride.Id);
            await Reload();
        }
        catch (Exception)
        {
            _statusLabel.SetText(AppStrings.ActionFailed);
        }
        finally
        {
            _busy = false;
        }
    }

    private Task<bool> ConfirmAsync(string title, string message, string positive, string negative)
    {
        var tcs = new TaskCompletionSource<bool>();
        new AlertDialog.Builder(this)!
            .SetTitle(title)!
            .SetMessage(message)!
            .SetCancelable(false)!
            .SetPositiveButton(positive, (_, _) => tcs.TrySetResult(true))!
            .SetNegativeButton(negative, (_, _) => tcs.TrySetResult(false))!
            .Show();
        return tcs.Task;
    }

    // ---- Разметка ---------------------------------------------------------------------------

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        root.SetBackgroundColor(this.PageBackground());
        int padH = this.Dp(16), padV = this.Dp(12);
        root.SetPadding(padH, padV, padH, padV);

        _statusLabel = new TextView(this) { Text = "" };
        _statusLabel.SetTextSize(ComplexUnitType.Sp, 12);
        _statusLabel.Alpha = 0.7f;
        root.AddView(_statusLabel);

        var list = new RecyclerView(this) { LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1f) { TopMargin = this.Dp(8) } };
        list.SetLayoutManager(new LinearLayoutManager(this));
        _adapter = new RideAdapter(this, _rides, OnRideTapped, OnRideLongPressed);
        list.SetAdapter(_adapter);
        root.AddView(list);

        return root;
    }

    /// <summary>Одна поездка в списке. Пересобирается целиком при перечитывании — итоги фиксируются раз при закрытии поездки, менять на месте нечего.</summary>
    private sealed record RideRow(RideSummary Ride, string When, string Wheel, string Details);

    /// <summary>Без DiffUtil — список короткий, перечитывается целиком, и разница кадр в кадр никого не интересует.</summary>
    private sealed class RideAdapter(Context context, List<RideRow> rides, Action<RideRow> onTap, Action<RideRow> onLongPress)
        : RecyclerView.Adapter
    {
        public override int ItemCount => rides.Count;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var layout = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
            int padV = context.Dp(12);
            layout.SetPadding(0, padV, 0, padV);

            var header = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Horizontal };

            var when = new TextView(context);
            when.SetTextSize(ComplexUnitType.Sp, 16);
            when.SetTextColor(UiKit.PlainText(context));
            header.AddView(when, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            var wheel = new TextView(context);
            wheel.SetTextSize(ComplexUnitType.Sp, 13);
            wheel.Alpha = 0.7f;
            header.AddView(wheel, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = context.Dp(6) });

            layout.AddView(header);

            var details = new TextView(context);
            details.SetTextSize(ComplexUnitType.Sp, 13);
            details.Alpha = 0.8f;
            layout.AddView(details, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = context.Dp(2) });

            return new Holder(layout, when, wheel, details);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            var row = rides[position];
            var h = (Holder)holder;
            h.When.SetText(row.When);
            // Разделитель « · », как в остальных подписях (полоса состояния, детали поездки) — без
            // него время и MAC слипаются в одну строку при увеличенном fontScale (план 11).
            h.Wheel.SetText($"· {row.Wheel}");
            h.Details.SetText(row.Details);

            h.ItemView.Click -= h.ClickHandler;
            h.ClickHandler = (_, _) => onTap(row);
            h.ItemView.Click += h.ClickHandler;

            h.ItemView.LongClick -= h.LongClickHandler;
            h.LongClickHandler = (_, e) => { onLongPress(row); e.Handled = true; };
            h.ItemView.LongClick += h.LongClickHandler;
        }

        private sealed class Holder(View itemView, TextView when, TextView wheel, TextView details)
            : RecyclerView.ViewHolder(itemView)
        {
            public TextView When => when;
            public TextView Wheel => wheel;
            public TextView Details => details;
            public EventHandler? ClickHandler;
            public EventHandler<View.LongClickEventArgs>? LongClickHandler;
        }
    }
}
