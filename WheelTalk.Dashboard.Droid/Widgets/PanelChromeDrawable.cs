using Android.Graphics;

namespace WheelTalk.Dashboard.Droid.Widgets;

/// <summary>
/// Две метки состояния приложения на поле панели: точка записи и подсказка, что снизу есть шторка
/// быстрых команд. Обе живут в одном файле потому, что обе — не приборы: они ничего не измеряют и
/// ни от какой телеметрии не зависят, а рисуются здесь по той же причине, что и плашка связи, —
/// канвой, чтобы не стоить разметке ни одного dp.
/// <para>
/// Галочка появилась вместо постоянной полосы вызова шторки (48 dp внизу экрана, пункт 1 прогона 4):
/// сам вызов — жест от нижней кромки, и полоса платилась за то, чтобы про жест знали. Знак в нижнем
/// поле центральной колонки говорит то же самое и не занимает высоты.
/// </para>
/// <para>
/// Тап по галочке (<see cref="HitsSheetHint"/>) — второй вход к тому же вызову, что и жест: не
/// каждый нашаривает свайп с первого раза, а знак нарисован ровно затем, чтобы по нему целились.
/// Своя цель касания, а не общая со свайпом: тап и флик по одной и той же зоне уже разводит
/// <c>GestureDetector</c> (см. <c>MainActivity</c>), здесь остаётся не спорить с точкой записи.
/// </para>
/// </summary>
public sealed class PanelChromeDrawable
{
    /// <summary>
    /// Радиус точки записи и её отступ от угла — в точках экрана. Угол правый верхний, и не абы
    /// какой: <c>rect</c> сюда приходит уже без лент (<c>DashboardView.ChromeArea</c>), поэтому
    /// точка стоит рядом со шкалой, а не на её делениях.
    /// </summary>
    private const float DotRadius = 6f;

    private const float DotInset = 12f;

    /// <summary>
    /// Насколько точка опущена от верхнего края, точек экрана. Не по одному отступу с боковым: у
    /// самой кромки она теряется рядом с системной строкой, а плашка связи занимает как раз верхние
    /// полсотни точек. Ниже — видно и её, и то, что она не часть шкалы.
    /// </summary>
    private const float DotTop = 34f;

    /// <summary>Радиус цели касания вокруг точки записи — половина минимальной цели в 48 dp.</summary>
    private const float TouchRadius = 24f;

    /// <summary>Ширина, высота и отступ галочки от нижнего края — в точках экрана.</summary>
    private const float HintWidth = 26f;

    private const float HintHeight = 7f;
    private const float HintBottom = 12f;

    /// <summary>Непрозрачность галочки: подсказку видно, но за сигнал её не примешь.</summary>
    private const float HintAlpha = 0.35f;

    /// <summary>
    /// Цель касания вокруг галочки, точки экрана: чуть больше самого знака (26×7), но много меньше
    /// зоны свайпа шторки (нижние 128 dp, <c>SwipeUpFromEdgeListener</c>) — прицельная точка, а не
    /// вторая полоса поверх первой (владелец, план 23).
    /// </summary>
    private const float HintTouchWidth = 48f;

    private const float HintTouchHeight = 32f;

    /// <summary>
    /// Непрозрачность точки записи, когда запись не идёт: место известно, тревоги нет. Было 0,3 —
    /// на выезде 31.07.2026 точку не нашли вовсе; полупрозрачная она всё ещё не спорит с приборами,
    /// но перестаёт быть невидимой.
    /// </summary>
    private const float IdleDotAlpha = 0.5f;

    private readonly Paint _fill = new() { AntiAlias = true };
    private readonly Paint _stroke = new() { AntiAlias = true };

    public PanelChromeDrawable() => _stroke.SetStyle(Paint.Style.Stroke);

    public required DashboardOptions Options { get; init; }

    /// <summary>Идёт ли запись поездки. Знает вызывающий: панель про базу поездок ничего не знает.</summary>
    public bool Recording { get; set; }

    /// <summary>Рисовать ли точку записи. Метку включает тот, кто её показывает, — см. DashboardView.</summary>
    public bool ShowRecordDot { get; set; }

    /// <summary>Рисовать ли подсказку про шторку. Пока шторка открыта, подсказывать нечего.</summary>
    public bool ShowSheetHint { get; set; }

    /// <summary>
    /// Попало ли касание в точку записи. Проверка живёт рядом с рисованием, потому что координаты
    /// точки — здешние: вторая их копия в экране разошлась бы с этой при первой же правке отступа,
    /// и метка перестала бы нажиматься молча.
    /// <para>
    /// Цель касания намеренно много больше самой точки (радиус 5 dp — это украшение, а не кнопка):
    /// берётся <see cref="TouchRadius"/>, то есть привычные 24 dp вокруг центра.
    /// </para>
    /// </summary>
    public bool HitsRecordDot(RectF rect, float density, float x, float y)
    {
        if (!ShowRecordDot) return false;

        float dx = x - (rect.Right - DotInset * density);
        float dy = y - (rect.Top + DotTop * density);
        float reach = TouchRadius * density;
        return dx * dx + dy * dy <= reach * reach;
    }

    /// <summary>
    /// Попало ли касание в галочку — подсказку про шторку. Тот же приём, что у
    /// <see cref="HitsRecordDot"/>: координаты знака здешние, вторая их копия снаружи разошлась бы
    /// при первой же правке отступа. Область не пересекается с целью точки записи — та стоит у
    /// верхнего края (<see cref="DotTop"/>), эта — у нижнего.
    /// </summary>
    public bool HitsSheetHint(RectF rect, float density, float x, float y)
    {
        if (!ShowSheetHint) return false;

        float centreX = rect.CenterX();
        float centreY = rect.Bottom - (HintBottom + HintHeight / 2) * density;
        float halfWidth = HintTouchWidth / 2 * density;
        float halfHeight = HintTouchHeight / 2 * density;
        return x >= centreX - halfWidth && x <= centreX + halfWidth
            && y >= centreY - halfHeight && y <= centreY + halfHeight;
    }

    public void Draw(Canvas canvas, RectF rect, float density)
    {
        var palette = Options.Palette;

        if (ShowRecordDot)
        {
            _fill.Color = Recording
                ? palette.Danger
                : Color.Argb((int)Math.Round(IdleDotAlpha * 255), palette.Dim.R, palette.Dim.G, palette.Dim.B);
            canvas.DrawCircle(rect.Right - DotInset * density, rect.Top + DotTop * density,
                DotRadius * density, _fill);
        }

        if (!ShowSheetHint) return;

        // Галочка смотрит вверх — туда, откуда придёт шторка.
        float width = HintWidth * density;
        float height = HintHeight * density;
        float centre = rect.CenterX();
        float bottom = rect.Bottom - HintBottom * density;

        _stroke.Color = Color.Argb((int)Math.Round(HintAlpha * 255), palette.Ink.R, palette.Ink.G, palette.Ink.B);
        _stroke.StrokeWidth = 2 * density;
        _stroke.StrokeCap = Paint.Cap.Round;

        var path = new Android.Graphics.Path();
        path.MoveTo(centre - width / 2, bottom);
        path.LineTo(centre, bottom - height);
        path.LineTo(centre + width / 2, bottom);
        canvas.DrawPath(path, _stroke);
    }
}
