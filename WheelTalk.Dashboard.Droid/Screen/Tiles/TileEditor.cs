using System.Globalization;
using Android.App;
using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Core.Metrics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Меню плитки: чем она будет, какого размера и — у графика — за какое время. Одно и то же меню
/// заводит новую плитку и правит существующую; разница лишь в том, что у существующей есть «убрать».
/// <para>
/// <b>Размер задаётся здесь, а не жестом на экране</b> (решение владельца 04.08.2026): перенос
/// двигает плитку и не трогает её размера.
/// </para>
/// <para>
/// <b>График не предлагается там, где его не из чего строить</b> (план 23 §3.2): величина без
/// колонки в таблице телеметрии из списка пропадает — отказ на входе, а не пустая плитка.
/// </para>
/// </summary>
internal static class TileEditor
{
    /// <param name="tile">Правим эту плитку либо <c>null</c> — заводим новую.</param>
    /// <param name="save">Что вышло из меню: новая плитка либо изменённая старая.</param>
    /// <param name="remove">Убрать плитку. <c>null</c> у новой — убирать ещё нечего.</param>
    public static void Show(Context context, Func<string, string> translate, MetricTile? tile,
        Action<MetricTile> save, Action? remove)
    {
        var sizes = TilesLayout.Sizes;
        var windows = TilesLayout.ChartWindows;
        var all = MetricCatalogue.All;
        var charted = all.Where(metric => metric.Column is not null).ToList();

        bool chart = tile?.Kind == TileKind.Chart;
        bool empty = tile is null || tile.Kind == TileKind.Empty;

        var kindPick = Pick(context, [translate("TilesKindValue"), translate("TilesKindChart")], chart ? 1 : 0);
        var metricPick = Pick(context, Choices(translate, chart ? charted : all), 0);
        var sizePick = Pick(context, [.. sizes.Select(size => size.Describe())],
            tile is null ? 0 : Math.Max(0, sizes.ToList().IndexOf(tile.Size)));
        var windowPick = Pick(context, [.. windows.Select(window => Describe(window, translate))],
            tile?.Chart is { } options ? Math.Max(0, windows.ToList().IndexOf(options.Window)) : 0);

        var overlay = new CheckBox(context)
        {
            Text = translate("TilesTileOverlay"),
            Checked = tile?.Chart?.ShowValue == true,
        };

        var zoom = new CheckBox(context)
        {
            Text = translate("TilesTileZoom"),
            Checked = tile?.Chart?.Zoom == true,
        };

        // Подпись — свойство всякой плитки, не только графика: на мелкой она забирает место у числа,
        // и выключают её чаще, чем кажется.
        var showLabel = new CheckBox(context)
        {
            Text = translate("TilesTileLabel"),
            Checked = tile?.ShowLabel != false,
        };

        Select(metricPick, empty ? 0 : IndexOfMetric(chart ? charted : all, tile!.MetricId) + 1);

        // Окно и наложение числа — свойства одного только графика: у плитки значения их нет, и
        // показывать их выключенными значило бы спрашивать о том, чего не бывает.
        var chartOptions = new LinearLayout(context)
        {
            Orientation = Android.Widget.Orientation.Vertical,
            Visibility = chart ? ViewStates.Visible : ViewStates.Gone,
        };

        chartOptions.AddView(Caption(context, translate("TilesTileWindow")));
        chartOptions.AddView(windowPick);
        chartOptions.AddView(overlay);
        chartOptions.AddView(zoom);

        // Смена вида пересобирает список величин: у графика в нём остаются только те, у кого есть
        // история. Выбранная величина переносится, если она есть и в новом списке.
        kindPick.ItemSelected += (_, _) =>
        {
            bool wantChart = kindPick.SelectedItemPosition == 1;
            var metrics = wantChart ? charted : all;
            string chosen = ChosenMetric(metricPick, wantChart ? all : charted);

            Fill(metricPick, Choices(translate, metrics));
            Select(metricPick, chosen.Length == 0 ? 0 : IndexOfMetric(metrics, chosen) + 1);

            chartOptions.Visibility = wantChart ? ViewStates.Visible : ViewStates.Gone;
        };

        int pad = context.Dp(TilesLayout.PaddingDp * 2);
        var content = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        content.SetPadding(pad, pad, pad, pad);
        content.AddView(Caption(context, translate("TilesTileKind")));
        content.AddView(kindPick);
        content.AddView(Caption(context, translate("TilesTileMetric")));
        content.AddView(metricPick);
        content.AddView(Caption(context, translate("TilesTileSize")));
        content.AddView(sizePick);
        content.AddView(showLabel);
        content.AddView(chartOptions);

        var dialog = new AlertDialog.Builder(context)
            .SetView(content)!
            .SetPositiveButton(Android.Resource.String.Ok, (_, _) => save(Result(
                kindPick.SelectedItemPosition == 1 ? charted : all,
                kindPick.SelectedItemPosition == 1,
                metricPick.SelectedItemPosition,
                sizes[sizePick.SelectedItemPosition],
                showLabel.Checked,
                new TileChart(windows[windowPick.SelectedItemPosition], overlay.Checked, zoom.Checked))))!
            .SetNegativeButton(Android.Resource.String.Cancel, (_, _) => { })!;

        if (remove is not null) dialog.SetNeutralButton(translate("TilesTileRemove"), (_, _) => remove());

        dialog.Show();
    }

