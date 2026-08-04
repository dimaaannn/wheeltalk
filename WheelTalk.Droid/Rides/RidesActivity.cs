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
/// действий — отдельная иконка «⋮». Здесь — тап открывает, долгий тап показывает меню, без второй
/// зоны нажатия на тесной строке списка. Меню же — дверь и в отметку нескольких строк разом
/// (план 23 §3а, шаг после менюки, владелец 04.08.2026: удалять полсотни поездок по одной было
/// недопустимо): выбрав «Выбрать несколько», тап и долгий тап по любой строке переключают отметку,
/// пока список отмеченных не опустеет.
/// </para>
/// </summary>
[Activity]
public sealed class RidesActivity : Activity
{
    private RideExporter _exporter = null!;
    private RideStore _store = null!;
    private TimeProvider _timeProvider = null!;
    private StorageOptions _storage = null!;

    private readonly List<RideRow> _rides = [];
    private RideAdapter _adapter = null!;
    private TextView _statusLabel = null!;

    /// <summary>
    /// Id поездок, отмеченных для удаления списком — непусто значит «идёт выбор» (ToggleSelection
    /// отказывает открытой поездке, так что её id сюда не попадает).
    /// </summary>
    private readonly HashSet<long> _selected = [];
    private LinearLayout _selectionBar = null!;
    private TextView _selectionCount = null!;

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
        _storage = MainApplication.Services.GetRequiredService<IOptions<StorageOptions>>().Value;

