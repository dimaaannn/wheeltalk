using Android.App;
using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Core.Dashboard;
using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Правка справочного блока центра: список того, что показано, кнопки «вверх», «вниз», «убрать» и
/// «добавить» (решение владельца 12.08.2026 — «редактирование долгим тапом, добавление элементов»),
/// а с 13.08.2026 — и косая, которой две строки складывают в пару либо разбирают обратно.
/// <para>
/// <b>Почему окном, а не прямо на панели.</b> Правило панели старше этой задачи (прогон 3):
/// индикаторы независимы, всё рисуется канвой в своей вьюхе, <b>разметка после сборки не
/// трогается</b>. Редактор — это разметка: список, кнопки, прокрутка. Значит он живёт отдельным
/// окном, и открывает его хозяин активности, у которого есть <c>OwnedWindow</c>: окно без хозяина —
/// это <c>WindowLeaked</c> при первом же повороте телефона.
/// </para>
/// <para>
/// Сохранение — на каждую правку, отдельной кнопки «сохранить» нет: список короткий, действий три,
/// и «нажал ОК, а не сохранилось» здесь взяться неоткуда. Панель перерисовывается сама следующим
/// кадром — состав она читает из живых настроек.
/// </para>
/// </summary>
public static class CentreEditor
{
    /// <summary>
    /// Показать окно правки. Возвращает диалог — закрывать его будет хозяин (правило «у окна есть
    /// хозяин», <c>Architecture/WindowOwnershipTests</c>).
    /// </summary>
    /// <param name="rows">Что показано сейчас.</param>
    /// <param name="words">Ключ ресурса → слово: библиотека ресурсов приложения не видит.</param>
    /// <param name="save">Куда деть новый состав. Зовётся на каждую правку.</param>
    public static Dialog Show(
        Context context,
        IReadOnlyList<CenterRow> rows,
        Func<string, string> words,
        Action<IReadOnlyList<CenterRow>> save)
    {
        var current = rows.ToList();
        var list = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };

        var dialog = new AlertDialog.Builder(context)
            .SetTitle(words("CentreEditTitle"))!
            .SetView(Frame(context, list))!
            .SetPositiveButton(words("ButtonDone"), (_, _) => { })!
            .Create()!;

        void Redraw()
        {
            list.RemoveAllViews();

            for (int index = 0; index < current.Count; index++)
            {
                int at = index;
                Add(list, Row(context, CenterReadings.Title(current[at], words),
                    pair: Pair(current, at, changed => { current = changed; Apply(); }),
                    up: () => { Swap(current, at, at - 1); Apply(); },
                    down: () => { Swap(current, at, at + 1); Apply(); },
                    remove: () => { current.RemoveAt(at); Apply(); }));
            }

            if (current.Count < CenterLayout.MaxRows) Add(list, AddButton(context, words, added =>
            {
                current.Add(added);
                Apply();
            }));

            // Потолок — не запрет ради запрета: седьмая строка не встаёт по полу читаемости ни при
            // каком кегле (CenterLayout.MaxRows), и предлагать её значило бы предлагать пустоту.
            else Add(list, Hint(context, words("CentreEditFull")));
        }

        /// <summary>
        /// Правка принята: состав уходит в хранилище и список пересобирается.
        /// <para>
        /// <b>И то, и другое — вне пути отрисовки</b> (урок плана 31): это обработчик нажатия, а не
        /// кадр. Пересборка списка целиком оправдана его размером — шесть строк потолком, семь вью
        /// на всё окно; точечная правка стоила бы здесь больше кода, чем экономила бы работы.
        /// Запись в хранилище остаётся немедленной (решение «сохранять сразу»): она одна на нажатие,
        /// а не одна на кадр, и панель за окном её не ждёт — состав она читает из живых настроек.
        /// </para>
        /// </summary>
        void Apply()
        {
            save(current.ToList());
            Redraw();
        }

        Redraw();
        dialog.Show();

