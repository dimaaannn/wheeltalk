using Android.Graphics;
using WheelTalk.Core.Battery;
using WheelTalk.Dashboard.Droid.Widgets;
using WheelTalk.Dashboard.Droid.Widgets.Tape;

namespace WheelTalk.Dashboard.Droid.Layouts;

/// <summary>
/// Настройка лент в одном месте: их показывают несколько вариантов, и лента, настроенная в каждом
/// по-своему, сравнение вариантов сделала бы бессмысленным. Здесь же держится обещание, ради
/// которого левую ленту и переделали в напряжение, — обе шкалы устроены одинаково и читаются как
/// одна система, а не как два разных прибора рядом.
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Layouts/Tapes.cs</c>: единственная правка —
/// <c>Colors.Black</c> заменён на <c>Android.Graphics.Color.Black</c>. Пороги, формулы и порядок
/// применения настроек — без изменений.
/// </para>
/// </summary>
public static class Tapes
{
    /// <summary>
    /// Шкала не имеет концов: деления идут дальше нуля и дальше двухсот, а крайние цвета тянутся
    /// за край экрана. Числа здесь — не границы диапазона, а запас, заведомо больший любого
    /// видимого куска ленты; настоящих границ у шкалы нет.
    /// </summary>
    private static class Endless
    {
        public const double Low = -1000;
        public const double High = 1000;
    }

    /// <summary>
    /// Во сколько раз видимый кусок шкалы напряжения шире размаха поездки. Ровно по размаху нельзя:
    /// оба следа прижались бы к самым краям, а метка, стоящая на границе экрана, не читается.
    /// </summary>
    private const double SwingMargin = 1.4;

    /// <summary>
    /// С какой просадки рисуется стрелка, вольт. Стрелка осталась после перехода на абсолютные
    /// пороги — она про просадку прямо сейчас, а не про разметку шкалы, и холостое напряжение ей
    /// нужно по существу. Раньше порог считался долей от «жёлтой просадки»; той настройки больше
    /// нет, и здесь своё число: четверть вольта — это уже разгон, а не дрожание измерения.
    /// </summary>
    private const double SagArrowMinV = 0.25;

    public static TapeDrawable Pwm(DashboardOptions options, TapeSide side = TapeSide.Right) => new()
    {
        Options = options,
        Side = side,
        DpPerUnit = 12,
        Caption = "ШИМ %",
    };

    public static TapeDrawable Voltage(DashboardOptions options, TapeSide side = TapeSide.Left) => new()
    {
        Options = options,
        Side = side,
        Caption = "В",
    };

    /// <summary>
    /// Вторая лента напряжения — в вольтах на ячейку (план 27 §27.4). Отдельный элемент, а не режим
    /// первой: пороги пакета и пороги банки не должны лежать в одних полях, и лента, меняющая смысл
    /// своих чисел, — это ошибка привязки, ждущая своего часа.
    /// <para>
    /// <b>Копия рядом стоящей <see cref="Voltage"/>, и правки между ними сами не переносятся.</b>
    /// Так задумано: шкала на ячейку вправе разойтись с пакетной и в разметке, и в окне, и общего
    /// помощника, обещающего их одинаковость, никто не обещал.
    /// </para>
    /// </summary>
    public static TapeDrawable CellVoltage(DashboardOptions options, TapeSide side = TapeSide.Left) => new()
    {
        Options = options,
        Side = side,
        Caption = "В/яч",
    };

    /// <summary>
    /// Лента скорости осталась только у варианта D. Решено, что скорость показывается цифрой по
    /// центру, но сам вариант живёт до конца сравнения — иначе сравнивать будет не с чем.
    /// </summary>
    public static TapeDrawable Speed(DashboardOptions options, TapeSide side = TapeSide.Left) => new()
    {
        Options = options,
        Side = side,
        DpPerUnit = 12,
        Caption = "км/ч",
    };

    public static void ApplySpeed(TapeDrawable tape, DashboardReading reading, DashboardOptions options)
    {
        tape.Value = reading.SpeedKmh;
        tape.Ticks.Step = 5;
        tape.Ticks.LabelStep = 10;
        tape.Ticks.From = 0;
        tape.Window.Format = options.HideTenthsAbove > 0 && reading.SpeedKmh >= options.HideTenthsAbove ? "F0" : "F1";
        tape.Window.Fill = Color.Black;
        tape.Window.Ink = options.Palette.Ink;
    }