        var root = BuildLayout();
        SetContentView(root);
        EdgeToEdge.Apply(this, root);
    }

    protected override void OnStart()
    {
        base.OnStart();

        // Выбор не переживает уход с экрана: список за это время мог перечитаться (удаление, конец
        // поездки), и старые id — уже не про то, что видит человек.
        CancelSelection();

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
        // Три подписи, а не две: пустые итоги значат разное у открытой поездки и у закрытой
        // (план 23 §5.5, RideSummary.Totals). Идёт запись — говорит ended_at IS NULL, и только он;
        // у закрытой пустые итоги значат «подробностей больше нет», кадры унёс срок хранения.
        string details = ride.Totals is { } totals
            ? RideFormat.Summary(totals) + DetailsSuffix(ride, now)
            : ride.IsOpen
                ? string.Format(CultureInfo.CurrentCulture, AppStrings.RidesRecording, ride.Rows)
                : AppStrings.RidesNoDetails;

        // Начало и конец, а не одно начало: по списку выбирают поездку, а «та, что после обеда до
        // пяти» — это два времени. Дистанция стоит первой в строке итогов под ними.
        return new RideRow(
            ride,
            RideFormat.Interval(ride.StartedAt, ride.EndedAt, now),
            RideFormat.WheelName(ride, _wheel, _identity),
            details);
    }

    /// <summary>
    /// «· Подробности до 2 сентября», приписанное к итогам, — или «· Подробностей больше нет», если
    /// срок уже прошёл. Девять чисел в <see cref="RideFormat.Summary"/> живут вечно, кадры за ними —
    /// нет (план 23 §5.8): без этой строки узнают об этом, только открыв поездку и не найдя графиков.
    /// </summary>
    private string DetailsSuffix(RideSummary ride, DateTimeOffset now) =>
        RideFormat.DetailsExpiry(ride.EndedAt, _storage.TelemetryRetention, now) is { } note ? $" · {note}" : "";

    private void OnRideTapped(RideRow row)
    {
        // Идёт выбор — тап переключает отметку, а не открывает поездку: два смысла у одного жеста
        // жили бы одновременно, и один из них удивлял бы.
        if (_selected.Count > 0)
        {
            ToggleSelection(row);
            return;
        }

        var intent = new Intent(this, typeof(RideActivity));
        intent.PutExtra(RideActivity.ExtraRideId, row.Ride.Id);
        StartActivity(intent);
    }

    /// <summary>
    /// The row's own commands. Delete is refused for the ride being written — it is not finished
    /// being written. Идёт выбор — долгий тап не открывает попап, а как и короткий, отмечает строку:
    /// иначе пришлось бы тянуться к другому жесту посреди отметки полусотни поездок.
    /// </summary>
    private void OnRideLongPressed(RideRow row)
    {
        if (_selected.Count > 0)
        {
            ToggleSelection(row);
            return;
        }

        // «Выбрать несколько» — дверь в отметку не одной строкой (владелец 04.08.2026: полсотни
        // поездок по одной недопустимо). Одиночные экспорт и удаление остаются как были — на них
        // это меню и рассчитано у большинства поездок.
        string[] actions = row.Ride.IsOpen
            ? [AppStrings.RidesExport]
            : [AppStrings.RidesExport, AppStrings.RidesDelete, AppStrings.RidesSelect];

        new AlertDialog.Builder(this)!
            .SetTitle(row.When)!
            .SetItems(actions, (_, e) =>
            {
                if (actions[e.Which] == AppStrings.RidesExport) _ = Export(row.Ride);
                else if (actions[e.Which] == AppStrings.RidesDelete) _ = ConfirmAndDelete(row.Ride);
                else if (actions[e.Which] == AppStrings.RidesSelect) ToggleSelection(row);
            })!
            .SetNegativeButton(AppStrings.Cancel, (_, _) => { })!
            .Show();
    }

    /// <summary>Отмечает или снимает строку. Открытую поездку не берём — на неё и одиночное удаление отказывает, тем же правилом.</summary>
    private void ToggleSelection(RideRow row)
    {
        if (row.Ride.IsOpen) return;

        if (!_selected.Remove(row.Ride.Id)) _selected.Add(row.Ride.Id);
        UpdateSelectionBar();
        _adapter.NotifyDataSetChanged();
    }

    private void UpdateSelectionBar()
    {
        _selectionBar.SetShown(_selected.Count > 0);
        _selectionCount.SetText(string.Format(CultureInfo.CurrentCulture, AppStrings.RidesSelectedCount, _selected.Count));
    }

    private void CancelSelection()
    {
        if (_selected.Count == 0) return;

        _selected.Clear();
        UpdateSelectionBar();
        _adapter.NotifyDataSetChanged();
    }

    /// <summary>Одно действие на все отмеченные — то, чего не было (владелец 04.08.2026). Тот же запрос на строку, просто по очереди на каждую.</summary>
    private async Task DeleteSelected()
    {
        if (_busy || _selected.Count == 0) return;

        bool confirmed = await ConfirmAsync(
            AppStrings.RidesDelete,
            string.Format(CultureInfo.CurrentCulture, AppStrings.RidesDeleteSelectedConfirm, _selected.Count),
            AppStrings.RidesDelete,
            AppStrings.Cancel);

        if (!confirmed || _busy) return;

        _busy = true;
        try
        {
            await Task.WhenAll(_selected.Select(id => _store.DeleteRideAsync(id)));
            _selected.Clear();
            UpdateSelectionBar();
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
        // Про строки здесь больше не говорим: удаление поездки уносит её саму и её итоги, а поток
        // остаётся и уходит своим сроком (план 23 §5.1 п. 5). Число строк в вопросе обещало бы, что
        // с поездкой пропадут и данные, — а это ровно то, от чего ушли.
        bool confirmed = await ConfirmAsync(
            AppStrings.RidesDelete,
            string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.RidesDeleteConfirm,
                RideFormat.Interval(ride.StartedAt, ride.EndedAt, _timeProvider.GetLocalNow())),
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

        root.AddView(BuildSelectionBar(), new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(8) });

        var list = new RecyclerView(this) { LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1f) { TopMargin = this.Dp(8) } };
        list.SetLayoutManager(new LinearLayoutManager(this));
        _adapter = new RideAdapter(this, _rides, OnRideTapped, OnRideLongPressed, id => _selected.Contains(id), () => _selected.Count > 0);
        list.SetAdapter(_adapter);
        root.AddView(list);

        return root;
    }

    /// <summary>
    /// «Выбрано: N» и одно действие на все отмеченные, вместо полусотни отдельных попапов. Скрыта,
    /// пока ничего не отмечено — тот же приём, что у вуали устаревших данных: показывается и
    /// прячется правкой видимости, не пересборкой.
    /// </summary>
    private View BuildSelectionBar()
    {
        _selectionBar = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        _selectionBar.SetGravity(GravityFlags.CenterVertical);
        _selectionBar.Visibility = ViewStates.Gone;

        _selectionCount = new TextView(this) { Text = "" };
        _selectionCount.SetTextColor(UiKit.PlainText(this));
        _selectionBar.AddView(_selectionCount, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var deleteButton = UiKit.CreateButton(this, AppStrings.RidesDelete);
        deleteButton.Click += (_, _) => _ = DeleteSelected();
        _selectionBar.AddView(deleteButton, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { RightMargin = this.Dp(8) });

        var cancelButton = UiKit.CreateButton(this, AppStrings.Cancel);
        cancelButton.Click += (_, _) => CancelSelection();
        _selectionBar.AddView(cancelButton);

        return _selectionBar;
    }

    /// <summary>Одна поездка в списке. Пересобирается целиком при перечитывании — итоги фиксируются раз при закрытии поездки, менять на месте нечего.</summary>
    private sealed record RideRow(RideSummary Ride, string When, string Wheel, string Details);

    /// <summary>
    /// Без DiffUtil — список короткий, перечитывается целиком, и разница кадр в кадр никого не
    /// интересует. <paramref name="isSelected"/>/<paramref name="isSelectionMode"/> читают выбор
    /// хозяина экрана, а не хранят свой — второй копии множества id заводить незачем.
    /// </summary>
    private sealed class RideAdapter(
        Context context,
        List<RideRow> rides,
        Action<RideRow> onTap,
        Action<RideRow> onLongPress,
        Func<long, bool> isSelected,
        Func<bool> isSelectionMode)
        : RecyclerView.Adapter
    {
        public override int ItemCount => rides.Count;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var row = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Horizontal };
            row.SetGravity(GravityFlags.CenterVertical);
            int padV = context.Dp(12);
            row.SetPadding(0, padV, 0, padV);

            // Не кликабелен сам — тап по чекбоксу должен делать то же, что тап по всей строке
            // (переключать отметку), а не обзаводиться вторым, чуть другим поведением.
            var check = new CheckBox(context) { Clickable = false, Focusable = false };
            row.AddView(check, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { RightMargin = context.Dp(8) });

            var layout = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };

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

            row.AddView(layout, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            return new Holder(row, check, when, wheel, details);
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

            // Открытую поездку не отмечают — ни чекбокс ей не рисуем, ни на неё не переключаемся
            // (тем же правилом, что и у одиночного удаления).
            bool selectable = !row.Ride.IsOpen;
            h.Check.SetShown(isSelectionMode() && selectable);
            h.Check.Checked = selectable && isSelected(row.Ride.Id);

            h.ItemView.Click -= h.ClickHandler;
            h.ClickHandler = (_, _) => onTap(row);
            h.ItemView.Click += h.ClickHandler;

            h.ItemView.LongClick -= h.LongClickHandler;
            h.LongClickHandler = (_, e) => { onLongPress(row); e.Handled = true; };
            h.ItemView.LongClick += h.LongClickHandler;
        }

        private sealed class Holder(View itemView, CheckBox check, TextView when, TextView wheel, TextView details)
            : RecyclerView.ViewHolder(itemView)
        {
            public CheckBox Check => check;
            public TextView When => when;
            public TextView Wheel => wheel;
            public TextView Details => details;
            public EventHandler? ClickHandler;
            public EventHandler<View.LongClickEventArgs>? LongClickHandler;
        }
    }
}
