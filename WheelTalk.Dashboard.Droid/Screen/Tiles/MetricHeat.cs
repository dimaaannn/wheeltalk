using Android.Graphics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Насколько величина подошла к тревоге: 0 — спокойна, 1 — дошла до порога тревоги. Отсюда плитка
/// берёт цвет рамки, и только он: число остаётся <c>Ink</c> всегда.
/// <para>
/// <b>Красится рамка, а не число</b>: показание должно читаться одинаково при любом значении, а
/// цветное пришлось бы либо гасить до нечитаемого, либо брать в полную насыщенность. Рамкой, а не
/// заливкой всей плитки (решение владельца 05.08.2026), — залитая подложка ложилась на график и
/// спорила с его линией; рамка идёт по краю внутрь и содержимого не закрывает.
/// </para>
/// <para>
/// <b>Переход плавный, а не ступенями</b> (владелец 05.08.2026) — в отличие от
/// <see cref="DashboardPalette.ForPwm"/>, который отвечает на вопрос «в какой я зоне» и потому обязан
/// быть ступенчатым. Здесь вопрос другой: «насколько близко», и на него ступенька отвечает хуже
/// плавного разогрева. Пороги при этом те же самые, из настроек.
/// </para>
/// <para>
/// <b>У большинства величин порогов нет вовсе</b>, и это нормально: одометру и наклону не от чего
/// греться. Величина без порогов остаётся серой при любом значении.
/// </para>
/// <para>
/// Греются все плитки, где показано текущее значение, — и число, и график, и крайнее: молчащая
/// среди греющихся читалась бы как «здесь всё хорошо».
/// </para>
/// </summary>
internal static class MetricHeat
{
    /// <summary>
    /// Жар величины по порогам плитки, а при их отсутствии — по её же порогам из настроек. Растущие
    /// (ШИМ) греются вверх, падающие (напряжение) — вниз; ноль в пороге выключает разогрев, как и
    /// везде в настройках.
    /// <para>
    /// Свои пороги плитки старше настроечных: величин с порогами в настройках всего две, а греться
    /// человек может захотеть от чего угодно — от тока, от температуры двигателя, от заряда.
    /// </para>
    /// </summary>
    public static double Of(string metricId, double? value, DashboardOptions options, TileLimits? limits = null)
    {
        if (value is not { } number) return 0;

        if (limits is { } own) return Heat(number, own);

        return metricId switch
        {
            // Все три — скважность в процентах, и порог у них общий: тот, что задан в
            // «Предупреждениях». «ШИМ колеса» — то же самое число, но посчитанное самим колесом.
            "pwm" or "max_pwm" or "hw_pwm" =>
                Rising(number, options.Thresholds.WarnPwm, options.Thresholds.DangerPwm),

            // Пороги напряжения по умолчанию нулевые: угадать их не из чего, задаются в настройках
            // на каждое колесо (см. DashboardOptions.WarnVolts). Пока не заданы — плитка серая.
            "voltage" => Falling(number, options.WarnVolts, options.DangerVolts),

            _ => 0,
        };
    }

    /// <summary>
    /// Сами пороги — тем же правилом, каким считается жар: свои у плитки старше настроечных.
    /// Нужны там, где мало знать «насколько близко»: полноэкранный график проводит по ним черту, и
    /// ему требуются числа, а не доля.
    /// <para>
    /// <c>null</c> — порогов нет: у величины их не задали, либо задан ноль, что в настройках значит
    /// «не предупреждать».
    /// </para>
    /// </summary>
    public static TileLimits? Limits(string metricId, DashboardOptions options, TileLimits? limits)
    {
        if (limits is { } own) return Sane(own);

        return metricId switch
        {
            "pwm" or "max_pwm" or "hw_pwm" =>
                Sane(new TileLimits(options.Thresholds.WarnPwm, options.Thresholds.DangerPwm, Rising: true)),
            "voltage" => Sane(new TileLimits(options.WarnVolts, options.DangerVolts, Rising: false)),
            _ => null,
        };
    }

