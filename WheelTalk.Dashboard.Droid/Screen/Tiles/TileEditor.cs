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
    /// <returns>
    /// Показанное меню — хозяину экрана: диалог висит на окне активности, и брошенный он утекает
    /// вместе с ней (тот же корень, что у полноэкранного просмотра, — <see cref="ChartViewer.Show"/>;
    /// в стеке <c>WindowLeaked</c> они и не различаются, оба стоят как <c>Dialog.show</c> из тапа по
    /// плитке).
    /// </returns>
    public static Dialog Show(Context context, Func<string, string> translate, MetricTile? tile,
        Action<MetricTile> save, Action? remove)
    {
        var sizes = TilesLayout.Sizes;
        var windows = TilesLayout.ChartWindows;
        var all = MetricCatalogue.All;
        var charted = all.Where(metric => metric.Column is not null).ToList();

        bool chart = tile?.Kind == TileKind.Chart;
        bool empty = tile is null || tile.Kind == TileKind.Empty;

        // Порядок видов в списке — он же порядок чисел в Result: «число», «график», «крайнее»,
        // «дистанция», «пусто».
        int kindOf(MetricTile? known) => known?.Kind switch
        {
            TileKind.Chart => 1,
            TileKind.Extremum => 2,
            TileKind.Trip => 3,
            TileKind.Empty => 4,
            null => 4,
            _ => 0,
        };

        // Пустое место — третий вид плитки, а не первый пункт в списке величин: дырку ставят не
        // «величиной по имени Пусто», а решением «здесь ничего». Заодно у пустой гаснет всё, чего у
        // неё не бывает, — величина, подпись, пороги.
        var kindPick = Pick(context,
            [translate("TilesKindValue"), translate("TilesKindChart"), translate("TilesKindExtremum"),
                translate("TilesKindTrip"), translate("TilesTileEmpty")],
            kindOf(tile));
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

        var fill = new CheckBox(context)
        {
            Text = translate("TilesTileFill"),
            Checked = tile?.Chart?.Fill != false,
        };

        var axis = new CheckBox(context)
        {
            Text = translate("TilesTileAxis"),
            Checked = tile?.Chart?.Axis != false,
        };

        var smoothingPick = Pick(context,
            [translate("TilesSmoothMinMax"), translate("TilesSmoothPeaks"), translate("TilesSmoothDips")],
            (int)(tile?.Chart?.Smoothing ?? ChartSmoothing.MinMax));

        // Пороги — одни на плитку: по ним же красится подложка при текущем значении и пики её
        // графика (решение владельца 05.08.2026). Пусто в поле значит «брать из настроек тревог».
        var warn = Number(context, translate("TilesTileWarn"), tile?.Limits?.Warn);
        var danger = Number(context, translate("TilesTileDanger"), tile?.Limits?.Danger);
        var falling = new CheckBox(context)
        {
            Text = translate("TilesTileFalling"),
            Checked = tile?.Limits is { Rising: false },
        };

        // У крайнего значения свойство одно: какой край помнить. Сброс в меню не живёт — им служит
        // короткий тап по самой плитке.
        var lowest = new CheckBox(context)
        {
            Text = translate("TilesTileLowest"),
            Checked = tile?.Extremum?.Lowest == true,
            Visibility = tile?.Kind == TileKind.Extremum ? ViewStates.Visible : ViewStates.Gone,
        };

        // Подпись — свойство всякой плитки, не только графика: на мелкой она забирает место у числа,
        // и выключают её чаще, чем кажется.
        var showLabel = new CheckBox(context)
        {
            Text = translate("TilesTileLabel"),
            Checked = tile?.ShowLabel != false,
        };

        // Полоска жара — своя у каждой плитки, тем же правом, что и пороги: она отвечает «насколько
        // близко к тревоге», и там, где тревоге взяться неоткуда (одометр, пробег), это лишняя
        // строка внимания. Включена по умолчанию — и у новой плитки, и у старой сохранённой
        // раскладки, где поля нет вовсе.
        var heatBar = new CheckBox(context)
        {
            Text = translate("TilesTileHeatBar"),
            Checked = tile?.ShowHeatBar != false,
        };

        // Округление — своё у каждой плитки, тем же правом, что пороги и полоса жара: одну и ту же
        // величину ставят и крупной, и справочной, и подробность у них разная. Первый пункт — «по
        // умолчанию», то есть размерность типа величины; он же стоит у новой плитки.
        var roundingPick = Pick(context, Roundings(translate), RoundingIndex(tile?.Decimals));

        Select(metricPick, empty ? 0 : IndexOfMetric(chart ? charted : all, tile!.MetricId));

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
        chartOptions.AddView(fill);
        chartOptions.AddView(axis);
        chartOptions.AddView(Caption(context, translate("TilesTileSmoothing")));
        chartOptions.AddView(smoothingPick);

        // Величина первой, вид вторым: человек думает «хочу ток», а не «хочу число» — вид это
        // свойство показа, а не предмет разговора.
        var metricLine = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        metricLine.AddView(Caption(context, translate("TilesTileMetric")));
        metricLine.AddView(metricPick);
        metricLine.Visibility = empty ? ViewStates.Gone : ViewStates.Visible;

        // Своя подпись — у всякой плитки, не только у дистанции (решение владельца 10.08.2026).
        // Пусто значит «зови по величине»: подсказкой стоит то самое имя, чтобы видно было, что
        // получится, если поле оставить пустым.
        var caption = new EditText(context)
        {
            Text = tile?.Caption ?? "",
            Hint = translate("TilesTileCaption"),
        };

        caption.SetSingleLine(true);

        var captionLine = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        captionLine.AddView(Caption(context, translate("TilesTileCaption")));
        captionLine.AddView(caption);
        captionLine.Visibility = empty ? ViewStates.Gone : ViewStates.Visible;

        var roundingLine = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        roundingLine.AddView(Caption(context, translate("TilesTileRounding")));
        roundingLine.AddView(roundingPick);
        roundingLine.Visibility = empty ? ViewStates.Gone : ViewStates.Visible;

        var limitsLine = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        limitsLine.AddView(Caption(context, translate("TilesTileLimits")));
        limitsLine.AddView(warn);
        limitsLine.AddView(danger);
        limitsLine.AddView(falling);
        limitsLine.Visibility = empty ? ViewStates.Gone : ViewStates.Visible;

        showLabel.Visibility = empty ? ViewStates.Gone : ViewStates.Visible;
        heatBar.Visibility = empty ? ViewStates.Gone : ViewStates.Visible;

        // Какой список величин лежит в спиннере прямо сейчас. Держим его сами, а не выводим из вида:
        // `Spinner` стреляет `ItemSelected` вхолостую сразу после раскладки, ещё до всякого выбора, и
        // обработчик, считавший вид, принимал старый список за другой — выбор перекладывался через
        // чужой, и «Напряжение» превращалось в «Фазный ток» молча, при одном открытии меню.
        var shownMetrics = chart ? charted : all;

        kindPick.ItemSelected += (_, _) =>
        {
            bool wantEmpty = kindPick.SelectedItemPosition == 4;
            bool wantChart = kindPick.SelectedItemPosition == 1;
            bool wantTrip = kindPick.SelectedItemPosition == 3;
            lowest.Visibility = kindPick.SelectedItemPosition == 2 ? ViewStates.Visible : ViewStates.Gone;
            var metrics = wantChart ? charted : all;

            chartOptions.Visibility = wantChart ? ViewStates.Visible : ViewStates.Gone;

            // У пустого места нет ни величины, ни подписи, ни порогов — спрашивать о них незачем.
            var forFilled = wantEmpty ? ViewStates.Gone : ViewStates.Visible;
            captionLine.Visibility = forFilled;

            // У дистанции величина одна — общий пробег колеса, из которого она и считается;
            // спрашивать «какую величину» там значит предлагать бессмыслицу вроде «дистанция ШИМа».
            metricLine.Visibility = wantTrip ? ViewStates.Gone : forFilled;
            showLabel.Visibility = forFilled;
            heatBar.Visibility = forFilled;
            roundingLine.Visibility = forFilled;
            limitsLine.Visibility = forFilled;

            if (wantEmpty) return;

            // Список тот же — перекладывать нечего: холостой выстрел не должен трогать выбор.
            if (ReferenceEquals(metrics, shownMetrics)) return;

            // У графика в списке остаются только величины с историей, поэтому выбранное переносится
            // по имени, а не по номеру: номера в двух списках разные.
            string chosen = ChosenMetric(metricPick, shownMetrics);
            shownMetrics = metrics;

            Fill(metricPick, Choices(translate, metrics));
            Select(metricPick, IndexOfMetric(metrics, chosen));
        };

        int pad = context.Dp(TilesLayout.PaddingDp * 2);
        var content = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        content.SetPadding(pad, pad, pad, pad);
        content.AddView(metricLine);
        content.AddView(captionLine);
        content.AddView(Caption(context, translate("TilesTileKind")));
        content.AddView(kindPick);
        content.AddView(Caption(context, translate("TilesTileSize")));
        content.AddView(sizePick);
        content.AddView(showLabel);
        content.AddView(heatBar);
        content.AddView(lowest);
        content.AddView(roundingLine);
        content.AddView(chartOptions);
        content.AddView(limitsLine);

        // Меню длинное, а экраны разные: без прокрутки на телефоне поменьше или при крупном
        // системном шрифте пороги уезжают за край вместе с кнопками.
        var scroller = new ScrollView(context);
        scroller.AddView(content);

        var dialog = new AlertDialog.Builder(context)
            .SetView(scroller)!
            .SetPositiveButton(Android.Resource.String.Ok, (_, _) => save(Result(
                kindPick.SelectedItemPosition == 1 ? charted : all,
                kindPick.SelectedItemPosition,
                metricPick.SelectedItemPosition,
                sizes[sizePick.SelectedItemPosition],
                showLabel.Checked,
                heatBar.Checked,
                new TileChart(windows[windowPick.SelectedItemPosition], overlay.Checked, zoom.Checked,
                    fill.Checked, axis.Checked, (ChartSmoothing)smoothingPick.SelectedItemPosition),
                Limits(warn, danger, falling.Checked),
                new TileExtremum(lowest.Checked),
                Rounding(roundingPick.SelectedItemPosition),
                caption.Text ?? "",
                // Имя плитки переживает правку: точка отсчёта дистанции хранится по нему, и
                // рождённое заново имя означало бы сброшенный счёт после смены размера.
                tile?.Id ?? "")))!
            .SetNegativeButton(Android.Resource.String.Cancel, (_, _) => { })!;

        if (remove is not null) dialog.SetNeutralButton(translate("TilesTileRemove"), (_, _) => remove());

        return dialog.Show()!;
    }

    /// <param name="named">Имя правимой плитки; пусто — плитка новая, имя ей даётся здесь.</param>
    private static MetricTile Result(IReadOnlyList<MetricDescriptor> metrics, int kind, int chosen,
        TileSize size, bool showLabel, bool heatBar, TileChart options, TileLimits? limits,
        TileExtremum extremum, int? decimals, string caption, string named)
    {
        string tile = named.Length > 0 ? named : MetricTile.NewId();

        // У дистанции величина не спрашивается: она считается из общего пробега колеса, и другой
        // величины у этого вида не бывает.
        string id = kind == 3
            ? Odometer
            : chosen >= 0 && chosen < metrics.Count ? metrics[chosen].Id : "";

        if (kind == 4 || id.Length == 0) return MetricTile.Empty(size) with { Id = tile };

        return kind switch
        {
            1 => new MetricTile(id, TileKind.Chart, size, showLabel, options, limits, ShowHeatBar: heatBar,
                Decimals: decimals, Caption: caption) { Id = tile },
            2 => new MetricTile(id, TileKind.Extremum, size, showLabel, Limits: limits, Extremum: extremum,
                ShowHeatBar: heatBar, Decimals: decimals, Caption: caption) { Id = tile },
            3 => new MetricTile(id, TileKind.Trip, size, showLabel, Limits: limits, ShowHeatBar: heatBar,
                Decimals: decimals, Caption: caption) { Id = tile },
            _ => new MetricTile(id, TileKind.Value, size, showLabel, Limits: limits, ShowHeatBar: heatBar,
                Decimals: decimals, Caption: caption) { Id = tile },
        };
    }

    /// <summary>
    /// Величина, из которой считается дистанция, — общий пробег колеса. Имя, а не описание: у вида
    /// «дистанция» выбора величины нет, и держать его в трёх местах незачем.
    /// </summary>
    internal const string Odometer = "totaldistance";

    /// <summary>
    /// Пункты выбора округления: «по умолчанию» и сами числа. Числа переводить нечего — цифра
    /// читается на любом языке, как и доли в подписи размера.
    /// </summary>
    private static string[] Roundings(Func<string, string> translate) =>
        [translate("TilesRoundingDefault"),
            .. MetricRounding.Choices.Select(digits => digits.ToString(CultureInfo.InvariantCulture))];

    /// <summary>
    /// Какой пункт списка стоит против сохранённого числа. Нулевой — «по умолчанию», и им же
    /// оборачивается число, которого в списке нет: <c>IndexOf</c> отвечает на такое −1.
    /// </summary>
    private static int RoundingIndex(int? decimals) =>
        decimals is { } chosen ? MetricRounding.Choices.ToList().IndexOf(chosen) + 1 : 0;

    /// <summary>Что выбрано в списке: первый пункт — «по умолчанию», остальные — сами числа.</summary>
    private static int? Rounding(int position) =>
        position >= 1 && position <= MetricRounding.Choices.Count ? MetricRounding.Choices[position - 1] : null;

    /// <summary>
    /// Свои пороги плитки. Пустое поле — не ноль, а «нет своего»: ноль в настройках тревог означает
    /// «не предупреждать», и путать эти два ответа нельзя. Пуст хоть один — берём пороги из настроек.
    /// </summary>
    private static TileLimits? Limits(EditText warn, EditText danger, bool falling) =>
        Read(warn) is { } low && Read(danger) is { } high
            ? new TileLimits(low, high, !falling)
            : null;

    private static double? Read(EditText field) =>
        double.TryParse(field.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double value)
        || double.TryParse(field.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
            ? value
            : null;

    private static EditText Number(Context context, string hint, double? value)
    {
        var field = new EditText(context)
        {
            Hint = hint,
            InputType = Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagDecimal,
            Text = value is { } number ? number.ToString("0.##", CultureInfo.CurrentCulture) : "",
        };

        field.SetSingleLine(true);

        return field;
    }

    /// <summary>
    /// Пустое место — первым пунктом того же списка, а не отдельной кнопкой: выбирают-то одно и то
    /// же — чем будет плитка.
    /// </summary>
    private static string[] Choices(Func<string, string> translate, IReadOnlyList<MetricDescriptor> metrics) =>
        [.. metrics.Select(metric => Name(translate, metric))];

    /// <summary>
    /// Величина с единицей — «Двигатель, °C». Без неё в списке два «Двигателя» (градусы и ватты) и
    /// «Максимум» неизвестно чего: имена коротки нарочно, а единица разводит их даром.
    /// </summary>
    private static string Name(Func<string, string> translate, MetricDescriptor metric) =>
        metric.UnitKey is { } unit ? $"{translate(metric.LabelKey)}, {translate(unit)}" : translate(metric.LabelKey);

    /// <summary>Что выбрано сейчас, именем величины. Пусто — выбрано пустое место.</summary>
    private static string ChosenMetric(Spinner pick, IReadOnlyList<MetricDescriptor> metrics)
    {
        int chosen = pick.SelectedItemPosition;

        return chosen < 0 || chosen >= metrics.Count ? "" : metrics[chosen].Id;
    }

    /// <summary>Величины может не быть в списке — тогда меню открывается на пустом месте, а не падает.</summary>
    private static int IndexOfMetric(IReadOnlyList<MetricDescriptor> metrics, string id)
    {
        for (int index = 0; index < metrics.Count; index++)
        {
            if (metrics[index].Id == id) return index;
        }

        return 0;
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
        caption.SetTextSize(ComplexUnitType.Dip, TilesLayout.LabelSp);

        return caption;
    }
}
