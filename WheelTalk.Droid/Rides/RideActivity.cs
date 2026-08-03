using System.Globalization;
using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WheelTalk.Droid.Logging;
using WheelTalk.Droid.Resources.Strings;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Droid.Ui;
using WheelTalk.Storage;

using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.App;
using WheelTalk.Droid.Playback;

namespace WheelTalk.Droid.Rides;

/// <summary>
/// Одна поездка целиком и кнопка, которая превращает её в CSV, который читают сторонние сервисы.
/// Портировано с эталона <c>WheelTalk.App/Pages/RidePage.xaml(.cs)</c> (опись §1.3, §5). Без
/// графиков и без карты — по тем же причинам, что у эталона (комментарий класса там же): графики —
/// отдельная задача (план 7), а GPS у приложения нет вовсе.
/// <para>
/// В отличие от эталона (страница получала уже загруженный <c>RideSummary</c> вызовом
/// <c>Show(ride)</c> перед пушем), сюда Android передаёт только идентификатор через
/// <see cref="Android.Content.Intent"/> — Activity создаёт сам, конструктор с параметром ему не
/// передать (план 12 §2.2) — и поездка перечитывается здесь же, в фоновом потоке.
/// </para>
/// </summary>
[Activity]
public sealed class RideActivity : Activity
{
    public const string ExtraRideId = "ride_id";

    private RideExporter _exporter = null!;

    private RideSummary? _ride;
    private bool _busy;

    private TextView _whenLabel = null!;
    private TextView _wheelLabel = null!;
    private LinearLayout _totalsLayout = null!;
    private Button _exportButton = null!;
    private TextView _resultLabel = null!;
    private WheelOptions _wheel = null!;
    private WheelIdentity _identity = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _wheel = MainApplication.Services.GetRequiredService<IOptions<WheelOptions>>().Value;
        _identity = MainApplication.Services.GetRequiredService<WheelIdentity>();

        // Заголовок ставится кодом, а не атрибутом [Activity]: в атрибут можно положить только
        // константу, а строки живут в ресурсах (план 6, фаза 1 — вшитых строк не осталось).
        Title = string.Format(CultureInfo.CurrentCulture, AppStrings.ScreenTitleFormat, AppStrings.RideTitle);

        _exporter = MainApplication.Services.GetRequiredService<RideExporter>();

        var root = BuildLayout();
        SetContentView(root);
        EdgeToEdge.Apply(this, root);