    /// <summary>
    /// Жар по меткам плитки. Две метки — шкала натянута между ними; одна — от нуля до уставки у
    /// растущей величины и от уставки до нуля у падающей (решение владельца 11.08.2026).
    /// <para>
    /// Одинокая метка даёт <b>полную</b> единицу жара на своём конце шкалы, а какой краской она
    /// горит, решает <see cref="Tint(double, DashboardPalette, TileLimits?)"/>: жёлтая метка не
    /// вправе покраснеть оттого, что красной рядом не поставили.
    /// </para>
    /// </summary>
    private static double Heat(double value, TileLimits limits)
    {
        if (limits is { Warn: { } warn, Danger: { } danger })
        {
            return limits.Rising ? Rising(value, warn, danger) : Falling(value, warn, danger);
        }

        // Одна метка. У растущей величины шкала идёт снизу вверх до уставки, у падающей — от
        // уставки вниз к нулю: и там, и там «пусто» на спокойном краю, «полно» на тревожном.
        if ((limits.Warn ?? limits.Danger) is not { } mark) return 0;

        return limits.Rising ? Alone(value, mark) : Alone(mark - value, mark);
    }

    /// <summary>Доля пути от нуля до одинокой уставки. Ноль и меньше — уставки нет, греться не от чего.</summary>
    private static double Alone(double passed, double mark) =>
        mark <= 0 ? 0 : Math.Clamp(passed / mark, 0, 1);

    /// <summary>
    /// Метка, по которой нечего рисовать, — не метка: ноль значит «не предупреждать», как и везде в
    /// настройках. Вывернутая <b>пара</b> отбрасывается целиком: у растущей величины красная ниже
    /// жёлтой — это не шкала, а опечатка.
    /// </summary>
    private static TileLimits? Sane(TileLimits limits)
    {
        var sane = limits with
        {
            Warn = limits.Warn is > 0 ? limits.Warn : null,
            Danger = limits.Danger is > 0 ? limits.Danger : null,
        };

        if (sane is { Warn: { } warn, Danger: { } danger }
            && (sane.Rising ? danger <= warn : warn <= danger))
        {
            return null;
        }

        return sane is { Warn: null, Danger: null } ? null : sane;
    }

    /// <summary>Цвет подложки для этого жара: серый → предупреждение → тревога, без ступеней.</summary>
    public static Color Tint(double heat, DashboardPalette palette) => heat switch
    {
        <= 0 => palette.Dim,
        < 0.5 => Mix(palette.Dim, palette.Caution, heat * 2),
        _ => Mix(palette.Caution, palette.Danger, heat * 2 - 1),
    };

    /// <summary>
    /// Цвет для этого жара с оглядкой на то, <b>какие метки стоят</b>. Две — привычный путь через
    /// жёлтое к красному. Одна — путь от серого прямо к её собственной краске: жёлтая метка не
    /// краснеет оттого, что красной рядом не поставили, а красная не обязана сперва желтеть
    /// (решение владельца 11.08.2026).
    /// </summary>
    public static Color Tint(double heat, DashboardPalette palette, TileLimits? limits) => limits switch
    {
        { Warn: not null, Danger: not null } or null => Tint(heat, palette),
        { Danger: not null } => Mix(palette.Dim, palette.Danger, Math.Clamp(heat, 0, 1)),
        _ => Mix(palette.Dim, palette.Caution, Math.Clamp(heat, 0, 1)),
    };

    /// <summary>
    /// Густота рамки для этого жара. Растёт вместе с цветом: у самого порога рамка едва проступает,
    /// у тревоги горит в полную силу — так видно не только «плохо», но и «насколько».
    /// </summary>
    public static int Alpha(double heat) => (int)Math.Round(
        TilesLayout.HeatStrokeMinAlpha + (255 - TilesLayout.HeatStrokeMinAlpha) * heat);

    /// <summary>Чем больше значения, тем хуже: ШИМ, ток, температура.</summary>
    private static double Rising(double value, double warn, double danger) =>
        danger <= warn ? 0 : Math.Clamp((value - warn) / (danger - warn), 0, 1);

    /// <summary>Чем меньше значения, тем хуже: напряжение, заряд.</summary>
    private static double Falling(double value, double warn, double danger) =>
        warn <= 0 || danger <= 0 || warn <= danger ? 0 : Math.Clamp((warn - value) / (warn - danger), 0, 1);

    /// <summary>
    /// Смесь двух красок. Открыта наружу, потому что тем же переходом красится линия графика
    /// (<c>ChartLine</c>) — только с другой базой: у подложки в покое серое, у линии — спокойная
    /// краска шкалы.
    /// </summary>
    internal static Color Mix(Color from, Color to, double part) => Color.Rgb(
        Channel(from.R, to.R, part), Channel(from.G, to.G, part), Channel(from.B, to.B, part));

    private static int Channel(int from, int to, double part) => (int)Math.Round(from + (to - from) * part);
}
