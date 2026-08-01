using Android.Graphics;
using WheelTalk.Dashboard.Droid.Widgets.Tape;

namespace WheelTalk.Dashboard.Droid.Widgets;

/// <summary>
/// Ленточная шкала авиационного PFD: значение стоит неподвижно в окне, а мимо него ползёт
/// разметка. Цифра сообщает состояние, лента — производную: направление и темп её движения видно,
/// не читая. Это же и единственный элемент, который ловится периферией — сдвиг границы она
/// различает, цифру не может в принципе.
/// <para>
/// Сама по себе лента не рисует ничего: она собирает части (<see cref="Tape"/>) и задаёт
/// им общую систему координат. Части разделены потому, что уточняются они порознь и в каждой
/// накапливаются свои условия — сложенные в один файл, они превращаются в один длинный <c>Draw</c>,
/// где правка подписей задевает штриховку.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/TapeDrawable.cs</c>. Две содержательные правки, обе
/// в <see cref="Scroll"/>: время считается через <c>Java.Lang.JavaSystem.NanoTime()</c>, а не через
/// <c>Environment.TickCount64</c> (наносекунды точнее миллисекунд), и экспоненциальное сглаживание
/// хода заменено равномерным ходом к пришедшему значению — почему, написано у самого метода.
/// MAUI-пара заморожена (AGENTS.md), и обратно эта правка не переносится.
/// </para>
/// </summary>
public sealed class TapeDrawable
{
    public required DashboardOptions Options { get; init; }

    public TapeSide Side { get; init; } = TapeSide.Right;

    /// <summary>Значение под окном.</summary>
    public double Value { get; set; }

    /// <summary>Сколько dp приходится на единицу величины: задаёт и масштаб, и видимый диапазон.</summary>
    public double DpPerUnit { get; set; } = 12;

    /// <summary>
    /// Сколько единиц величины укладывается в высоту ленты целиком. Если задано — масштаб считается
    /// от неё, а <see cref="DpPerUnit"/> игнорируется.
    /// </summary>
    public double? SpanPerHeight { get; set; }

    /// <summary>Доля высоты, на которой стоит окно. Ровно середина экрана.</summary>
    public double WindowAt { get; set; } = 0.5;

    /// <summary>
    /// Нижняя граница времени, за которое лента проходит очередной шаг, секунд. Ноль отключает ход
    /// вовсе — шкала прыгает на новое значение, как только оно пришло. Сглаживается только ход
    /// шкалы; цифра в окне берёт сырое значение и потому не врёт.
    /// </summary>
    public double SmoothSeconds { get; set; } = 0.02;

    public string Caption { get; set; } = "";

    /// <summary>
    /// Пол и потолок измеренного интервала телеметрии. Пол — на случай, когда два кадра приходят
    /// подряд (BLE отдаёт накопившееся пачкой: в полевых записях между кадрами бывает 21 мс при
    /// обычных двухстах): считать по такой паре, что колесо теперь говорит 50 раз в секунду, значит
    /// снова начать прыгать. Потолок — чтобы ход после паузы в связи не растягивался на всю паузу.
    /// </summary>
    private const double MinSampleSeconds = 0.05;

    private const double MaxSampleSeconds = 0.3;

    private double _scroll;
    private double _target;
    private double _speed;
    private long _lastDraw;
    private long _lastSample;
    private bool _started;

    private readonly Paint _captionFill = new() { AntiAlias = true };
    private readonly Paint _captionText = new() { AntiAlias = true };

    public TapeScalePart Scale { get; } = new();
    public TapeTicksPart Ticks { get; } = new();
    public TapeWindowPart Window { get; } = new();
    public TapeTrendPart Trend { get; } = new();

    /// <summary>След в «плохую» сторону: максимум ШИМ, минимум напряжения.</summary>
    public TapeMarkPart Mark { get; } = new();

    /// <summary>След в «хорошую» сторону. Пока им пользуется только напряжение — максимум за поездку.</summary>
    public TapeMarkPart Peak { get; } = new();
    public TapeHatchPart Hatch { get; } = new();