    private static MetricTile Result(IReadOnlyList<MetricDescriptor> metrics, bool chart, int chosen,
        TileSize size, bool showLabel, TileChart options)
    {
        if (chosen == 0) return MetricTile.Empty(size);

        string id = metrics[chosen - 1].Id;

        return chart
            ? new MetricTile(id, TileKind.Chart, size, showLabel, options)
            : new MetricTile(id, TileKind.Value, size, showLabel);
    }

    /// <summary>
    /// Пустое место — первым пунктом того же списка, а не отдельной кнопкой: выбирают-то одно и то
    /// же — чем будет плитка.
    /// </summary>
    private static string[] Choices(Func<string, string> translate, IReadOnlyList<MetricDescriptor> metrics) =>
        [translate("TilesTileEmpty"), .. metrics.Select(metric => translate(metric.LabelKey))];

    /// <summary>Что выбрано сейчас, именем величины. Пусто — выбрано пустое место.</summary>
    private static string ChosenMetric(Spinner pick, IReadOnlyList<MetricDescriptor> metrics)
    {
        int chosen = pick.SelectedItemPosition;

        return chosen <= 0 || chosen > metrics.Count ? "" : metrics[chosen - 1].Id;
    }

    /// <summary>Величины может не быть в списке — тогда меню открывается на пустом месте, а не падает.</summary>
    private static int IndexOfMetric(IReadOnlyList<MetricDescriptor> metrics, string id)
    {
        for (int index = 0; index < metrics.Count; index++)
        {
            if (metrics[index].Id == id) return index;
        }

        return -1;
    }

    /// <summary>Окно графика: «15 мин», «3 ч». Число и краткая единица — двух слов на всё меню хватает.</summary>
    private static string Describe(TimeSpan window, Func<string, string> translate) =>
        window < TimeSpan.FromHours(1)
            ? $"{window.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture)} {translate("UnitMinutesShort")}"
            : $"{window.TotalHours.ToString("F0", CultureInfo.InvariantCulture)} {translate("UnitHoursShort")}";

    private static Spinner Pick(Context context, string[] items, int selected)
    {
        var pick = new Spinner(context);

        Fill(pick, items);
        Select(pick, selected);

        return pick;
    }

    private static void Fill(Spinner pick, string[] items)
    {
        var adapter = new ArrayAdapter<string>(pick.Context!, Android.Resource.Layout.SimpleSpinnerItem, items);

        // Раскрытый список рисуется своей разметкой — без неё пункты наезжают друг на друга. Обе
        // разметки платформенные: своих здесь заводить незачем.
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        pick.Adapter = adapter;
    }

    private static void Select(Spinner pick, int index)
    {
        if (pick.Adapter is { } adapter && index >= 0 && index < adapter.Count) pick.SetSelection(index);
    }

    private static TextView Caption(Context context, string text)
    {
        var caption = new TextView(context) { Text = text };
        caption.SetTextSize(ComplexUnitType.Sp, TilesLayout.LabelSp);

        return caption;
    }
}
