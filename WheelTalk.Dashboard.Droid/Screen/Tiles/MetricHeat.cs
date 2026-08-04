using Android.Graphics;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Насколько величина подошла к тревоге: 0 — спокойна, 1 — дошла до порога тревоги. Отсюда плитка
/// берёт цвет подложки, и только он: число остаётся <c>Ink</c> всегда.
/// <para>
/// <b>Красится подложка, а не число</b> — тем же порядком, которым на панели устроена верхняя
/// ступень ленты ШИМ (<c>Tapes.ApplyPwm</c>): «красная — красный фон под цифрами, на нём цифры
/// возвращаются к белому». Цветное число пришлось бы либо гасить до нечитаемого, либо брать в полную
/// насыщенность — а показание должно читаться одинаково при любом значении.
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
/// Греется только плитка значения. Почему плитка-график не греется — в <c>ChartTileView.Render</c>.
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

        if (limits is { } own)
        {
            return own.Rising
                ? Rising(number, own.Warn, own.Danger)
                : Falling(number, own.Warn, own.Danger);
        }

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

    /// <summary>Порог, по которому нечего рисовать, — не порог: нули и вывернутая пара отбрасываются.</summary>
    private static TileLimits? Sane(TileLimits limits) => limits switch
    {
        { Rising: true } when limits.Danger > limits.Warn => limits,
        { Rising: false } when limits.Warn > limits.Danger && limits.Danger > 0 => limits,
        _ => null,
    };

    /// <summary>Цвет подложки для этого жара: серый → предупреждение → тревога, без ступеней.</summary>
    public static Color Tint(double heat, DashboardPalette palette) => heat switch
    {
        <= 0 => palette.Dim,
        < 0.5 => Mix(palette.Dim, palette.Caution, heat * 2),
        _ => Mix(palette.Caution, palette.Danger, heat * 2 - 1),
    };

    /// <summary>
    /// Густота подложки для этого жара. Растёт вместе с цветом: одного поворота краски мало — тёплый
    /// тон при той же прозрачности отличается от серого слабее, чем нужно, чтобы плитку заметили,
    /// не разглядывая.
    /// </summary>
    public static int Alpha(double heat) => (int)Math.Round(
        TilesLayout.BackgroundAlpha + (TilesLayout.BackgroundHotAlpha - TilesLayout.BackgroundAlpha) * heat);

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
