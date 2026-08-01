using Android.Graphics;

namespace WheelTalk.Dashboard.Droid.Widgets;

/// <summary>Что панель говорит про связь с колесом. Решает вызывающий, панель только показывает.</summary>
public enum LinkPhase
{
    /// <summary>Связь есть, кадры идут. Плашки нет — на экране только приборы.</summary>
    Live,

    /// <summary>Идёт попытка подключения или переподключение после обрыва.</summary>
    Connecting,

    /// <summary>Отключено пользователем. Это покой, а не беда.</summary>
    Idle,

    /// <summary>Подключиться нечем: нет разрешений, выключен Bluetooth, колесо не найдено.</summary>
    Failed,

    /// <summary>Только что подключились. Показывается недолго и гаснет само.</summary>
    JustConnected,
}

/// <summary>
/// Верхняя надпись панели: плашка состояния связи, а когда связь в порядке — имя колеса на стоянке.
/// Одно место решает, что стоит наверху, потому что стоять там может только что-то одно.
/// <para>
/// Рисуется канвой, а не элементом разметки, и в этом весь смысл. Плашка появляется и исчезает по
/// ходу поездки; будь она соседом панели в разметке, каждый обрыв связи менял бы высоту панели — та
/// самая прыгающая разметка, которую прогон 3 запретил («разметка после сборки экрана не трогается
/// никогда»). Здесь она лежит поверх приборов, и под ней ничего не двигается.
/// </para>
/// <para>
/// Плашка может накрыть цифру скорости, и это разрешено (решение владельца): видна она только
/// тогда, когда данных нет, а без данных скорость на экране — прошлогодняя. Имя колеса, наоборот,
/// обязано уложиться выше цифры: оно показывается, когда всё хорошо.
/// </para>
/// </summary>
public sealed class LinkBadgeDrawable
{
    /// <summary>Сколько держится зелёная плашка, прежде чем уйти сама, секунд.</summary>
    private const double GreenSeconds = 2;

    /// <summary>
    /// Высота плашки в долях высоты панели, с полом и потолком в точках экрана. Считана на две
    /// строки: имя колеса и под ним состояние со временем — в одну строку они складывались в
    /// перечисление, где ни одно из трёх не главное.
    /// </summary>
    private const float HeightOfPanel = 0.085f;

    private const float MinHeight = 46f;
    private const float MaxHeight = 64f;

    /// <summary>
    /// Отступ от верхней кромки отданной области, точек экрана. Плашка — отдельная вещь на панели, а
    /// не продолжение системной строки: прижатая вплотную, она читается как её часть и спорит с
    /// часами за один и тот же край. Вызывающий отдаёт область уже ниже статус-бара
    /// (<c>DashboardView.TopInset</c>), а этот отступ отделяет плашку и от него.
    /// </summary>
    private const float TopMargin = 8f;

    private readonly Paint _plate = new() { AntiAlias = true };
    private readonly Paint _border = new() { AntiAlias = true };
    private readonly Paint _text = new() { AntiAlias = true };

    private LinkPhase _phase = LinkPhase.Live;
    private long _phaseSince;

    public LinkBadgeDrawable() => _border.SetStyle(Paint.Style.Stroke);

    public required DashboardOptions Options { get; init; }

    /// <summary>
    /// Состояние связи. Отсчёт «сколько уже так» ведётся от смены значения, а не от каждого
    /// присваивания: вызывающий выставляет фазу на каждом кадре, и без этой проверки зелёная плашка
    /// не ушла бы никогда.
    /// </summary>
    public LinkPhase Phase
    {
        get => _phase;
        set
        {
            if (_phase == value) return;
            _phase = value;
            _phaseSince = Java.Lang.JavaSystem.NanoTime();
        }
    }

    /// <summary>Имя колеса — то, которое дал ему хозяин, а не MAC. Пустое — не показывать.</summary>
    public string WheelName { get; set; } = "";

    /// <summary>Состояние словами («Подключение», «Отключено»). Слова у приложения и стенда свои.</summary>
    public string StateText { get; set; } = "";

    /// <summary>Сколько секунд нет данных. Ноль — не показывать: на стоянке счётчик ничего не значит.</summary>
    public int Seconds { get; set; }

    /// <summary>Текущая скорость: по ней имя колеса гаснет на ходу.</summary>
    public double SpeedKmh { get; set; }

    /// <summary>
    /// Попало ли касание в плашку. Проверка живёт рядом с рисованием по той же причине, что у точки
    /// записи: координаты плашки — здешние, и вторая их копия в экране разошлась бы с этой при
    /// первой же правке отступа.
    /// <para>
    /// Плашки нет — нет и цели: в <see cref="LinkPhase.Live"/> под этим местом обычные приборы, и
    /// нажатие там ничего значить не должно. Что делать с попаданием, решает вызывающий: панель
    /// говорит только «нажали на плашку».
    /// </para>
    /// </summary>
    public bool Hits(RectF rect, float density, float x, float y) =>
        Effective() != LinkPhase.Live && Plate(rect, density).Contains(x, y);

