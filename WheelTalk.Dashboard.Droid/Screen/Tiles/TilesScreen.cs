using Android.Content;
using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Metrics;
using WheelTalk.Core.Tiles;
using WheelTalk.Dashboard.Droid.Widgets;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Второй основной экран (план 23 §3): сетка плиток с числами живого колеса. Смотрят его не на
/// ходу — на стоянке, после поездки, при зарядке, — поэтому здесь нет ни лент, ни моргания: числа
/// стоят и читаются.
/// <para>
/// Тот же <see cref="IMainScreen"/>, что и панель: принимает посчитанное состояние кадра и ни к
/// сессии, ни к рекордеру, ни к контейнеру служб не ходит. Живой снимок приходит кадром
/// (<see cref="MainScreenFrame.Snapshot"/>).
/// </para>
/// <para>
/// Укладывает плитки <see cref="TileGridLayoutManager"/> — клеточная сетка с поиском места потоком.
/// Почему не <c>GridLayoutManager</c>, одобренный планом, написано там же: ряд «половина плюс
/// четыре четвертных» линейной сеткой не выкладывается.
/// </para>
/// <para>
/// <b>Правка раскладки</b> (шаг 6): долгий тап включает режим, в нём плитку тащат пальцем, а тап по
/// плитке открывает её меню — величина, размер, «убрать». Новую заводит кнопка «плюс». «Сохранить»
/// уносит раскладку в хранилище (<see cref="ITileLayoutStore"/>), выданное хозяином экрана; без
/// него правки живут до пересборки экрана.
/// </para>
/// </summary>
public sealed class TilesScreen : IMainScreen
{
    private readonly Context _context;
    private readonly Func<string, string> _translate;
    private readonly IMetricHistory? _history;
    private readonly DashboardOptions _options;
    private readonly DashboardPalette _palette;
    private readonly TilesRoot _root;
    private readonly RecyclerView _list;
    private readonly View _buttons;

    /// <summary>
    /// Подсказка про шторку — тот же знак, что у панели (план 25 §0.2). Раньше её рисовал хром
    /// панели, и на плитках шторку было не найти вовсе: сама шторка общая на оба экрана, а вход в
    /// неё показывал только один из них.
    /// </summary>
    private readonly SheetHintDrawable _hint = new();

    /// <summary>
    /// Плашка связи — тот же рисовальщик, что у панели, по тому же прецеденту, что и галочка выше:
    /// связь принадлежит приложению, и экран, на котором её не видно, молчит о беде (баг владельца
    /// 09.08.2026 — «таблички не видно на плитках»). Имя колеса при живой связи плитки не рисуют —
    /// у них наверху не пустой центр панели, а плитка с содержимым.
    /// </summary>
    private readonly LinkBadgeDrawable _link;

    private readonly TileAdapter _adapter;
    private readonly int _padding;

    /// <summary>Была ли плашка на прошлом кадре: гашение — тоже перерисовка, одной последней.</summary>
    private bool _linkShown;

    private int _topInset = -1;
    private bool _editing;
    private long _polledAt;

    /// <summary>Список едет прямо сейчас: пока едет, графики не перечитываются (план 31 §3.2).</summary>
    private bool _scrolling;

    /// <summary>
    /// Открытое поверх экрана окно — полноэкранный просмотр графика либо меню плитки. Держится
    /// <b>здесь</b>, потому что диалог висит на окне активности, а не на нашей ветви вью: брошенный,
    /// он переживает свою активность и утекает вместе с ней (дамп владельца 10.08.2026 —
    /// <c>WindowLeaked</c> со стеком от тапа по плитке). Одно на двоих: два таких окна разом не
    /// открыть — оба открываются тем же тапом, который тут же и занят.
    /// </summary>
    private Dialog? _overlay;

    /// <summary>Раскладка на входе в режим правки: к ней возвращает «отменить» и кнопка «назад».</summary>
    private IReadOnlyList<MetricTile> _beforeEditing = [];

    /// <param name="translate">
    /// Ключ ресурса → слово. Слова — забота вызывающего, как у подписей шторки: у приложения они
    /// переводимые, у стенда свои, а библиотека ресурсов приложения не видит.
    /// </param>
    /// <param name="history">
    /// Откуда плитки-графики берут историю. <c>null</c> — истории нет вовсе (запись выключена, база
    /// не открыта): графики останутся пустыми, остальные плитки работают как работали.
    /// </param>
    /// <param name="layout">
    /// Где живёт собранная человеком раскладка (план 23 §3.4). <c>null</c> — хранилища нет: экран
    /// начинает с зашитой раскладки, а правки живут до его пересборки.
    /// </param>
    /// <param name="trips">
    /// Точки отсчёта дистанций — <b>общие с центром главного экрана</b>: хранилище у них одно, а
    /// каждый экземпляр пишет свой набор целиком (<see cref="TripPoints"/>). <c>null</c> — точек
    /// снаружи не дали: экран заведёт свои, они умрут вместе с ним, а дистанция после перезапуска
    /// начнётся заново.
    /// </param>
    /// <param name="wheel">
    /// Чьё колесо сейчас на связи — адресом. Дистанция считается по колесу (решение владельца
    /// 10.08.2026), и знать, какое из них выбрано, экрану неоткуда: это забота хозяина. Пусто —
    /// колесо не выбрано, и плитка-дистанция честно молчит.
    /// </param>
    public TilesScreen(Context context, DashboardOptions options, Func<string, string> translate,
        IMetricHistory? history = null, ITileLayoutStore? layout = null,
        TripPoints? trips = null, Func<string>? wheel = null)
    {
        _context = context;
        _translate = translate;
        _history = history;
        _options = options;
        _palette = options.Palette;
        _padding = context.Dp(ListPaddingDp);
        _adapter = new TileAdapter(context, options, translate, layout, layout?.Load() ?? TilesLayout.Fixed,
            trips ?? new TripPoints(null), wheel ?? (() => ""));

        _list = new RecyclerView(context);
        _list.SetLayoutManager(LayoutManager(context));
        _list.SetAdapter(_adapter);
        _list.SetBackgroundColor(options.Palette.Background);
        // Отступ сверху отдан инсету статус-бара, и содержимое под ним должно прокручиваться, а не
        // обрезаться по краю паддинга.
        _list.SetClipToPadding(false);
        _list.AddOnItemTouchListener(new TileTouch(context, this));

        // Пока список едет, чтение истории и стройка графиков ждут: их работа не срочна (окно едет
        // само), а кадр прокрутки дорог. По остановке ближайший же кадр их и запустит.
        _list.AddOnScrollListener(new ScrollWatch(scrolling => _scrolling = scrolling));

        new ItemTouchHelper(new DragCallback(this)).AttachToRecyclerView(_list);

        _buttons = EditButtons(context, options.Palette);

        _link = new LinkBadgeDrawable { Options = options, NameOnLive = false };
        _root = new TilesRoot(context, _hint, _link, options.Palette.Ink,
            () => OnIntent?.Invoke(MainScreenIntent.ShowSheet),
            () => OnIntent?.Invoke(MainScreenIntent.ShowConnection),
            CloseOverlay);
        _root.AddView(_list, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _root.AddView(_buttons, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent,
            GravityFlags.Bottom));
    }

