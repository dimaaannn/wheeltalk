using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Меню действий плитки: короткий тап <b>по любой плитке</b> вне режима правки (решение владельца
/// 10.08.2026). Прежде тап у каждого вида значил своё — у крайнего сбрасывал пик, у графика
/// открывал просмотр, у прочих не значил ничего, — и узнать это можно было только попробовав.
/// <para>
/// <b>Надписи общие и короткие.</b> «Сбросить» — не «сбросить пик» и не «сбросить дистанцию»: что
/// именно сбрасывается, знает вид плитки, а человек и так смотрит на ту плитку, по которой нажал.
/// Пункт, которому у этого вида нечего делать, <b>погашен, а не спрятан</b>: меню у всех плиток
/// одинаковое, и место пунктов не должно прыгать от вида к виду.
/// </para>
/// <para>
/// Случайное касание открывает меню и ничего не рушит — за этим оно и заведено: сброс и
/// переименование стоят одного лишнего нажатия, а стёртый касанием счёт недель не вернуть.
/// </para>
/// </summary>
internal static class TileActions
{
    /// <param name="reset">Сбросить то, что у этого вида значит сброс. <c>null</c> — сбрасывать нечего.</param>
    /// <param name="rename">Переименовать плитку — та же своя подпись, что и в меню правки.</param>
    /// <param name="chart">Открыть полноэкранный просмотр. <c>null</c> — плитка не график.</param>
    /// <returns>
    /// Открытое окно — <b>хозяину</b>, а не в пустоту: диалог висит на окне активности, и брошенный
    /// он переживает её смерть (<c>WindowLeaked</c>, дамп владельца 10.08.2026). Закрывает его тот,
    /// кто открыл, — <c>TilesScreen</c> по уходу экрана из окна.
    /// </returns>
    public static Dialog Show(Context context, Func<string, string> translate, string title,
        Action? reset, Action rename, Action? chart)
    {
        List<(string Word, Action? Deed)> items =
        [
            (translate("TilesActionReset"), reset),
            (translate("TilesActionRename"), rename),
        ];

        if (chart is not null) items.Add((translate("TilesActionChart"), chart));

        var menu = new Menu(context, items);

        return new AlertDialog.Builder(context)
            .SetTitle(title)!
            .SetAdapter(menu, (_, args) => items[args.Which].Deed?.Invoke())!
            .SetNegativeButton(Android.Resource.String.Cancel, (_, _) => { })!
            .Show()!;
    }

    /// <summary>
    /// Спросить подпись плитки — и из меню действий, и из меню правки это один и тот же вопрос.
    /// Пустой ответ значит «называй по величине»: у стирания подписи должен быть тот же путь, что у
    /// её задания, иначе вернуть имя величины будет нечем.
    /// </summary>
    /// <returns>Открытое окно — хозяину, тем же правилом, что и меню.</returns>
    public static Dialog AskCaption(Context context, Func<string, string> translate, string current,
        string hint, Action<string> save)
    {
        var field = new EditText(context) { Text = current, Hint = hint };
        field.SetSingleLine(true);

        int pad = context.Dp(TilesLayout.PaddingDp * 2);
        var frame = new FrameLayout(context);
        frame.SetPadding(pad, pad, pad, pad);
        frame.AddView(field);

        return new AlertDialog.Builder(context)
            .SetTitle(translate("TilesTileCaption"))!
            .SetView(frame)!
            .SetPositiveButton(Android.Resource.String.Ok, (_, _) => save(field.Text ?? ""))!
            .SetNegativeButton(Android.Resource.String.Cancel, (_, _) => { })!
            .Show()!;
    }

    /// <summary>
    /// Список с погашенными пунктами. Своей разметки не заводим — платформенная строка списка та
    /// же, что у прочих меню приложения; гасится пункт цветом и запретом нажатия, а не пропажей.
    /// </summary>
    private sealed class Menu(Context context, List<(string Word, Action? Deed)> items)
        : ArrayAdapter<string>(context, Android.Resource.Layout.SimpleListItem1,
            [.. items.Select(item => item.Word)])
    {
        public override bool AreAllItemsEnabled() => false;

        public override bool IsEnabled(int position) => items[position].Deed is not null;

        public override View GetView(int position, View? convertView, ViewGroup parent)
        {
            var view = base.GetView(position, convertView, parent);

            if (view is TextView line) line.Alpha = IsEnabled(position) ? 1f : 0.4f;

            return view;
        }
    }
}