    public void Draw(Canvas canvas, RectF rect, float density)
    {
        var palette = Options.Palette;
        double scale = SpanPerHeight is > 0 ? rect.Height() / SpanPerHeight.Value : DpPerUnit * density;
        var geometry = new TapeGeometry(rect, Side, Scroll(), scale, WindowAt, density);
        Window.Value = Value;

        canvas.Save();
        canvas.ClipRect(rect);

        Scale.Draw(canvas, geometry);
        Hatch.Draw(canvas, geometry, palette, density);
        Ticks.Draw(canvas, geometry, palette, density);
        Trend.Draw(canvas, geometry, density);
        Mark.Draw(canvas, geometry, density);
        Peak.Draw(canvas, geometry, density);

        canvas.Restore();

        // Окно рисуется без клипа и последним: оно неподвижно, лежит поверх шкалы и не должно
        // оказаться срезанным вместе с уехавшей разметкой.
        Window.Draw(canvas, geometry, palette, density);
        DrawCaption(canvas, geometry, palette, density);
    }

    /// <summary>
    /// Положение разметки: лента идёт к пришедшему значению равномерно и приходит к нему ровно к
    /// следующему кадру телеметрии, а не прыгает.
    /// <para>
    /// Экспоненциальный фильтр, стоявший здесь раньше, отрабатывал шаг за три кадра экрана и дальше
    /// стоял: колесо говорит пять раз в секунду (полевые записи Sherman L — 199 мс между кадрами,
    /// p99 257), между отсчётами ШИМ меняется в среднем на 0,5 %, а на разгоне на 3–7 %, и при
    /// 12 dp на процент это скачок на треть экрана каждые 200 мс. На стенде этого не видно — там
    /// сценарии рисуют ровные пандусы с шагом в сотые доли процента, — а живое колесо шумит, и
    /// лента дёргалась. Постоянная времени тут не спасает: любой фильтр «по остатку» проходит
    /// большую часть шага в первые кадры и оставляет ту же рванину, только помельче.
    /// </para>
    /// <para>
    /// Скорость хода берётся из интервала между приходами данных, измеренного здесь же: у стенда,
    /// который подаёт значение на каждом кадре, ход остаётся мгновенным сам собой, без отдельной
    /// настройки. Плата — задержка разметки на один интервал телеметрии; цифра в окне идёт по
    /// сырому значению и остаётся мгновенной, а лента показывает ход, а не отсчёт.
    /// </para>
    /// <para>
    /// Время считается по настоящим часам, а не по кадрам: кадры приходят неровно, и фильтр
    /// «по кадрам» на просадке частоты врал бы ровно тогда, когда картинка и так дёргается.
    /// </para>
    /// </summary>
    private double Scroll()
    {
        long now = Java.Lang.JavaSystem.NanoTime();
        double seconds = (now - _lastDraw) / 1_000_000_000.0;
        _lastDraw = now;

        if (!_started || SmoothSeconds <= 0 || seconds <= 0 || seconds > 1)
        {
            _started = true;
            _scroll = Value;
            _target = Value;
            _lastSample = now;
            _speed = 0;
            return _scroll;
        }

        if (Value != _target)
        {
            double sinceSample = (now - _lastSample) / 1_000_000_000.0;
            double travel = Math.Max(Math.Clamp(sinceSample, MinSampleSeconds, MaxSampleSeconds), SmoothSeconds);
            _speed = (Value - _scroll) / travel;
            _target = Value;
            _lastSample = now;
        }

        _scroll = _speed >= 0
            ? Math.Min(_scroll + _speed * seconds, _target)
            : Math.Max(_scroll + _speed * seconds, _target);
        return _scroll;
    }

    private void DrawCaption(Canvas canvas, in TapeGeometry geometry, DashboardPalette palette, float density)
    {
        if (Caption.Length == 0) return;

        var rect = geometry.Rect;

        // Подпись живёт в узкой ширине ленты, и крупным кеглем «73% −0,3В» туда не влезает бы
        // строкой без переносов. Подпись сдвинута к внутреннему краю и занимает ровно свою ширину,
        // а не всю полосу внизу: так она не отъедает у шкалы целую строку по вертикали. Плашка под
        // ней нужна потому, что цикл делений про подпись не знает и спокойно ставит своё число
        // прямо на неё.
        var area = geometry.LabelArea;
        float font = Math.Min(20 * density, rect.Width() * 0.2f);
        float height = font * 1.4f;

        _captionText.TextSize = font;
        float width = _captionText.MeasureText(Caption) + 8 * density;
        float left = Side == TapeSide.Right ? area.Left : area.Right - width;

        _captionFill.Color = palette.Background;
        canvas.DrawRect(left, rect.Bottom - height, left + width, rect.Bottom, _captionFill);

        _captionText.Color = palette.Dim;
        canvas.DrawString(_captionText, Caption, left, rect.Bottom - height, width, height, HAlign.Center, VAlign.Center);
    }
}