    public View View => _root;

    /// <summary>
    /// Одно намерение плитки всё же подают — <see cref="MainScreenIntent.ShowSheet"/> по тапу в
    /// галочку (план 25 §0.2). Правка раскладки за пределы экрана по-прежнему не выходит.
    /// </summary>
    public Action<MainScreenIntent>? OnIntent { get; set; }

    public void Show(MainScreenFrame frame)
    {
        ApplyInset((int)frame.TopInset);

        // В правке галочки нет: низ экрана занят «сохранить/отменить», и подсказка про шторку там
        // спорила бы с ними и за место, и за касание.
        _hint.Visible = frame.ShowSheetHint && !_editing;

        _link.Phase = frame.LinkPhase;
        _link.StateText = frame.LinkText;
        _link.Seconds = frame.LinkSeconds;
        _link.WheelName = frame.WheelName;
        _link.SpeedKmh = frame.Reading?.SpeedKmh ?? 0;

        // Список сам не знает, что поверх него живёт плашка: пока она видна (и один кадр после —
        // стереть погасшую), корень перерисовывается кадром. JustConnected не Live — зелёная
        // плашка мигает и тает этим же путём.
        bool linkShown = frame.LinkPhase != LinkPhase.Live;
        if (linkShown || _linkShown) _root.Invalidate();
        _linkShown = linkShown;

        // Пока список едет, снимок не разносится по плиткам: перестановка текста семнадцати плиток
        // стоила до 20 мс и вклинивалась в кадр прокрутки (план 31 §3.1а). По остановке ближайший
        // кадр принесёт свежий снимок сам — устареть числа успевают не больше чем на 200 мс, а на
        // летящем экране их всё равно не читают. Плитка, въехавшая в экран во время прокрутки,
        // получает текущий снимок при привязке — она этой стражи не ждёт.
        if (!_scrolling) _adapter.Render(frame.Snapshot);
        PollCharts();
    }

    /// <summary>
    /// Перечитать историю показанных графиков. Зовётся с кадром, а работает раз в секунду-две
    /// (<see cref="TilesLayout.ChartPollMs"/>): своего таймера заводить незачем — кадры и так идут,
    /// а лишний источник частоты дал бы биение с ними.
    /// <para>
    /// Обходятся <b>показанные</b> плитки, а не вся раскладка: то, что уехало за край, читать из
    /// базы не нужно, а вернётся оно уже с точками.
    /// </para>
    /// </summary>
    private void PollCharts()
    {
        if (_history is null) return;

        // В прокрутке не читаем и не строим: окно графика едет, секунда ожидания законна, а вот
        // кадр, в который вклинилась стройка данных, — пропущенный кадр (план 31 §3.2).
        if (_scrolling) return;

        long now = Environment.TickCount64;
        if (now - _polledAt < TilesLayout.ChartPollMs) return;

        _polledAt = now;

        for (int index = 0; index < _list.ChildCount; index++)
        {
            if (_list.GetChildAt(index) is not ChartTileView chart) continue;

            int position = _list.GetChildAdapterPosition(chart);
            if (position < 0 || _adapter.TileAt(position) is not { Chart: { } options } tile) continue;

            _ = FillAsync(chart, tile.MetricId, options);
        }
    }

    /// <summary>
    /// Прочитать историю и <b>собрать по ней набор данных — всё вне потока отрисовки</b>. Главному
    /// потоку достаётся только вручение готового (<see cref="ChartTileView.ShowData"/>).
    /// <para>
    /// Корзин просим вдвое меньше, чем точек влезает в линию: из каждой история отдаёт минимум и
    /// максимум (план 23 §5.6), и плитка в 700 px, попросившая 700 корзин, получала 1334 точки —
    /// вдвое больше, чем можно нарисовать, и вдвое дороже по стройке.
    /// </para>
    /// </summary>
    private async Task FillAsync(ChartTileView chart, string metricId, TileChart options)
    {
        var to = DateTimeOffset.Now;
        var from = to - options.Window;
        int buckets = Math.Max(1, chart.Points / ChartTileView.PointsPerBucket);

        // ConfigureAwait(false) здесь не украшение, а суть правки: без него продолжение вернулось бы
        // на главный поток, и стройка набора снова считалась бы в кадре.
        var points = await _history!
            .ReadAsync(metricId, from, to, buckets, CancellationToken.None)
            .ConfigureAwait(false);

        var data = ChartLine.Build(points, _options.Palette, label: "", from, options);

        // Готовое вручается тому, кто рисует: подмена Data и Invalidate — работа главного потока.
        chart.Post(() => chart.ShowData(data, from, to));
    }

    public void Tap(float windowX, float windowY)
    {
    }

    /// <summary>
    /// Смена колеса: крайние значения прежнего к новому не относятся. Зовёт хозяин экрана — только
    /// он знает, что колесо сменилось; кадр этого не несёт, в нём нет адреса.
    /// </summary>
    public void WheelChanged() => ResetExtremes();

    /// <summary>Сбросить крайние значения плиток. Своё имя у действия остаётся: стенд зовёт его кнопкой.</summary>
    public void ResetExtremes() => _adapter.ResetExtremeTiles();

    /// <summary>
    /// «Назад» в режиме правки значит «не сохранять» — то же, что кнопка «отменить». Иначе кнопка
    /// закрывала бы приложение вместе с незакрытой правкой.
    /// </summary>
    public bool Back()
    {
        if (!_editing) return false;

        StopEditing(save: false);
        return true;
    }

    /// <summary>
    /// Верхний инсет забирает сам экран, как и панель: фон уходит под статус-бар, а числа
    /// начинаются ниже него. Переставляется только при изменении — правка паддинга тянет перекладку
    /// всей сетки, а кадров шестьдесят в секунду.
    /// </summary>
    private void ApplyInset(int top)
    {
        if (_topInset == top) return;

        _topInset = top;
        _root.TopInset = top;
        ApplyPadding();
    }

    /// <summary>
    /// Отступы списка. Нижний в правке даёт **запас прокрутки** (план 25 §0.3): без него последний
    /// ряд плиток упирается в «сохранить/отменить» и в зону вызова шторки, и до него не добраться
    /// ни пальцем, ни глазом.
    /// <para>
    /// Запас считается, а не выбирается: высота ряда кнопок плюс зона жеста шторки. Магическое
    /// число разошлось бы с обоими при первой же правке — а 128 dp зоны подобраны 04.08.2026 и
    /// живут своей жизнью (<c>quick-commands-design.md</c> §2).
    /// </para>
    /// </summary>
    private void ApplyPadding()
    {
        // Запас нужен ВСЕГДА, а не только в правке. Галочка стоит поверх списка и забирает себе
        // касания в своём пятне — без запаса нижняя плитка оказывается под ней, и по ней просто не
        // попасть. На панели такого не было: там внизу нечего нажимать, а здесь есть.
        int bottom = _padding + _context.Dp(HintReserveDp);

        if (_editing) bottom += _buttons.Height + _context.Dp(SheetGestureZoneDp);

        _list.SetPadding(_padding, _topInset + _padding, _padding, bottom);
    }