        return dialog;
    }

    private static View Frame(Context context, View content)
    {
        var scroll = new ScrollView(context);
        int pad = context.Dp(16);
        content.SetPadding(pad, pad / 2, pad, pad / 2);

        // Ширина — во всё окно, и это не украшение: список внутри раздаёт её весом, а вес делит
        // только то, что есть. Оставь <c>WrapContent</c> — и делить будет нечего.
        scroll.AddView(content, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        return scroll;
    }

    /// <summary>
    /// Ребёнок списка — <b>во всю его ширину</b>. Ширина здесь несущая: без неё строка сжимается до
    /// самой узкой своей части, и подпись встаёт столбиком по букве (телефон владельца, 12.08.2026 —
    /// «ШИМ макс» в один знак шириной на всю высоту окна).
    /// </summary>
    private static void Add(LinearLayout list, View child) => list.AddView(child,
        new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

    /// <summary>
    /// Строка списка: что показано и четыре действия над ним. Кнопки — знаками, они короче слов.
    /// <para>
    /// Подпись берёт <b>всё, что осталось от кнопок</b> (ширина 0 + вес 1), кнопки — по себе. Долями,
    /// а не пикселями: подписи разной длины («Заряд / Напряжение ▼» — самая длинная из нынешних),
    /// экраны разной ширины, и число, подогнанное под один, режет другой.
    /// </para>
    /// <para>
    /// <b>Косая стоит первой, крестик — последним.</b> Порядок не случаен: правка содержимого строки
    /// впереди, перестановка в середине, а то, что отнимает строку, — с краю, дальше всего от пальца,
    /// метящего в соседний знак.
    /// </para>
    /// </summary>
    private static View Row(Context context, string caption, Action? pair, Action up, Action down, Action remove)
    {
        var row = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);

        var label = new TextView(context) { Text = caption };
        label.SetTextSize(ComplexUnitType.Sp, 15);

        // Две строки — потолок: длинная подпись переносится, а не режет кнопки и не растит строку
        // без края.
        label.SetMaxLines(2);
        label.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        row.AddView(label, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        foreach (var (sign, click) in ((string, Action?)[])[(PairSign, pair), ("↑", up), ("↓", down), ("✕", remove)])
        {
            row.AddView(Button(context, sign, click), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        }

        return row;
    }

    /// <summary>
    /// Знак пары — <b>та самая косая, которой пара подписана на панели</b>: «t° / ▲», «Заряд % / V ▼».
    /// Не слово: словам в строке места нет (её ширину и без того делят четыре кнопки), а знак стоит
    /// ровно там, где человек его уже видел, и означает то же самое — «эти двое в одной строке».
    /// </summary>
    private const string PairSign = "/";

    /// <summary>
    /// Что сделает косая у этой строки. Пара — <b>разделится</b>, одиночная — <b>сложится с
    /// нижней</b>: один знак на два действия потому, что действие ровно одно — поставить косую или
    /// убрать, а что именно выйдет, видно в подписи прямо перед знаком. И оно обратимо одним
    /// нажатием, чем и объясняет себя тому, кто нажал впервые.
    /// <para>
    /// <c>null</c> — сейчас нельзя, и знак гаснет: место под ним остаётся, чтобы кнопки соседних
    /// строк не плясали. Нельзя в трёх случаях, и все они честные: под строкой нет соседа, сосед сам
    /// пара (третьему показанию в строке места нет) либо состав уже на потолке — разделение просит
    /// седьмую строку, которой не будет (<see cref="CenterLayout.CanSplit"/>). Отказ виден до
    /// нажатия, а внизу списка в этот миг стоит и его причина — «Больше строк в центр не помещается».
    /// </para>
    /// </summary>
    private static Action? Pair(List<CenterRow> rows, int at, Action<List<CenterRow>> changed)
    {
        if (CenterLayout.CanSplit(rows, at)) return () => changed([.. CenterLayout.Split(rows, at)]);

        return CenterLayout.CanMerge(rows, at) ? () => changed([.. CenterLayout.Merge(rows, at)]) : null;
    }

    private static View AddButton(Context context, Func<string, string> words, Action<CenterRow> added)
    {
        var button = new Button(context) { Text = "+ " + words("CentreEditAdd") };
        button.Click += (_, _) => Offer(context, words, added);

        return button;
    }

    /// <summary>
    /// Что можно добавить — плоским списком «величина + сторона» (<see cref="CenterReadings.Offered"/>):
    /// панель предлагает лишь то, что умеет показать, иначе выбранная строка рисовала бы прочерк
    /// вечно, и человек узнавал бы об этом уже на дороге.
    /// </summary>
    private static void Offer(Context context, Func<string, string> words, Action<CenterRow> added)
    {
        var choices = CenterReadings.Offered
            .SelectMany(offer => offer.Aspects.Select(aspect => new CenterReading(offer.Metric, aspect)))
            .ToList();

        string[] names = [.. choices.Select(choice => CenterReadings.Title(new CenterRow(choice, null), words))];

        new AlertDialog.Builder(context)
            .SetTitle(words("CentreEditAdd"))!
            .SetItems(names, (_, e) => added(new CenterRow(choices[e.Which], null)))!
            .Show();
    }

    /// <summary>
    /// Кнопка-знак. Ширина по себе, но не уже цели касания: 44 dp — та же мера, какой меряют палец
    /// на всех прочих экранах, и меньше неё промах становится правилом.
    /// <para>
    /// Действия нет (<c>null</c>) — кнопка гаснет средствами темы, а не исчезает: пропавшая двигала
    /// бы соседние знаки от строки к строке, и палец, привыкший к месту, попадал бы не туда.
    /// </para>
    /// </summary>
    private static View Button(Context context, string sign, Action? click)
    {
        var button = new Button(context) { Text = sign, Enabled = click is not null };

        // Обе ручки разом: SetMinimumWidth ставит минимум вью, но у Button есть ещё свой minWidth
        // из темы (~88 dp) — и три кнопки съедали весь диалог, оставляя подписи одну букву
        // (поймано прогоном 12.08.2026). Его перебивает только SetMinWidth. С четвёртым знаком это
        // стало ещё нужнее: подписи остаётся всё, что не забрали четыре цели касания.
        button.SetMinWidth(context.Dp(44));
        button.SetMinimumWidth(context.Dp(44));
        button.SetPadding(context.Dp(8), 0, context.Dp(8), 0);

        if (click is { } act) button.Click += (_, _) => act();

        return button;
    }

    private static View Hint(Context context, string text)
    {
        var hint = new TextView(context) { Text = text, Alpha = 0.7f };
        hint.SetTextSize(ComplexUnitType.Sp, 12);

        return hint;
    }

    /// <summary>Обмен соседей. Край списка — не действие: «вверх» у первого просто ничего не делает.</summary>
    private static void Swap(List<CenterRow> rows, int from, int to)
    {
        if (to < 0 || to >= rows.Count) return;

        (rows[from], rows[to]) = (rows[to], rows[from]);
    }
}
