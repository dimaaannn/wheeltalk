using Android.App;
using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Core.Metrics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Меню плитки: чем она будет и какого размера. Одно и то же меню заводит новую плитку и правит
/// существующую — разница лишь в том, что у существующей есть «убрать».
/// <para>
/// <b>Размер задаётся здесь, а не жестом на экране</b> (решение владельца 04.08.2026): перенос
/// двигает плитку и не трогает её размера. Позже сюда же придёт диапазон графика — потому меню, а
/// не выбор из одного списка.
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
        var metrics = MetricCatalogue.All;
        var sizes = TilesLayout.Sizes;

        // Пустое место — первым пунктом того же списка, а не отдельной кнопкой: выбирают-то одно и
        // то же — чем будет плитка.
        string[] choices = [translate("TilesTileEmpty"), .. metrics.Select(metric => translate(metric.LabelKey))];

        var metricPick = Pick(context, choices,
            tile is null || tile.Kind == TileKind.Empty ? 0 : IndexOfMetric(metrics, tile.MetricId) + 1);
        var sizePick = Pick(context, [.. sizes.Select(size => size.Describe())],
            tile is null ? 0 : Math.Max(0, sizes.ToList().IndexOf(tile.Size)));

        int pad = context.Dp(TilesLayout.PaddingDp * 2);
        var content = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        content.SetPadding(pad, pad, pad, pad);
        content.AddView(Caption(context, translate("TilesTileMetric")));
        content.AddView(metricPick);
        content.AddView(Caption(context, translate("TilesTileSize")));
        content.AddView(sizePick);

        var dialog = new AlertDialog.Builder(context)
            .SetView(content)!
            .SetPositiveButton(Android.Resource.String.Ok, (_, _) => save(
                metricPick.SelectedItemPosition == 0
                    ? MetricTile.Empty(sizes[sizePick.SelectedItemPosition])
                    : new MetricTile(
                        metrics[metricPick.SelectedItemPosition - 1].Id,
                        TileKind.Value,
                        sizes[sizePick.SelectedItemPosition])))!
            .SetNegativeButton(Android.Resource.String.Cancel, (_, _) => { })!;

        if (remove is not null) dialog.SetNeutralButton(translate("TilesTileRemove"), (_, _) => remove());

        dialog.Show();
    }

    /// <summary>Величина могла выпасть из каталога — тогда меню открывается на первой, а не падает.</summary>
    private static int IndexOfMetric(IReadOnlyList<MetricDescriptor> metrics, string id)
    {
        for (int index = 0; index < metrics.Count; index++)
        {
            if (metrics[index].Id == id) return index;
        }

        return 0;
    }

    private static Spinner Pick(Context context, string[] items, int selected)
    {
        var pick = new Spinner(context)
        {
            Adapter = new ArrayAdapter<string>(context, Android.Resource.Layout.SimpleSpinnerItem, items)
            {
                // Раскрытый список рисуется своей разметкой — без неё пункты наезжают друг на друга.
                // Обе разметки платформенные: своих здесь заводить незачем.
            },
        };

        ((ArrayAdapter<string>)pick.Adapter).SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        pick.SetSelection(selected);

        return pick;
    }

    private static TextView Caption(Context context, string text)
    {
        var caption = new TextView(context) { Text = text };
        caption.SetTextSize(ComplexUnitType.Sp, TilesLayout.LabelSp);

        return caption;
    }
}