    /// <summary>
    /// Долгий тап включает режим правки — привычный для сеток жест, которому не надо учить
    /// (план 23 §3.2). Ловится он <b>всюду на сетке</b>: и по плитке, и по фону между ними, и по
    /// пустому полю под последней строкой.
    /// <para>
    /// <b>Почему не только по плитке.</b> Пустая раскладка законна и переживает перезапуск (человек
    /// убрал все плитки — <c>TileLayoutJson.Read</c> хранит это как есть), а хвататься в ней не за
    /// что: экран без плиток не пускал обратно в правку вовсе, и собрать его заново было нечем
    /// (владелец, 12.08.2026 — «если удалить все таблички, в редактор не зайти»). Вход, работающий
    /// лишь тогда, когда экран уже собран, — не вход.
    /// </para>
    /// <para>
    /// <b>С перетаскиванием это не спорит, и разведены они режимом, а не координатой.</b> В самой
    /// правке долгий тап занят: им берут плитку и тащат, и делает это <see cref="ItemTouchHelper"/>
    /// своим счётом — по плитке под пальцем и только при <c>IsLongPressDragEnabled</c>, то есть
    /// когда <see cref="_editing"/>. Здешний вход работает ровно наоборот — только вне правки, —
    /// и потому одно не может случиться вместо другого ни в одной точке экрана.
    /// </para>
    /// </summary>
    private void LongPress()
    {
        if (_editing) return;

        _beforeEditing = _adapter.Snapshot();
        SetEditing(true);
    }

    /// <summary>
    /// Короткий тап по плитке: в правке — её меню правки, вне правки — <b>меню действий</b>
    /// (решение владельца 10.08.2026). Мимо плиток он не значит ничего: правку заканчивают кнопкой,
    /// а не промахом — иначе непонятно, легла она или потерялась.
    /// <para>
    /// Прежде тап у каждого вида значил своё — сбрасывал пик, открывал график, — и это снято: одно
    /// действие на жест, а <b>что</b> оно сделает, человек читает в меню, а не вспоминает.
    /// </para>
    /// </summary>
    private void SingleTap(float x, float y)
    {
        if (_list.FindChildViewUnder(x, y) is not { } view) return;

        int position = _list.GetChildAdapterPosition(view);
        if (position < 0) return;

        if (_editing)
        {
            ShowEditor(position);
            return;
        }

        ShowActions(position, view as TileView);
    }

    /// <summary>
    /// Меню действий плитки. Сброс делает сама плитка — только она знает, что у неё сбрасывается;
    /// переименование правит раскладку и тут же её сохраняет: вне режима правки кнопки «сохранить»
    /// нет, и уйти правке некуда, кроме хранилища.
    /// <para>
    /// <b>Всякое открытое отсюда окно уходит в <see cref="_overlay"/></b> — и само меню, и то, что
    /// оно откроет следом: просмотр графика или вопрос о подписи. Хозяин у окна один, и меняется
    /// оно на ходу: список закрывается по выбору пункта, а на его место встаёт следующее окно.
    /// Брошенное здесь окно — это <c>WindowLeaked</c> при смерти экрана, тот самый, что уже был
    /// пойман дампом 10.08.2026.
    /// </para>
    /// </summary>
    private void ShowActions(int position, TileView? view)
    {
        var tile = _adapter.TileAt(position);
        if (tile.Kind == TileKind.Empty) return;

        var metric = MetricCatalogue.Find(tile.MetricId);

        Action? chart = _history is { } history && tile is { Kind: TileKind.Chart, Chart: { } options }
            && metric is not null
            ? () => _overlay = ChartViewer.Show(_context, _options, history, metric,
                _adapter.LabelOf(tile, metric), metric.UnitKey is { } unit ? _translate(unit) : "",
                options, tile.Limits, tile.Decimals)
            : null;

        _overlay = TileActions.Show(_context, _translate, _adapter.LabelOf(tile, metric),
            view is { CanReset: true } ? view.ResetValue : null,
            () => _overlay = TileActions.AskCaption(_context, _translate, tile.Caption,
                metric is not null ? _translate(metric.LabelKey) : "",
                caption => _adapter.Rename(position, caption)),
            chart);
    }

    /// <summary>
    /// Экран уходит из окна — открытое поверх него окно уходит с ним. Это и есть починка того, что
    /// осталось в дампе владельца 10.08.2026: активность уничтожили с открытым просмотром графика,
    /// и её окно утекло (<c>WindowLeaked</c>) вместе с диалогом, ветвью вью, чартом и его данными.
    /// Закрытие заодно снимает и догоняющее заполнение — просмотр гасит свою задачу по
    /// <c>DismissEvent</c>.
    /// </summary>
    private void CloseOverlay()
    {
        if (_overlay is { IsShowing: true } overlay) overlay.Dismiss();
        _overlay = null;
    }

    /// <param name="position">Правим плитку с этим номером либо <c>null</c> — заводим новую.</param>
    private void ShowEditor(int? position)
    {
        var tile = position is { } index ? _adapter.TileAt(index) : null;

        _overlay = TileEditor.Show(_context, _translate, tile,
            saved =>
            {
                if (position is { } index)
                {
                    _adapter.Replace(index, saved);
                    return;
                }

                _adapter.Add(saved);

                // Новая плитка встаёт в конец, а конец бывает за краем экрана: без этого «нажал ОК —
                // и ничего не изменилось», хотя плитка уже есть.
                _list.SmoothScrollToPosition(_adapter.ItemCount - 1);
            },
            position is { } removed ? () => _adapter.RemoveAt(removed) : null);
    }

    /// <summary>
    /// Конец правки: сохранить — оставить как есть, отменить — вернуть раскладку, какой она была на
    /// входе в режим. До этого мига правки живут только в адаптере, а хранилище
    /// (<see cref="ITileLayoutStore"/>) пишется одним разом: пиши оно на каждое действие, «отменить»
    /// отменяло бы лишь последнее.
    /// </summary>
    private void StopEditing(bool save)
    {
        if (save) _adapter.Keep();
        else _adapter.Restore(_beforeEditing);

        SetEditing(false);
    }

    private void SetEditing(bool editing)
    {
        _editing = editing;
        _adapter.Editing = editing;
        _buttons.Visibility = editing ? ViewStates.Visible : ViewStates.Gone;

        // Высота ряда кнопок известна только после того, как он измерен, — на первом включении
        // правки её ещё нет. Отсюда Post, а не прямой вызов: отступ ставится, когда есть что
        // прибавлять.
        _root.Post(ApplyPadding);
    }