    public static void ApplyPwm(TapeDrawable tape, DashboardReading reading, DashboardOptions options)
    {
        var palette = options.Palette;

        tape.Value = reading.Pwm;
        tape.DpPerUnit = options.PwmDpPerUnit;
        tape.SmoothSeconds = options.TapeSmoothSeconds;
        tape.Ticks.Step = 2.5;
        tape.Ticks.LabelStep = 5;
        tape.Ticks.LabelFrom = 0;

        // Шкала уходит в бесконечность в обе стороны: деления не обрываются ни на нуле, ни на
        // двухстах. Крайние цвета тянутся туда же — обрыв заливки читался бы как конец шкалы,
        // то есть ровно как та граница, которой решено не делать.
        tape.Scale.Bands =
        [
            new TapeBand(Endless.Low, options.PwmGreyBelow, palette.Dim),
            new TapeBand(options.PwmGreyBelow, options.Thresholds.WarnPwm, palette.Calm),
            new TapeBand(options.Thresholds.WarnPwm, options.Thresholds.DangerPwm, palette.Caution),
            new TapeBand(options.Thresholds.DangerPwm, Endless.High, palette.Danger),
        ];

        tape.Hatch.From = options.ShowBarberPole ? options.BarberPolePwm : null;
        tape.Hatch.To = null;

        // След — максимум за поездку: он всегда выше указателя, поэтому за место с ним не спорит.
        tape.Mark.Value = options.ShowBug ? reading.MaxPwm : null;
        tape.Mark.Color = palette.Accent;

        tape.Trend.From = null;
        tape.Trend.To = options.ShowTrend ? reading.PwmIn(options.TrendSeconds) : null;
        tape.Trend.Color = palette.Accent;

        // Три ступени, каждая добавляет сигнал к предыдущей: жёлтая зона — жёлтые рамка и цифры,
        // красная — красный фон под ними, за штриховкой — этот фон мигает. На красном фоне цифры
        // возвращаются к белому: жёлтое по киновари даёт 2,9:1, то есть хуже всего читается ровно
        // там, где читать важнее всего, а сигнал к этому моменту уже несёт сама заливка.
        bool warn = reading.Pwm >= options.Thresholds.WarnPwm;
        bool danger = reading.Pwm >= options.Thresholds.DangerPwm;

        tape.Window.Format = "F0";
        tape.Window.Text = null;
        tape.Window.Fill = danger ? palette.Danger : Color.Black;
        tape.Window.Critical = reading.Pwm >= options.BarberPolePwm;
        tape.Window.Ink = danger ? Color.White : warn ? palette.Accent : palette.Ink;
        tape.Window.Border = tape.Window.Ink;
    }

