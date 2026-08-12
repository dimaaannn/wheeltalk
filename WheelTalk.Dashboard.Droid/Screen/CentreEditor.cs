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
/// «добавить» (решение владельца 12.08.2026 — «редактирование долгим тапом, добавление элементов»).
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
    /// Строка списка: что показано и три действия над ним. Кнопки — знаками, они короче слов.
    /// <para>
    /// Подпись берёт <b>всё, что осталось от кнопок</b> (ширина 0 + вес 1), кнопки — по себе. Долями,
    /// а не пикселями: подписи разной длины («Заряд / Напряжение ▼» — самая длинная из нынешних),
    /// экраны разной ширины, и число, подогнанное под один, режет другой.
    /// </para>
    /// </summary>
    private static View Row(Context context, string caption, Action up, Action down, Action remove)
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

        foreach (var (sign, click) in ((string, Action)[])[("↑", up), ("↓", down), ("✕", remove)])
        {
            row.AddView(Button(context, sign, click), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        }

        return row;
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
    /// </summary>
    private static View Button(Context context, string sign, Action click)
    {
        var button = new Button(context) { Text = sign };

        // Обе ручки разом: SetMinimumWidth ставит минимум вью, но у Button есть ещё свой minWidth
        // из темы (~88 dp) — и три кнопки съедали весь диалог, оставляя подписи одну букву
        // (поймано прогоном 12.08.2026). Его перебивает только SetMinWidth.
        button.SetMinWidth(context.Dp(44));
        button.SetMinimumWidth(context.Dp(44));
        button.SetPadding(context.Dp(8), 0, context.Dp(8), 0);
        button.Click += (_, _) => click();

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