    /// <summary>Где стоит плашка. Одно место на отрисовку и на попадание.</summary>
    private static RectF Plate(RectF rect, float density)
    {
        float height = Math.Clamp(rect.Height() * HeightOfPanel, MinHeight * density, MaxHeight * density);
        float margin = 8 * density;
        float top = rect.Top + TopMargin * density;
        return new RectF(rect.Left + margin, top, rect.Right - margin, top + height);
    }

    public void Draw(Canvas canvas, RectF rect, float density)
    {
        var phase = Effective();
        if (phase == LinkPhase.Live)
        {
            DrawName(canvas, rect, density);
            return;
        }

        var palette = Options.Palette;
        var plate = Plate(rect, density);
        float height = plate.Height();

        _plate.Color = phase switch
        {
            LinkPhase.Connecting => palette.Caution,
            LinkPhase.Failed => palette.Danger,
            LinkPhase.JustConnected => palette.Good,
            _ => palette.Dim,
        };

        // Скругление со всех сторон: плашка стоит на панели отдельной вещью и ни к какому краю не
        // прижата, поэтому обрезать ей верх незачем.
        float radius = 6 * density;
        canvas.DrawRoundRect(plate, radius, radius, _plate);

        // Обводка — тем же приёмом, что у окон значений на лентах: без неё цветная плашка сливается
        // с цветными полосами шкал, которые начинаются в паре точек от её краёв, и читается как ещё
        // одна зона шкалы, а не как сообщение поверх приборов.
        _border.Color = Color.White;
        _border.StrokeWidth = 2 * density;
        canvas.DrawRoundRect(plate, radius, radius, _border);

        // Две строки: имя колеса и под ним состояние со временем. Имя крупнее — на него смотрят,
        // чтобы убедиться, что колесо своё; состояние мельче, но оно уже сказано цветом.
        float pad = height * 0.12f;
        float body = height - pad * 2;
        float nameRow = body * 0.55f;
        float stateRow = body - nameRow;

        _text.Color = Color.White;
        _text.SetTypeface(Typeface.DefaultBold);
        _text.TextSize = nameRow * 0.82f;
        canvas.DrawString(_text, WheelName, plate.Left, plate.Top + pad, plate.Width(), nameRow,
            HAlign.Center, VAlign.Center);

        _text.SetTypeface(Typeface.Default);
        _text.TextSize = stateRow * 0.82f;
        canvas.DrawString(_text, StateLine(), plate.Left, plate.Top + pad + nameRow, plate.Width(), stateRow,
            HAlign.Center, VAlign.Center);
    }

    /// <summary>
    /// Зелёная плашка уходит сама; остальные держатся, пока вызывающий не скажет иначе. Отмены по
    /// таймеру не нужно: если за эти две секунды связь упала, вызывающий выставит другую фазу, и
    /// отсчёт начнётся заново вместе с ней.
    /// </summary>
    private LinkPhase Effective()
    {
        if (_phase != LinkPhase.JustConnected) return _phase;

        double seconds = (Java.Lang.JavaSystem.NanoTime() - _phaseSince) / 1_000_000_000.0;
        return seconds > GreenSeconds ? LinkPhase.Live : LinkPhase.JustConnected;
    }

    /// <summary>Нижняя строка: состояние и сколько оно уже длится.</summary>
    private string StateLine() => Seconds > 0 ? $"{StateText} · {Seconds} с" : StateText;

    /// <summary>
    /// Имя колеса на стоянке. Оно нужно ровно затем, чтобы поймать подключение к чужому колесу, —
    /// то есть до того, как поехали. На ходу гаснет: постоянный текст поверх приборов был бы
    /// вернувшейся полосой состояния, которую отсюда и убрали.
    /// </summary>
    private void DrawName(Canvas canvas, RectF rect, float density)
    {
        if (WheelName.Length == 0) return;

        float alpha = NameAlpha();
        if (alpha <= 0) return;

        float height = Math.Clamp(rect.Height() * HeightOfPanel, MinHeight * density, MaxHeight * density);
        var palette = Options.Palette;

        _text.Color = Color.Argb((int)Math.Round(alpha * 255), palette.Dim.R, palette.Dim.G, palette.Dim.B);
        _text.TextSize = height * 0.38f;
        _text.SetTypeface(Typeface.Default);
        canvas.DrawString(_text, WheelName, rect.Left, rect.Top, rect.Width(), height,
            HAlign.Center, VAlign.Center);
    }

    /// <summary>
    /// Имя гаснет не порогом, а плавно: порог давал бы мигание на светофоре, где скорость ходит
    /// вокруг него. Полностью видно до 70 % порога, дальше линейно в ноль.
    /// </summary>
    private float NameAlpha()
    {
        double hide = Options.ShowNameBelow;
        if (hide <= 0) return 1;

        double from = hide * 0.7;
        if (SpeedKmh <= from) return 1;
        if (SpeedKmh >= hide) return 0;

        return (float)((hide - SpeedKmh) / (hide - from));
    }
}