    /// <summary>
    /// Зона вызова шторки снизу, точки экрана — <c>quick-commands-design.md</c> §2, подобрана
    /// 04.08.2026. Здесь она нужна только как слагаемое запаса прокрутки в правке: сама зона живёт
    /// в приложении, и трогать её отсюда нельзя.
    /// </summary>
    /// <summary>
    /// Поля списка плиток. Числом их знают двое: сам список и подбор кегля — клетка сетки режется по
    /// ширине списка <b>без</b> этих полей (<c>TileGridLayoutManager</c>), и разойтись им нельзя.
    /// </summary>
    private const int ListPaddingDp = 6;

    private const int SheetGestureZoneDp = 128;

    /// <summary>
    /// Место под галочкой, точки экрана: её цель касания (32 dp) плюс отступ от неё. Список
    /// прокручивается выше на эту величину, иначе последняя плитка живёт под знаком, который
    /// перехватывает касания раньше неё.
    /// </summary>
    private const int HintReserveDp = 40;

    /// <summary>
    /// Корень экрана, умеющий подсказку про шторку и плашку связи. Отдельным типом, потому что
    /// рисовать их надо **поверх** списка и ловить по ним тап раньше, чем его возьмёт список:
    /// галочка стоит у самого низа, плашка — у самого верха, и под обеими всегда какая-нибудь
    /// плитка.
    /// </summary>
    private sealed class TilesRoot(
        Context context, SheetHintDrawable hint, LinkBadgeDrawable link, Color ink,
        Action onHintTapped, Action onLinkTapped, Action onDetached)
        : FrameLayout(context)
    {
        private readonly RectF _bounds = new();

        /// <summary>
        /// Уход из окна — единственная весть об конце экрана, которая доходит до библиотеки:
        /// <c>OnDestroy</c> принадлежит активности, а её у экрана нет. Приходит она и при смене
        /// экрана корешком, и при уничтожении активности — оба случая одинаково требуют убрать
        /// открытое поверх окно.
        /// </summary>
        protected override void OnDetachedFromWindow()
        {
            onDetached();
            base.OnDetachedFromWindow();
        }

        /// <summary>Инсет статус-бара: плашка стоит под ним, как у панели (`DashboardView.LinkArea`).</summary>
        public int TopInset { get; set; }

        public override bool OnInterceptTouchEvent(MotionEvent? e)
        {
            if (e?.Action == MotionEventActions.Down && HitsLink(e.GetX(), e.GetY()))
            {
                onLinkTapped();
                return true;
            }

            if (e?.Action == MotionEventActions.Down && HitsHint(e.GetX(), e.GetY()))
            {
                onHintTapped();
                return true;
            }

            return base.OnInterceptTouchEvent(e);
        }

        protected override void DispatchDraw(Canvas canvas)
        {
            base.DispatchDraw(canvas);

            float density = Resources!.DisplayMetrics!.Density;
            _bounds.Set(0, 0, Width, Height);
            hint.Draw(canvas, _bounds, density, ink);

            _bounds.Set(0, TopInset, Width, Height);
            link.Draw(canvas, _bounds, density);
        }

        private bool HitsHint(float x, float y)
        {
            _bounds.Set(0, 0, Width, Height);
            return hint.Hits(_bounds, Resources!.DisplayMetrics!.Density, x, y);
        }

        /// <summary>Плашки нет — `Hits` сам отвечает «нет»: в Live под этим местом обычная плитка.</summary>
        private bool HitsLink(float x, float y)
        {
            _bounds.Set(0, TopInset, Width, Height);
            return link.Hits(_bounds, Resources!.DisplayMetrics!.Density, x, y);
        }
    }

    /// <summary>
    /// Который из двух укладчиков собирает сетку — <see cref="TilesLayout.PackTiles"/>. Оба живут
    /// рядом, пока не решено глазами: у сетки из плана дырки под низкими плитками остаются пустыми,
    /// свой их заполняет.
    /// </summary>
    private RecyclerView.LayoutManager LayoutManager(Context context)
    {
        if (TilesLayout.PackTiles) return new TileGridLayoutManager(context, _adapter.SizeAt, _adapter.DividerAt);

        var grid = new GridLayoutManager(context, TilesLayout.Columns);
        grid.SetSpanSizeLookup(new TileSpans(_adapter.SizeAt));

        return grid;
    }

    /// <summary>Ширина плитки в колонках — единственное, что <c>GridLayoutManager</c> умеет спросить.</summary>
    private sealed class TileSpans(Func<int, TileSize> sizeAt) : GridLayoutManager.SpanSizeLookup
    {
        public override int GetSpanSize(int position) => sizeAt(position).Columns;
    }

    /// <summary>
    /// Полоса режима правки: завести плитку, отменить, сохранить. Внизу и во всю ширину — так её не
    /// закрывает палец, которым тащат плитку, и так видно, что правка ещё не закончена.
    /// </summary>
    private View EditButtons(Context context, DashboardPalette palette)
    {
        var row = new LinearLayout(context)
        {
            Orientation = Android.Widget.Orientation.Horizontal,
            Visibility = ViewStates.Gone,
        };

        int pad = context.Dp(TilesLayout.ButtonGapDp);
        row.SetPadding(pad, pad, pad, pad);
        row.SetBackgroundColor(palette.Background);

        row.AddView(Button(context, palette, "+", () => ShowEditor(null)), Weighted(context, 0.5f));
        row.AddView(Button(context, palette, _translate("TilesEditCancel"), () => StopEditing(save: false)), Weighted(context, 1f));
        row.AddView(Button(context, palette, _translate("TilesEditSave"), () => StopEditing(save: true)), Weighted(context, 1f));

        return row;
    }

