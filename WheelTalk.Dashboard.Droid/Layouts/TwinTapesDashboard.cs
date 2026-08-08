using Android.Content;
using Android.Graphics;
using Android.Views;
using WheelTalk.Dashboard.Droid.Widgets;
using WheelTalk.Dashboard.Droid.Widgets.Tape;

namespace WheelTalk.Dashboard.Droid.Layouts;

/// <summary>
/// Вариант A — две ленты в «авиа»-режиме: лента ШИМ справа, её же зеркало под напряжение слева,
/// крупная цифра скорости по центру и справочные значения над ней и под ней. Единственная
/// раскладка, которая стоит на главном экране приложения; остальные варианты каталога живут ради
/// сравнения на стенде.
/// <para>
/// В MAUI-версии это <c>ContentView</c> с тремя дочерними <c>GraphicsView</c> (по одному на ленту и
/// на центр) — так исторически вышло из-за того, что правка любого свойства разметки рядом с
/// панелью роняла перерисовку соседних канв (см. §3.3 описи). У обычного <see cref="View"/> в
/// Android нет системы разметки с общей инвалидацией между соседями — есть один
/// <c>View.Invalidate()</c> на весь холст, — поэтому здесь одна <see cref="View"/> рисует ленты и
/// центр в одном <c>OnDraw</c>, как и эталонный экран замера в
/// <c>WheelTalk.Native/Drawing/DashboardView.cs</c>. Части при этом остаются отдельными классами
/// (<see cref="TapeDrawable"/>, <see cref="SpeedBlockDrawable"/>) — по образцу MAUI-версии, а не
/// одним монолитом.
/// </para>
/// <para>
/// Фон, полосы тревоги и вуаль устаревших данных рисует <see cref="DashboardView"/>: они одинаковы
/// во всех вариантах и к этой раскладке отношения не имеют.
/// </para>
/// </summary>
public sealed class TwinTapesDashboard : DashboardView
{
    /// <summary>
    /// Доли ширины экрана: по ленте на край, остальное центру. Не точки, потому что экраны
    /// отличаются не только плотностью, но и пропорциями: раскладка, подогнанная под 360 dp, на
    /// 411 оставила бы ленты прежними, а весь выигрыш отдала бы пустоте в центре.
    /// <para>
    /// Величина переехала в <see cref="DashboardOptions.TapeShare"/> и стала ручкой стенда: ленты
    /// шире — замечание прогона 4, но платит за это центр, и сколько именно платит, видно только
    /// глазами.
    /// </para>
    /// </summary>
    private double TapeShare => Options.TapeShare;

    /// <summary>Длина затухания разметки у кромки, точки экрана. Сверху к ней добавляется высота статус-бара.</summary>
    private const float TickFadeDp = 20;

    private readonly TapeDrawable _voltage;

    /// <summary>
    /// Вторая левая лента — в вольтах на ячейку. Обе живут всегда, а рисуется одна: элементы
    /// разные, чтобы пороги пакета и пороги банки не делили одни поля (план 27 §27.4).
    /// </summary>
    private readonly TapeDrawable _cellVoltage;

    private readonly TapeDrawable _pwm;
    private readonly SpeedBlockDrawable _centre;

    /// <summary>
    /// Резерва под чужой хром сверху больше нет: панель занимает экран целиком, а плашка связи и имя
    /// колеса ложатся поверх приборов. Доля высоты (<c>DefaultTopBarShare</c> = 7 %) осталась от той
    /// поры, когда сверху стояла постоянная полоса состояния; приложение и так передавало ноль, и
    /// пустой пояс держался только на стенде — он и был виден на снимках как отступ ниоткуда.
    /// </summary>
    public TwinTapesDashboard(Context context, DashboardOptions options) : base(context, options)
    {
        _voltage = Tapes.Voltage(options, TapeSide.Left);
        _cellVoltage = Tapes.CellVoltage(options, TapeSide.Left);
        _pwm = Tapes.Pwm(options, TapeSide.Right);
        _centre = new SpeedBlockDrawable { Options = options };
    }

    /// <summary>
    /// Метки живут в центральной колонке, между лентами: у краёв экрана стоят цветные полосы шкал и
    /// их деления, и точка в углу оказалась бы на них.
    /// </summary>
    protected override RectF ChromeArea
    {
        get
        {
            float tape = (float)(Width * TapeShare / 100.0);
            var content = Content;
            return new RectF(tape, content.Top, Width - tape, content.Bottom);
        }
    }

    protected override void DrawPanel(Canvas canvas, RectF content)
    {
        float tape = (float)(Width * TapeShare / 100.0);

        // Слева стоит одна из двух лент. Считать вольт на ячейку бывает нечем — нет BMS, не задан
        // ряд, — и тогда возвращается пакетная: молча и мягко, это обычный день, а не ошибка.
        var left = Tapes.ShowsCellVoltage(Reading, Options) ? _cellVoltage : _voltage;
        if (left == _cellVoltage) Tapes.ApplyCellVoltage(_cellVoltage, Reading, Options);
        else Tapes.ApplyVoltage(_voltage, Reading, Options);

        Tapes.ApplyPwm(_pwm, Reading, Options);
        _centre.Reading = Reading;

        // Разметка проявляется от кромок: сверху с запасом на статус-бар, под который панель уходит,
        // снизу — только поле. Ставится здесь, а не в Tapes.Apply*, потому что высота бара — свойство
        // экрана, а не ленты: у стенда его нет вовсе.
        float fade = Density * TickFadeDp;
        foreach (var scale in (TapeDrawable[])[left, _pwm])
        {
            scale.Ticks.FadeTop = TopInset + fade;
            scale.Ticks.FadeBottom = fade;
        }

        left.Draw(canvas, new RectF(0, content.Top, tape, content.Bottom), Density);
        _pwm.Draw(canvas, new RectF(Width - tape, content.Top, Width, content.Bottom), Density);
        _centre.Draw(canvas, new RectF(tape, content.Top, Width - tape, content.Bottom));
    }
}