        long rideId = Intent?.GetLongExtra(ExtraRideId, 0) ?? 0;
        _ = LoadAsync(rideId);
    }

    private async Task LoadAsync(long rideId)
    {
        try
        {
            var ride = await Task.Run(() => _exporter.Rides().FirstOrDefault(r => r.Id == rideId));
            if (ride is null)
            {
                // Поездку успели удалить (или id пустой) — здесь нечего показывать.
                Finish();
                return;
            }

            Show(ride);
        }
        catch (Exception)
        {
            _resultLabel.SetText(AppStrings.ActionFailed);
        }
    }

    /// <summary>Заполняет экран уже загруженной поездкой — ровно то, что в эталоне делал публичный <c>Show(ride)</c>.</summary>
    private void Show(RideSummary ride)
    {
        _ride = ride;

        _whenLabel.SetText(RideFormat.When(ride.StartedAt, DateTimeOffset.Now));
        string wheelName = RideFormat.WheelName(ride, _wheel, _identity);
        _wheelLabel.SetText(ride.Version.Length > 0 ? $"{wheelName} · {ride.Version}" : wheelName);
        _resultLabel.SetText(AppStrings.RideExportHint);

        _totalsLayout.RemoveAllViews();
        foreach (var (label, value) in Figures(ride)) _totalsLayout.AddView(Line(label, value));
    }

    private static IEnumerable<(string Label, string Value)> Figures(RideSummary ride)
    {
        if (ride.Totals is not { } totals)
        {
            // Итогов нет — либо поездка ещё пишется (их считают в момент закрытия, и до тех пор
            // любое число здесь было бы про поездку, которая не кончилась), либо она закрыта, а
            // кадры унёс срок хранения (план 23 §5.5). В обоих случаях показывать нечего, кроме
            // числа строк.
            yield return (AppStrings.RideRows, ride.Rows.ToString(CultureInfo.CurrentCulture));
            yield break;
        }

        yield return (AppStrings.RideDistance, RideFormat.Distance(totals.DistanceKm));
        yield return (AppStrings.RideDuration, RideFormat.Duration(totals.Duration));
        yield return (AppStrings.RideMoving, RideFormat.Duration(totals.Moving));
        yield return (AppStrings.RideAvgSpeed, RideFormat.Number(totals.AverageSpeedKmh, 1, AppStrings.UnitKmh));
        yield return (AppStrings.RideMaxSpeed, RideFormat.Number(totals.MaxSpeedKmh, 1, AppStrings.UnitKmh));
        yield return (AppStrings.RideMaxPwm, RideFormat.Number(totals.MaxPwm, 1, AppStrings.UnitPercent));
        yield return (AppStrings.RideMaxPower, RideFormat.Number(totals.MaxPowerW, 0, AppStrings.UnitWatts));
        yield return (AppStrings.RideMaxCurrent, RideFormat.Number(totals.MaxCurrentA, 1, AppStrings.UnitAmperes));
        yield return (AppStrings.RideConsumption, RideFormat.Number(totals.ConsumptionWh, 0, AppStrings.UnitWattHours));

        if (totals.ConsumptionWhPerKm is { } perKm)
        {
            yield return (AppStrings.RideConsumptionPerKm, RideFormat.Number(perKm, 1, AppStrings.UnitWattHoursPerKm));
        }

        yield return (AppStrings.RideRows, ride.Rows.ToString(CultureInfo.CurrentCulture));
    }

    private View Line(string label, string value)
    {
        var row = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        int padV = this.Dp(7);
        row.SetPadding(0, padV, 0, padV);

        var labelView = new TextView(this) { Text = label };
        labelView.SetTextSize(ComplexUnitType.Sp, 15);
        labelView.Alpha = 0.75f;
        row.AddView(labelView, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var valueView = new TextView(this) { Text = value };
        valueView.SetTextSize(ComplexUnitType.Sp, 17);
        valueView.SetTextColor(UiKit.PlainText(this));
        var valueParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = this.Dp(12) };
        row.AddView(valueView, valueParams);

        return row;
    }

    private async void OnExportClicked()
    {
        if (_busy || _ride is not { } ride) return;

        _busy = true;
        _exportButton.Enabled = false;
        try
        {
            // Двадцать тысяч строк через форматирование — не то, что делают на потоке разметки.
            string path = await Task.Run(() => RideCsvExport.Write(_exporter, ride));
            _resultLabel.SetText(string.Format(
                CultureInfo.CurrentCulture, AppStrings.RecordingExportDone, System.IO.Path.GetFileName(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _resultLabel.SetText(string.Format(CultureInfo.CurrentCulture, AppStrings.RecordingExportFailed, ex.Message));
        }
        catch (Exception)
        {
            _resultLabel.SetText(AppStrings.ActionFailed);
        }
        finally
        {
            _busy = false;
            _exportButton.Enabled = true;
        }
    }

    // ---- Разметка ---------------------------------------------------------------------------

    private View BuildLayout()
    {
        var scroll = new ScrollView(this);
        scroll.SetBackgroundColor(this.PageBackground());

        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        int pad = this.Dp(16);
        root.SetPadding(pad, pad, pad, pad);

        var header = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };

        _whenLabel = new TextView(this) { Text = "" };
        _whenLabel.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        _whenLabel.SetTextSize(ComplexUnitType.Sp, 19);
        _whenLabel.SetTextColor(UiKit.PlainText(this));
        header.AddView(_whenLabel);

        _wheelLabel = new TextView(this) { Text = "" };
        _wheelLabel.SetTextSize(ComplexUnitType.Sp, 13);
        _wheelLabel.Alpha = 0.7f;
        header.AddView(_wheelLabel);

        root.AddView(header);

        _totalsLayout = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        root.AddView(_totalsLayout, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(14) });

        var playButton = UiKit.CreateButton(this, AppStrings.RidePlay);
        playButton.Click += (_, _) => PlaybackActivity.Open(this, Intent?.GetLongExtra(ExtraRideId, 0) ?? 0);
        root.AddView(playButton, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(14) });

        _exportButton = UiKit.CreateButton(this, AppStrings.RidesExport);
        _exportButton.Click += (_, _) => OnExportClicked();
        root.AddView(_exportButton, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(8) });

        _resultLabel = new TextView(this) { Text = AppStrings.RideExportHint };
        _resultLabel.SetTextSize(ComplexUnitType.Sp, 12);
        _resultLabel.Alpha = 0.7f;
        root.AddView(_resultLabel, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(6) });

        scroll.AddView(root);
        return scroll;
    }
}