    private static LinearLayout.LayoutParams Weighted(Context context, float weight)
    {
        int gap = context.Dp(TilesLayout.ButtonGapDp);

        return new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, weight)
        {
            LeftMargin = gap,
            RightMargin = gap,
        };
    }

    private static View Button(Context context, DashboardPalette palette, string text, Action tapped)
    {
        var background = new GradientDrawable();
        background.SetShape(ShapeType.Rectangle);
        background.SetCornerRadius(context.Dp(TilesLayout.CornerRadiusDp));
        background.SetColor(Color.Argb(TilesLayout.BackgroundAlpha, palette.Dim.R, palette.Dim.G, palette.Dim.B));

        var button = new TextView(context)
        {
            Text = text,
            Gravity = GravityFlags.Center,
            Background = background,
        };

        int pad = context.Dp(TilesLayout.ButtonPaddingDp);
        button.SetPadding(pad, 0, pad, 0);

        // Полоса правки — 48 dp (план плиток §6): это и цель касания, и одинаковая высота у всех
        // трёх кнопок, чтобы полоса читалась полосой, а не тремя разными надписями.
        button.SetMinimumHeight(context.Dp(TilesLayout.ButtonsHeightDp));
        button.SetTextColor(palette.Ink);
        button.SetTextSize(ComplexUnitType.Dip, TilesLayout.ButtonSp);
        button.Click += (_, _) => tapped();

        return button;
    }

    /// <summary>
    /// Слушатель прокрутки: сообщает хозяину, едет список или стоит. Отдельным классом, а не
    /// лямбдой, потому что <see cref="RecyclerView.OnScrollListener"/> — абстрактный класс
    /// платформы, наследника ему не заменить делегатом.
    /// </summary>
    private sealed class ScrollWatch(Action<bool> onChanged) : RecyclerView.OnScrollListener
    {
        public override void OnScrollStateChanged(RecyclerView recyclerView, int newState) =>
            onChanged(newState != RecyclerView.ScrollStateIdle);
    }

    /// <summary>
    /// Жесты сетки. Один класс на две роли намеренно: <see cref="RecyclerView"/> отдаёт события
    /// только слушателю своего вида, а разбирает их <see cref="GestureDetector"/>, и разводить их по
    /// двум классам значило бы завести третий — мост между ними.
    /// <para>
    /// События не перехватываются (<c>false</c> из <see cref="OnInterceptTouchEvent"/>): прокрутка
    /// сетки и перетаскивание плитки должны видеть тот же палец.
    /// </para>
    /// </summary>
    private sealed class TileTouch : GestureDetector.SimpleOnGestureListener, RecyclerView.IOnItemTouchListener
    {
        private readonly TilesScreen _screen;
        private readonly GestureDetector _gestures;

        public TileTouch(Context context, TilesScreen screen)
        {
            _screen = screen;
            _gestures = new GestureDetector(context, this);
        }

        public bool OnInterceptTouchEvent(RecyclerView view, MotionEvent e)
        {
            _gestures.OnTouchEvent(e);
            return false;
        }

        public void OnTouchEvent(RecyclerView view, MotionEvent e)
        {
        }

        public void OnRequestDisallowInterceptTouchEvent(bool disallow)
        {
        }

        /// <summary>
        /// Долгий тап приходит сюда <b>с любого места сетки</b>: слушатель висит на самом
        /// <see cref="RecyclerView"/>, а не на плитках, и события он получает даже тогда, когда
        /// плиток нет вовсе. Координаты вход в правку не спрашивает — ему довольно того, что палец
        /// на экране (см. <see cref="TilesScreen.LongPress"/>).
        /// </summary>
        public override void OnLongPress(MotionEvent e) => _screen.LongPress();

        public override bool OnSingleTapUp(MotionEvent e)
        {
            _screen.SingleTap(e.GetX(), e.GetY());
            return false;
        }
    }

    /// <summary>
    /// Перетаскивание плиток — все четыре направления: без горизонтали сетка не двигается
    /// (план 23 §3.3). Смахивания нет: убрать плитку жестом — не то, что делают случайно, для этого
    /// есть «убрать» в меню плитки.
    /// </summary>
    private sealed class DragCallback(TilesScreen screen) : ItemTouchHelper.SimpleCallback(
        ItemTouchHelper.Up | ItemTouchHelper.Down | ItemTouchHelper.Left | ItemTouchHelper.Right, 0)
    {
        /// <summary>Тащить можно только в режиме правки: иначе экран уезжал бы под пальцем у того, кто просто смотрит.</summary>
        public override bool IsLongPressDragEnabled => screen._editing;

        /// <summary>
        /// Насколько глубоко палец должен зайти на соседа, чтобы сетка переложилась. При половине —
        /// значении по умолчанию — она перетекает от каждого касания краёв, и предсказать, куда
        /// встанет плитка, нельзя: к отпусканию картина уже другая, чем была в начале.
        /// </summary>
        public override float GetMoveThreshold(RecyclerView.ViewHolder holder) => TilesLayout.DragMoveThreshold;

        public override bool OnMove(RecyclerView view, RecyclerView.ViewHolder holder, RecyclerView.ViewHolder target)
        {
            screen._adapter.Move(holder.BindingAdapterPosition, target.BindingAdapterPosition);
            return true;
        }

        public override void OnSwiped(RecyclerView.ViewHolder holder, int direction)
        {
        }
    }

    /// <summary>
    /// Плитки как список: адаптер отдаёт вид, а величину каждой берёт из каталога по имени. Плитка,
    /// сославшаяся на неизвестную величину, в список не попадает вовсе — то же правило, которым
    /// план 17 §4 требует отвергать скин со ссылкой на несуществующую величину.
    /// </summary>
    private sealed class TileAdapter : RecyclerView.Adapter
    {
        private readonly Context _context;
        private readonly DashboardOptions _options;
        private readonly Func<string, string> _translate;
        private readonly ITileLayoutStore? _layout;

        /// <summary>Точки отсчёта дистанций — общие на экран: плиток-дистанций может стоять несколько.</summary>
        private readonly TripPoints _trips;

        /// <summary>Адрес колеса, к которому относится счёт дистанций. Спрашивается на каждом показе: колесо меняется.</summary>
        private readonly Func<string> _wheel;

        /// <summary>Величина у пустого места пуста: <see cref="TileKind.Empty"/> ни на что не ссылается.</summary>
        private readonly List<(MetricTile Tile, MetricDescriptor? Metric)> _tiles = [];

        /// <summary>
        /// Все созданные плитки. <see cref="RecyclerView"/> их переиспользует, но не выбрасывает, а
        /// экран короткий — так кадр обходит готовый список вместо <c>NotifyItemChanged</c> по
        /// каждой плитке пять раз в секунду.
        /// </summary>
        private readonly List<TileView> _views = [];

        private TelemetrySnapshot? _snapshot;
        private bool _editing;

        /// <summary>
        /// Форма и кегль на каждый класс плиток (план плиток §2–3). Считаются один раз на раскладку,
        /// а не на плитку и не на кадр: класс — это заявление человека, что величины равны, и ответ
        /// у него один на всех.
        /// </summary>
        private IReadOnlyDictionary<TileClass, TileTypeface> _faces = new Dictionary<TileClass, TileTypeface>();

        private readonly PaintRuler _ruler;

        /// <summary>
        /// Сколько разрядов до точки величина показала <b>на самом деле</b>. Растёт и никогда не
        /// падает: кегль класса, севший под увиденное, не должен прыгать обратно от того, что
        /// одометр на секунду показал меньше.
        /// </summary>
        private readonly Dictionary<string, int> _digits = new(StringComparer.Ordinal);

        /// <summary>Подпись входов, под которую посчитаны нынешние кегли; и счётчики для замера.</summary>
        private int _measuredFor;
        private int _remeasureAsked;
        private int _remeasureDone;

        public TileAdapter(Context context, DashboardOptions options, Func<string, string> translate,
            ITileLayoutStore? layoutStore, IReadOnlyList<MetricTile> layout, TripPoints trips,
            Func<string> wheel)
        {
            _context = context;
            _options = options;
            _translate = translate;
            _layout = layoutStore;
            _trips = trips;
            _wheel = wheel;

            // Плитке без имени имя даётся здесь — и здесь же сохраняется. Рождать его при каждом
            // чтении и не записывать значило бы терять вместе с ним точку отсчёта дистанции: она
            // хранится по имени плитки, а не по её месту в списке.
            bool named = false;
            foreach (var tile in layout)
            {
                var known = tile.Id.Length > 0 ? tile : tile with { Id = MetricTile.NewId() };
                named |= known.Id != tile.Id;

                if (Entry(known) is { } entry) _tiles.Add(entry);
            }

            // Отсев неизвестных величин уходит и в хранимое: иначе позиция плитки на экране
            // разошлась бы с позицией в хранимом списке, и перенос двигал бы не ту. Но только когда
            // отсев что-то выбросил — писать настройку на каждом запуске незачем.
            if (named || _tiles.Count != layout.Count) Keep();

            _ruler = new PaintRuler(context.Resources!.DisplayMetrics!.Density);
            Remeasure();
        }

        /// <summary>
        /// Пересчитать кегли по нынешней раскладке. Зовётся, когда раскладка изменилась и когда
        /// вошли в правку: в правке место под крест, ручку и подпись размера вычитается из бюджета,
        /// а не отнимается отступом после подбора — иначе число печатается прямо по кресту.
        /// </summary>
        /// <summary>
        /// Пересчитать кегли — <b>только если входы действительно изменились</b>. Подпись входов
        /// (классы, разряды, режим правки, ширина колонки) считается по уже посчитанным числам, а
        /// не по форматированным строкам: строка зависит от культуры («56,0» против «56.0»), и
        /// сравнение строк однажды сделало бы этот пересчёт вечным.
        /// <para>
        /// Стража здесь не оптимизация, а страховка: пересчёт зовут семь мест, и любое из них,
        /// позванное в кадре, превращало подбор с мерилкой в горячий цикл на главном потоке —
        /// 109 % CPU и ANR на стенде 10.08.2026. Теперь лишний зов стоит обхода восемнадцати
        /// плиток без единого обращения к шрифту.
        /// </para>
        /// </summary>
        private void Remeasure()
        {
            _remeasureAsked++;

            int signature = Signature();
            if (signature == _measuredFor) return;

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var metrics = Metrics();
            _faces = TileTypography.Measure(Texts(metrics), metrics, _ruler, _editing);
            _measuredFor = signature;
            _remeasureDone++;

            double ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Android.Util.Log.Info("WheelTalk.Tiles",
                $"Remeasure #{_remeasureDone} за {ms:F1} мс, просили {_remeasureAsked} раз, плиток {_tiles.Count}");

            foreach (var view in _views) view.Invalidate();
        }

        /// <summary>
        /// Чем определяется набор кеглей: состав классов, увиденные разряды величин, режим правки и
        /// ширина колонки. Всё это числа — их и сравниваем.
        /// </summary>
        private int Signature()
        {
            var hash = new HashCode();
            hash.Add(_editing);
            hash.Add((int)CellWidth());

            foreach (var (tile, metric) in _tiles)
            {
                hash.Add(tile.Size.Columns);
                hash.Add(tile.Size.Rows);
                hash.Add(tile.Kind);
                hash.Add(metric?.Id);
                // Округление меняет ширину худшей строки — значит и кегль класса: без него правка
                // «показывать целыми» не доехала бы до подбора вовсе.
                hash.Add(tile.Decimals);
                if (metric is not null) hash.Add(_digits.GetValueOrDefault(metric.Id));
            }

            return hash.ToHashCode();
        }

        /// <summary>Все размеры сетки и полей — в одном месте, чтобы бюджет считался по ним, а не по памяти.</summary>
        private TileMetrics Metrics()
        {
            return new TileMetrics(
                CellWidthPx: CellWidth(),
                RowHeightPx: _context.Dp(TilesLayout.RowHeightDp),
                GapPx: _context.Dp(TilesLayout.GapDp),
                PaddingPx: _context.Dp(TilesLayout.PaddingDp),
                // Подпись меряет тот же стиль, что её и рисует: место считается от видимой кромки
                // буквы до низа краски, а не по кеглю с поправкой на глазок. Формула одна на все
                // формы — разнится только кегль подписи.
                LabelHeightPx: TileLabelStyle.StripPx(_context, TilesLayout.LabelSp),
                // Шкала жара переехала в саму рамку (решение владельца 10.08.2026) и своего места
                // у содержимого больше не занимает — из бюджета вычитается только толщина рамки,
                // чтобы число не садилось на её линию.
                HeatBarPx: _context.Dp(TilesLayout.HeatStrokeDp),
                EditReservePx: _context.Dp(TilesLayout.EditReserveDp),
                EditFooterPx: _context.Dp(TilesLayout.EditFooterDp),
                GapUnitPx: _context.Dp(TilesLayout.UnitGapDp),
                GapLabelPx: _context.Dp(TilesLayout.RowGapDp),
                MarkPx: _context.Dp(TilesLayout.MarkDp),
                ValueBleedPx: _context.Dp(TilesLayout.ValueBleedDp))
            {
                RowLabelSp = TilesLayout.RowLabelSp,
                MinValueSp = TilesLayout.ValueMinSp,
                MaxValueSp = TilesLayout.ValueMaxSp,
                UnitScale = TilesLayout.UnitScale,
                RowLabelShare = TilesLayout.RowLabelShare,
                SquareRatio = TilesLayout.SquareRatio,
                // Полоска подписи квадрата — той же формулой, только своим кеглем. Мера идёт чистой
                // плотностью, а не в sp: подпись рисуется в dp, и системный множитель шрифта на неё
                // не действует — резервируя sp, бюджет отнимал бы у числа то, чего подпись не
                // занимает.
                SquareLabelPx = TileLabelStyle.StripPx(_context, TilesLayout.SquareLabelSp),
            };
        }

        /// <summary>
        /// Ширина одной колонки сетки. Считается по экрану, а не по измеренному списку: кегли нужны
        /// до первой укладки, а список к этому времени ещё не мерян.
        /// </summary>
        private float CellWidth()
        {
            // Клетка — та же, что режет укладчик: ширина списка без его полей, делённая на колонки
            // (TileGridLayoutManager: usable / Columns). Своей арифметики здесь быть не должно —
            // разошедшись с укладчиком, бюджет считает плитку шире настоящей, и число вылезает за
            // края (регресс якоря запятой, 11.08.2026).
            float screen = _context.Resources!.DisplayMetrics!.WidthPixels;
            float padding = _context.Dp(ListPaddingDp) * 2;

            return Math.Max(1, (screen - padding) / TilesLayout.Columns);
        }

        /// <summary>
        /// Строки, по которым подбирается кегль: <b>худшая в классе</b> задаёт кегль всему классу.
        /// Берётся не живое показание, а самое длинное, какое эта величина способна показать
        /// (<see cref="MetricNumber.Widest"/>): иначе кегль скакал бы на ходу — «9.9» сменилось на
        /// «10.0», и весь класс перерисовался мельче.
        /// </summary>
        private IEnumerable<TileText> Texts(TileMetrics metrics)
        {
            foreach (var (tile, metric) in _tiles)
            {
                if (metric is null) continue;

                var shape = new TileClass(tile.Size.Columns, tile.Size.Rows);
                string unit = metric.UnitKey is { } key ? _translate(key) : "";

                // Породы считаются по-разному, и общей строки у них нет: квадрат садится под то,
                // что колесо показало на самом деле, прямоугольные — под принятые пять разрядов.
                // Один бюджет на двоих однажды уже сломал строки (решение владельца 10.08.2026).
                int digits = TileTypography.IsSquare(shape, metrics)
                    ? _digits.GetValueOrDefault(metric.Id)
                    : MetricNumber.RectangleDigits;

                yield return new TileText(
                    shape,
                    // Знаки после запятой — этой плитки, а не величины: округление задаётся плиткой,
                    // и мерить надо ту строку, которая на ней окажется. Класс, как и прежде, садится
                    // по худшей строке — плитка с сотыми опустит кегль соседке с целыми.
                    MetricNumber.Widest(MetricRounding.Decimals(metric, tile.Decimals), digits),
                    unit,
                    Label(tile, metric),
                    tile.Kind is TileKind.Extremum or TileKind.Trip);
            }
        }

        public override int ItemCount => _tiles.Count;

        /// <summary>Размер плитки для укладчика: сетке он нужен до того, как плитка построена.</summary>
        public TileSize SizeAt(int position) => _tiles[position].Tile.Size;

        /// <summary>Разделитель ли это: у него своя, пониженная строка (решение владельца 11.08.2026).</summary>
        public bool DividerAt(int position) => _tiles[position].Tile.Kind == TileKind.Divider;

        public MetricTile TileAt(int position) => _tiles[position].Tile;

        /// <summary>Режим правки одинаков для всех плиток сразу — правят раскладку, а не одну из них.</summary>
        public bool Editing
        {
            set
            {
                _editing = value;
                Remeasure();
                foreach (var view in _views) view.Editing = value;
                NotifyDataSetChanged();
            }
        }

        /// <summary>Перенести плитку. Порядок в списке и есть порядок на экране — другого хранения позиции нет.</summary>
        public void Move(int from, int to)
        {
            var moved = _tiles[from];
            _tiles.RemoveAt(from);
            _tiles.Insert(to, moved);

            NotifyItemMoved(from, to);
        }

        public void Add(MetricTile tile)
        {
            if (Entry(tile) is not { } entry) return;

            _tiles.Add(entry);
            Remeasure();
            NotifyItemInserted(_tiles.Count - 1);
        }

        public void Replace(int position, MetricTile tile)
        {
            if (Entry(tile) is not { } entry) return;

            _tiles[position] = entry;
            Remeasure();
            NotifyItemChanged(position);
        }

        public void RemoveAt(int position)
        {
            _tiles.RemoveAt(position);
            Remeasure();
            NotifyItemRemoved(position);
        }

        /// <summary>
        /// Подпись величины, а на четвертной плитке — короткая (план плиток §4): «Температура» в
        /// 61 px содержимого не влезает ни при каком кегле, а обрезанная многоточием — та же
        /// нечитаемость, только тихая.
        /// <para>
        /// Короткая живёт своим ключом рядом с полным — <c>…Short</c>. Нет её в ресурсах — остаётся
        /// полная: словарь слов у стенда свой, и заставлять его держать вторую половину имён ради
        /// формы плитки незачем.
        /// </para>
        /// </summary>
        private string Label(MetricTile tile, MetricDescriptor metric)
        {
            // Своя подпись старше всего: ею и различают две дистанции по одному одометру. Короткую
            // подмену она не терпит — человек написал ровно то, что хотел прочесть.
            if (tile.Caption.Length > 0) return tile.Caption;

            // У дистанции имя величины не годится вовсе: она считается из одометра, но одометр — не
            // то, что на ней написано. Пока хозяин не назвал её сам, плитка зовётся своим видом.
            string full = tile.Kind == TileKind.Trip ? _translate("TilesKindTrip") : _translate(metric.LabelKey);
            if (tile.Size.Columns > 3 || tile.Kind == TileKind.Trip) return full;

            string key = metric.LabelKey + "Short";
            string shortened = _translate(key);

            // «Слова нет» словари отвечают по-разному: приложение рисует «!Ключ!», стенд возвращает
            // сам ключ. Считаем пропажей оба ответа — иначе на четвертной плитке стенда стоял бы
            // сырой ключ вместо подписи (найдено владельцем на снимках 10.08.2026).
            return shortened.Length == 0 || shortened == key || shortened.StartsWith('!')
                ? full
                : shortened;
        }

        /// <summary>
        /// Набор для класса этой плитки. Класса нет в наборе только у только что добавленной плитки
        /// — тогда столбик и пол кегля: следующий пересчёт всё расставит.
        /// </summary>
        private TileTypeface Face(TileSize size) =>
            _faces.TryGetValue(new TileClass(size.Columns, size.Rows), out var face)
                ? face
                : new TileTypeface(TileForm.Stack, TilesLayout.ValueMinSp, TilesLayout.MinUnitSp);

        /// <summary>
        /// Ширина бокса числа в знаках — по <b>увиденному</b>, тому же счёту, которым садится кегль
        /// (<see cref="_digits"/>): число не вправе встать в бокс, под который кегль не считался.
        /// <para>
        /// Прямоугольная порода берёт для кегля пять разрядов про запас (<c>RectangleDigits</c>), а
        /// бокс считается по увиденному и здесь: запас — это место, оставленное на будущее, и
        /// равнять по нему значило бы сдвинуть число к правому краю на три пустых разряда у
        /// величины, которая пяти никогда не покажет. Кегль от этого не страдает — его бюджет шире.
        /// </para>
        /// </summary>
        private int BoxWidth(MetricTile tile, MetricDescriptor metric)
        {
            int decimals = MetricRounding.Decimals(metric, tile.Decimals);

            return MetricNumber.Widest(decimals, _digits.GetValueOrDefault(metric.Id)).Length;
        }

        /// <summary>Чем подписана плитка — тем же словом, что и на ней самой: им же зовётся её меню.</summary>
        public string LabelOf(MetricTile tile, MetricDescriptor? metric) =>
            metric is null ? tile.Caption : Label(tile, metric);

        /// <summary>
        /// Переименовать плитку. Правка идёт мимо режима правки — из меню действий, — поэтому
        /// сохраняется сразу: кнопки «сохранить» там нет, и другого случая записать не будет.
        /// </summary>
        public void Rename(int position, string caption)
        {
            var tile = _tiles[position].Tile;
            if (tile.Caption == caption) return;

            if (Entry(tile with { Caption = caption }) is not { } entry) return;

            _tiles[position] = entry;
            Remeasure();
            NotifyItemChanged(position);
            Keep();
        }

        /// <summary>Раскладка как она есть сейчас — её запоминают на входе в режим правки.</summary>
        public IReadOnlyList<MetricTile> Snapshot() => [.. _tiles.Select(entry => entry.Tile)];

        /// <summary>Вернуть раскладку из снимка — «отменить».</summary>
        public void Restore(IReadOnlyList<MetricTile> tiles)
        {
            _tiles.Clear();
            foreach (var tile in tiles)
            {
                if (Entry(tile) is { } entry) _tiles.Add(entry);
            }

            Remeasure();
            NotifyDataSetChanged();
        }

        /// <summary>Запомнить правку — «сохранить». Раскладка уходит в хранилище хозяина экрана.</summary>
        public void Keep() => _layout?.Save([.. _tiles.Select(entry => entry.Tile)]);

        /// <summary>
        /// Плитка со своей величиной. Пустое место величины не имеет вовсе, а плитка, сославшаяся на
        /// неизвестную, отвергается целиком — то же правило, которым план 17 §4 требует отвергать
        /// скин со ссылкой на несуществующую величину.
        /// </summary>
        private static (MetricTile Tile, MetricDescriptor? Metric)? Entry(MetricTile tile)
        {
            // Пустому месту и разделителю величина не положена по виду — без этой ветки Add тихо
            // глотал разделитель: Find("") давал null, и «нажал ОК — ничего не произошло»
            // (баг владельца 11.08.2026).
            if (tile.Kind is TileKind.Empty or TileKind.Divider) return (tile, null);

            return MetricCatalogue.Find(tile.MetricId) is { } metric ? (tile, metric) : null;
        }

        /// <summary>
        /// Вид рисовальщика и есть тип держателя: число и график — разные <c>View</c>, и переиспользовать
        /// одну под другую нельзя. Пустое место идёт как число: рамка у них общая, а содержимого у
        /// него нет вовсе.
        /// </summary>
        public override int GetItemViewType(int position) => _tiles[position].Tile.Kind switch
        {
            TileKind.Chart => 1,
            TileKind.Extremum => 2,
            TileKind.Trip => 3,
            TileKind.Divider => 4,
            _ => 0,
        };

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            TileView view = viewType switch
            {
                1 => new ChartTileView(_context, _options, _translate),
                2 => new ExtremumTileView(_context, _options),
                3 => new TripTileView(_context, _options, _wheel),
                4 => new DividerView(_context, _options),
                _ => new MetricTileView(_context, _options),
            };

            // Плитка могла родиться уже посреди правки: сетка создаёт держатели по мере надобности.
            view.Editing = _editing;

            // Просветы полями держит GridLayoutManager; свой укладчик отступает сам и эти поля
            // не читает — вреда от них там нет.
            view.LayoutParameters = new RecyclerView.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            {
                LeftMargin = _context.Dp(TilesLayout.GapDp),
                RightMargin = _context.Dp(TilesLayout.GapDp),
                TopMargin = _context.Dp(TilesLayout.GapDp),
                BottomMargin = _context.Dp(TilesLayout.GapDp),
            };

            _views.Add(view);
            return new TileHolder(view);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            if (holder is not TileHolder tile) return;

            var (layout, metric) = _tiles[position];

            if (tile.Tile is DividerView divider)
            {
                divider.Bind(layout.Size);
                return;
            }

            if (metric is null)
            {
                tile.Tile.BindEmpty(layout.Size);
                return;
            }

            string label = Label(layout, metric);
            string unit = metric.UnitKey is { } key ? _translate(key) : "";

            // Бокс числа спрашивается на каждом показе, а не берётся числом при привязке: он растёт
            // вслед за увиденным, и новый разряд обязан встать на место в тот же кадр.
            int Box() => BoxWidth(layout, metric);

            if (tile.Tile is TripTileView trip)
            {
                trip.Bind(metric, label, unit, layout.Size, layout.ShowLabel, layout.Limits,
                    Face(layout.Size), layout.ShowHeatBar, layout.Decimals, layout.Id, _trips, Box);
            }
            else if (tile.Tile is ChartTileView chart)
            {
                chart.Bind(metric, label, unit, layout.Size, layout.ShowLabel,
                    layout.Chart ?? new TileChart(TilesLayout.ChartWindows[0], ShowValue: true, Zoom: false),
                    layout.Limits, layout.ShowHeatBar, layout.Decimals, Box);
            }
            else if (tile.Tile is ExtremumTileView extremum)
            {
                extremum.Bind(metric, label, unit, layout.Size, layout.ShowLabel,
                    layout.Extremum ?? new TileExtremum(Lowest: false), layout.Limits, Face(layout.Size),
                    layout.ShowHeatBar, layout.Decimals, Box);
            }
            else if (tile.Tile is MetricTileView value)
            {
                value.Bind(metric, label, unit, layout.Size, layout.ShowLabel, layout.Limits, Face(layout.Size),
                    layout.ShowHeatBar, layout.Decimals, Box);
            }

            tile.Tile.Render(_snapshot);
        }

        /// <summary>
        /// Кадров шестьдесят в секунду, а снимков — пять: пока приходит тот же самый, считать нечего.
        /// Сравнение по ссылке, а не по значению, и этого достаточно — снимок неизменяем, и каждый
        /// новый отсчёт колеса приходит новым объектом.
        /// </summary>
        public void Render(TelemetrySnapshot? snapshot)
        {
            if (ReferenceEquals(_snapshot, snapshot)) return;

            _snapshot = snapshot;

            // Показание выросло в разрядах — бюджет ширины пересчитывается один раз и навсегда:
            // иначе длинное число обрезалось бы краем плитки (гейт «ничего не срезано»).
            bool grown = false;
            foreach (var (tile, metric) in _tiles)
            {
                if (metric is null || tile.Kind == TileKind.Empty) continue;

                int digits = MetricNumber.Digits(MetricNumber.Value(metric, snapshot));
                if (digits <= _digits.GetValueOrDefault(metric.Id)) continue;

                _digits[metric.Id] = digits;
                grown = true;
            }

            if (grown) Remeasure();

            foreach (var view in _views) view.Render(snapshot);
        }

        /// <summary>
        /// Крайние значения — с нуля: максимум прежнего колеса ничего не говорит о новом. По всем
        /// созданным вью, а не по видимым: укатившаяся за край плитка вернётся со старым числом,
        /// если забыть её здесь.
        /// </summary>
        public void ResetExtremeTiles()
        {
            foreach (var view in _views)
            {
                // Дистанции это не касается вовсе (решение владельца 10.08.2026): её точку не
                // двигает ничто, кроме руки хозяина, — вернулся к прежнему колесу, продолжил счёт.
                if (view is ExtremumTileView extremum) extremum.ResetValue();
            }
        }

        private sealed class TileHolder(TileView tile) : RecyclerView.ViewHolder(tile)
        {
            public TileView Tile { get; } = tile;
        }
    }
}