    /// <summary>
    /// Лента напряжения — зеркало ленты ШИМ, но читается наоборот: у ШИМ плохо наверху, у
    /// напряжения — внизу. Поэтому и след поездки здесь минимум, а не максимум, и стрелка идёт не
    /// вперёд во времени, а от холостого напряжения к текущему: её длина — просадка прямо сейчас.
    /// <para>
    /// Рядом живёт <see cref="ApplyCellVoltage"/> — та же лента в вольтах на ячейку. <b>Правки
    /// отсюда туда сами не переносятся</b>, и это осознанно (план 27 §27.4).
    /// </para>
    /// </summary>
    public static void ApplyVoltage(TapeDrawable tape, DashboardReading reading, DashboardOptions options)
    {
        // Ниже нуля подписей нет — так же, как у ШИМ. Отрицательный ШИМ хотя бы существует
        // (рекуперация), а отрицательного напряжения не бывает вовсе: у нуля в окне шкала
        // подписывала «−10» и «−20», предлагая читать вольты, которых не бывает. Деления остаются:
        // они показывают, что шкала не кончилась. Стоит до развилки, потому что в состоянии «данных
        // нет» ноль в окне как раз и стоит — там эти подписи и увидели.
        tape.Ticks.LabelFrom = 0;

        if (reading.VoltageV <= 0)
        {
            // Колесо ещё ничего не сказало. Молча выйти нельзя: ранний return замораживал ленту на
            // последнем значении, и после «Стоп» окно продолжало показывать 77,9 В как живые
            // (защёлка унаследована из MAUI-версии вместе с дефектом). Сброс возвращает ленту в то
            // же состояние, что при старте приложения, — серая шкала и ноль в окне.
            tape.Value = 0;

            // Не пустая шкала, а серая — как лента ШИМ у нуля. Пустой её оставлять нельзя: без
            // заливки лента исчезает целиком, остаются висеть деления и подписи, и это читается не
            // как «данных нет», а как поломка панели. Один цвет на всю высоту: пороги-то заданы, но
            // раскрашивать по ним нечего, пока нет ни одного напряжения.
            tape.Scale.Bands = [new TapeBand(Endless.Low, Endless.High, options.Palette.Dim)];
            tape.Mark.Value = null;
            tape.Peak.Value = null;
            tape.Trend.From = null;
            tape.Trend.To = null;
            return;
        }

        var palette = options.Palette;

        // Пороги абсолютные: «жёлтая ниже стольких-то вольт», а не «просадка на столько-то от
        // холостого». Решение владельца 01.08.2026 — от холостого напряжения на шкале отказались.
        // Оно переопределялось на каждом выбеге, и зоны ехали по шкале вслед за садящимся паком;
        // разметка, которая ползает, обесценивает шкалу так же, как плавающий масштаб.
        //
        // Ноль выключает зону. Умолчание — ноль у обеих: абсолютный порог зависит от пакета
        // целиком, угадать его не из чего, а угаданный неверно закрасил бы ленту на всю поездку.
        // Задаются в настройках, страница «Отображение», у каждого колеса свой слой.
        double warn = options.WarnVolts;
        double danger = options.DangerVolts;
        double empty = options.EmptyVolts;

        // Масштаб постоянный, если не попросили обратного: прибор с плавающей шкалой перестаёт быть
        // сравнимым сам с собой — одно и то же напряжение в начале и в конце поездки стоит на
        // разной высоте. Растяжение под размах поездки есть, но это настройка, а не поведение.
        double span = options.SagWindowVolts;
        if (options.SagAutoScale)
        {
            span = Math.Max(span, (reading.MaxVoltageV - reading.MinVoltageV) * SwingMargin);
        }
        tape.SpanPerHeight = span;
        tape.SmoothSeconds = options.TapeSmoothSeconds;

        // Шаг подписей выбирается под масштаб так, чтобы их было примерно столько же, сколько на
        // ленте ШИМ, — иначе две шкалы рядом читаются как приборы разной точности. Риски всегда
        // вдвое чаще подписей, тот же ритм, что у ШИМ (2,5 при подписях через 5).
        double labelStep = span <= 12 ? 1 : span <= 24 ? 2 : span <= 60 ? 5 : 10;
        tape.Ticks.LabelStep = labelStep;
        tape.Ticks.Step = labelStep / 2;
        tape.Ticks.LabelFormat = "F0";

        tape.Value = reading.VoltageV;

        // Слоями снизу вверх: спокойная заливка на всю шкалу, поверх — жёлтая ниже своего порога,
        // поверх неё красная ниже своего, и последним абсолютный пол. Выключенный порог (ноль)
        // просто не кладёт свой слой — тогда лента остаётся спокойной, и это честно: раскрашивать
        // её не по чему.
        var bands = new List<TapeBand> { new(Endless.Low, Endless.High, palette.Calm) };
        if (warn > 0) bands.Add(new TapeBand(Endless.Low, warn, palette.Caution));
        if (danger > 0) bands.Add(new TapeBand(Endless.Low, danger, palette.Danger));
        if (empty > 0) bands.Add(new TapeBand(Endless.Low, empty, palette.Danger));
        tape.Scale.Bands = bands;

        tape.Hatch.From = null;

        // Два следа: снизу самая тяжёлая просадка, сверху напряжение, с которого поездка началась.
        // Вместе они и есть тот размах, по которому шкала выбрала масштаб, — то есть шкала всегда
        // показывает оба, и между ними видно, где ты сейчас.
        tape.Mark.Value = options.ShowBug && reading.MinVoltageV > 0 ? reading.MinVoltageV : null;
        tape.Mark.Color = palette.Accent;
        tape.Peak.Value = options.ShowBug && reading.MaxVoltageV > 0 ? reading.MaxVoltageV : null;
        tape.Peak.Color = palette.Good;

        // Просадка рисуется только когда она есть: стрелка в пол-пикселя на стоянке — это шум.
        // Порог — сотая доля порога «жёлтой»: меньше уже дрожание измерения, больше — уже
        // пропущенный разгон. Считается от него, а не своим числом, чтобы не заводить ещё одну
        // константу в вольтах, которую пришлось бы держать в согласии с размером пакета.
        double sag = reading.NoLoadVoltageV - reading.VoltageV;
        bool sagging = options.ShowTrend && reading.NoLoadVoltageV > 0 && sag > SagArrowMinV;
        tape.Trend.From = sagging ? reading.NoLoadVoltageV : null;
        tape.Trend.To = sagging ? reading.VoltageV : null;
        tape.Trend.Color = palette.Accent;

        // Две последние цифры и десятая: просадка живёт в десятых вольта, а сотни на трёхзначном
        // паке не меняются вовсе и видны по шкале. «147,5» пятью знаками ужало бы кегль сильнее,
        // чем того стоит старший разряд.
        tape.Window.Text = volts => (Math.Abs(volts) % 100).ToString("00.0");

        bool low = reading.VoltageV <= warn;
        bool sunk = reading.VoltageV <= danger;

        tape.Window.Fill = sunk ? palette.Danger : Color.Black;
        tape.Window.Critical = empty > 0 && reading.VoltageV <= empty;
        tape.Window.Ink = sunk ? Color.White : low ? palette.Accent : palette.Ink;
        tape.Window.Border = tape.Window.Ink;

        // Подпись снова статическая. Динамическая («72% −0,1В») была третьим рендером одной и той
        // же просадки — её уже рисует стрелка и показывает центральный индикатор, — и рисовалась
        // кеглем 10 угловых минут с руля: место занимала, взгляд тянула, прочитаться не могла.
        tape.Caption = "В";
    }

