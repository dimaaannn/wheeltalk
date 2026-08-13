using Android.Content;
using Android.Graphics;
using AndroidX.Core.Content;

namespace WheelTalk.Droid.Ui;

/// <summary>
/// Палитра <b>документных</b> экранов ролями: настройки, «Поездки», «Данные», поиск колёс, «Что
/// уйдёт» и общие виджеты. Значения живут в ресурсах (<c>values/colors.xml</c> и
/// <c>values-night/colors.xml</c>), а какой набор читать, решает система: смена темы Android — это
/// пересоздание активности, и ресурсы при нём читаются заново (план 33 §2).
/// <para>
/// <b>Почему ролями, а не цветами.</b> До перекладки половину экрана красила тема, а половину —
/// тёмные литералы в коде: вопрос «какая сейчас тема» решала система, а вопрос «каким цветом
/// карточка» — литерал, который о первом вопросе не знал. В тёмной теме этого не видно, оттого и
/// жило до снимка владельца 13.08.2026. Роль — это ответ на вопрос «что красим», и он один на обе
/// темы.
/// </para>
/// <para>
/// <b>Приборных поверхностей здесь нет.</b> Панель, «Цифры», плеер, шторка и полосы тревоги всегда
/// тёмные (решение владельца 13.08.2026), палитра у них своя и кодом
/// (<c>DashboardPalette</c>) — теме они безразличны, и путать эти два набора нельзя.
/// </para>
/// </summary>
internal static class DocPalette
{
    /// <summary>Фон страницы. Под ним меряется контраст всего, что на ней стоит.</summary>
    public static Color Surface(this Context context) => Role(context, Resource.Color.doc_surface);

    /// <summary>Заливка карточки раздела и её обводка: карточку различает граница, а не сама заливка.</summary>
    public static Color Card(this Context context) => Role(context, Resource.Color.doc_card);

    public static Color CardBorder(this Context context) => Role(context, Resource.Color.doc_card_border);

    /// <summary>Черта между строками внутри карточки.</summary>
    public static Color RowDivider(this Context context) => Role(context, Resource.Color.doc_row_divider);

    /// <summary>Обводка кнопок и полей — она объявляет орган управления, оттого мерится как компонент.</summary>
    public static Color Border(this Context context) => Role(context, Resource.Color.doc_border);

    /// <summary>Отбивка зависимой строки — черта, по которой видно, чья она.</summary>
    public static Color DependantBar(this Context context) => Role(context, Resource.Color.doc_dependant_bar);

    /// <summary>Основной текст страницы.</summary>
    public static Color TextPrimary(this Context context) => Role(context, Resource.Color.doc_text_primary);

    /// <summary>Заголовок строки настройки: тише основного текста, но всё ещё текст.</summary>
    public static Color TextTitle(this Context context) => Role(context, Resource.Color.doc_text_title);

    /// <summary>Знак на кнопке-квадрате: «+» и «−» листа правки.</summary>
    public static Color TextControl(this Context context) => Role(context, Resource.Color.doc_text_control);

    /// <summary>Невыбранное: чип, «Отмена».</summary>
    public static Color TextMuted(this Context context) => Role(context, Resource.Color.doc_text_muted);

    /// <summary>Заголовок раздела и подпись листа снизу.</summary>
    public static Color TextSecondary(this Context context) => Role(context, Resource.Color.doc_text_secondary);

    /// <summary>Подсказка под строкой.</summary>
    public static Color Hint(this Context context) => Role(context, Resource.Color.doc_hint);

    /// <summary>Самая тихая подсказка — та, что стоит в корне настроек под списком.</summary>
    public static Color HintDim(this Context context) => Role(context, Resource.Color.doc_hint_dim);

    /// <summary>Стрелка перехода на карточке раздела.</summary>
    public static Color Chevron(this Context context) => Role(context, Resource.Color.doc_chevron);

    /// <summary>Акцент выбранного и заливка кнопки «Готово».</summary>
    public static Color Accent(this Context context) => Role(context, Resource.Color.doc_accent);

    /// <summary>Текст поверх акцентной заливки — единственная роль, которая мерится не к фону страницы.</summary>
    public static Color OnAccent(this Context context) => Role(context, Resource.Color.doc_on_accent);

    /// <summary>«Своё» — значение, отличное от заводского.</summary>
    public static Color Override(this Context context) => Role(context, Resource.Color.doc_override);

    /// <summary>Плашка «своё» под ярлыком слоя.</summary>
    public static Color OverrideFill(this Context context) => Role(context, Resource.Color.doc_override_fill);

    /// <summary>Подсветка строки, к которой привели ссылкой: гаснет сама.</summary>
    public static Color Highlight(this Context context) => Role(context, Resource.Color.doc_highlight);

    /// <summary>Ссылка на связанную настройку.</summary>
    public static Color Link(this Context context) => Role(context, Resource.Color.doc_link);

    /// <summary>Предупреждение под строкой: «похоже на ошибку», в отличие от янтарного «не заводское».</summary>
    public static Color Warning(this Context context) => Role(context, Resource.Color.doc_warning);

    /// <summary>Выбранное колесо в корне настроек.</summary>
    public static Color Picked(this Context context) => Role(context, Resource.Color.doc_picked);

    /// <summary>Самая просевшая ячейка батареи на экране «Данные».</summary>
    public static Color CellLow(this Context context) => Role(context, Resource.Color.doc_cell_low);

    /// <summary>Самая полная ячейка батареи — вторая половина той же пары.</summary>
    public static Color CellHigh(this Context context) => Role(context, Resource.Color.doc_cell_high);

    /// <summary>Обводка карточки «Что уйдёт» — полупрозрачный серый, одинаковый в обеих темах.</summary>
    public static Color ShareBorder(this Context context) => Role(context, Resource.Color.doc_share_border);

    /// <summary>Общий разделитель <see cref="UiKit.Divider"/>: рисуется с прозрачностью на самой вьюхе.</summary>
    public static Color Divider(this Context context) => Role(context, Resource.Color.doc_divider);

    /// <summary>
    /// Значение роли из ресурсов. Через <see cref="ContextCompat"/>, а не <c>Resources.GetColor</c>:
    /// у последнего перегрузка без темы объявлена устаревшей с API 23, и на разных версиях она берёт
    /// цвет из разных мест.
    /// </summary>
    private static Color Role(Context context, int role) => new(ContextCompat.GetColor(context, role));
}