    /// <summary>
    /// Есть ли сейчас чем считать вольт на ячейку. Нет — показывается пакетная лента: не выбран
    /// режим, молчит BMS, не задан ряд. Это не ошибка, а обычный день у колеса без BMS.
    /// </summary>
    public static bool ShowsCellVoltage(DashboardReading reading, DashboardOptions options) =>
        CellDivisor(reading, options) > 1;

    private static double CellDivisor(DashboardReading reading, DashboardOptions options) =>
        VoltageScale.Divisor(options.VoltageScale, reading.VoltageV, reading.PackCells);

    /// <summary>
    /// Та же лента в вольтах на ячейку — <b>полная копия <see cref="ApplyVoltage"/></b> со своими
    /// порогами, своим окном и своим форматом окна. Копия нарочно: общий помощник был бы обещанием,
    /// что две шкалы останутся одинаковыми, а такого обещания никто не давал — ячейковая вправе
    /// разойтись с пакетной и в разметке, и в рисках (план 27 §27.4).
    /// <para>
    /// <b>Правки отсюда в <see cref="ApplyVoltage"/> сами не переносятся.</b> Чиня здесь просадку,
    /// загляни туда — и наоборот.
    /// </para>
    /// </summary>
    public static void ApplyCellVoltage(TapeDrawable tape, DashboardReading reading, DashboardOptions options)
    {
        tape.Ticks.LabelFrom = 0;

        // Делитель нужен и до развилки: без него «данных нет» — это и молчащее колесо, и молчащий
        // BMS. Показывается в обоих случаях одно и то же, но лента к этому моменту уже выбрана.
        double divisor = CellDivisor(reading, options);

        if (reading.VoltageV <= 0 || divisor <= 1)
        {
            // То же, что у пакетной: не пустая шкала, а серая, и ноль в окне. Пустая читается не
            // как «данных нет», а как поломка панели.
            tape.Value = 0;
            tape.Scale.Bands = [new TapeBand(Endless.Low, Endless.High, options.Palette.Dim)];
            tape.Mark.Value = null;
            tape.Peak.Value = null;
            tape.Trend.From = null;
            tape.Trend.To = null;
            return;
        }

        var palette = options.Palette;

        // Пороги на ячейку предзаполнены — в отличие от пакетных, у которых умолчание ноль: 3,5 В
        // на банке значат одно и то же и на 20S, и на 60S, и угадывать тут нечего. Ноль по-прежнему
        // выключает зону.
        double warn = options.WarnCellVolts;
        double danger = options.DangerCellVolts;
        double empty = options.EmptyCellVolts;

        // Своё окно, а не пересчитанное из пакетного: у банки размах свой — доли вольта.
        double span = options.SagWindowCellVolts;
        if (options.SagAutoScale)
        {
            span = Math.Max(span, (reading.MaxVoltageV - reading.MinVoltageV) / divisor * SwingMargin);
        }
        tape.SpanPerHeight = span;
        tape.SmoothSeconds = options.TapeSmoothSeconds;

        // Своя лестница подписей: на банке весь видимый кусок — около половины вольта, и шаг в
        // целый вольт оставил бы шкалу без разметки вовсе.
        double labelStep = span <= 0.3 ? 0.05 : span <= 0.6 ? 0.1 : span <= 1.5 ? 0.25 : 0.5;
        tape.Ticks.LabelStep = labelStep;
        tape.Ticks.Step = labelStep / 2;
        tape.Ticks.LabelFormat = labelStep >= 0.5 ? "F1" : "F2";

        double cellVolts = reading.VoltageV / divisor;
        tape.Value = cellVolts;

        // Слоями снизу вверх, как у пакетной: спокойная заливка на всю шкалу, поверх жёлтая, поверх
        // красная, последним пол. Выключенный порог свой слой не кладёт.
        var bands = new List<TapeBand> { new(Endless.Low, Endless.High, palette.Calm) };
        if (warn > 0) bands.Add(new TapeBand(Endless.Low, warn, palette.Caution));
        if (danger > 0) bands.Add(new TapeBand(Endless.Low, danger, palette.Danger));
        if (empty > 0) bands.Add(new TapeBand(Endless.Low, empty, palette.Danger));
        tape.Scale.Bands = bands;

        tape.Hatch.From = null;

        // Следы поездки — те же два, поделённые тем же делителем: они про тот же пакет, только в
        // других единицах.
        tape.Mark.Value = options.ShowBug && reading.MinVoltageV > 0 ? reading.MinVoltageV / divisor : null;
        tape.Mark.Color = palette.Accent;
        tape.Peak.Value = options.ShowBug && reading.MaxVoltageV > 0 ? reading.MaxVoltageV / divisor : null;
        tape.Peak.Color = palette.Good;

        // Порог стрелки — в вольтах пакета, как и у пакетной ленты: просадка одна и та же, и
        // рисоваться стрелка обязана в тех же случаях, а не раньше или позже.
        double sag = reading.NoLoadVoltageV - reading.VoltageV;
        bool sagging = options.ShowTrend && reading.NoLoadVoltageV > 0 && sag > SagArrowMinV;
        tape.Trend.From = sagging ? reading.NoLoadVoltageV / divisor : null;
        tape.Trend.To = sagging ? cellVolts : null;
        tape.Trend.Color = palette.Accent;

        // Три значащие цифры: у банки старших разрядов нет вовсе, прятать нечего, а сотые — это как
        // раз та точность, на которой видно просадку.
        tape.Window.Text = volts => volts.ToString("F2");

        bool low = cellVolts <= warn;
        bool sunk = cellVolts <= danger;

        tape.Window.Fill = sunk ? palette.Danger : Color.Black;
        tape.Window.Critical = empty > 0 && cellVolts <= empty;
        tape.Window.Ink = sunk ? Color.White : low ? palette.Accent : palette.Ink;
        tape.Window.Border = tape.Window.Ink;

        tape.Caption = "В/яч";
    }
}
