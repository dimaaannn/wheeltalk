using Android.Graphics;
using WheelTalk.Dashboard.Droid;

namespace WheelTalk.Lab.Droid.Widgets;

/// <summary>
/// Полоса напряжения: не «сколько осталось», а «как просело». Цифр и делений на ней нет нарочно —
/// напряжение под нагрузкой скачет на несколько вольт, и читать эти скачки числом бессмысленно, а
/// вот видеть, как столбик приседает на разгоне и отпускает на выбеге, — ровно то, что нужно.
/// <para>
/// Ниже порога полоса краснеет целиком. Порог — не «мало заряда», а напряжение на банку, с
/// которого колесо начинает откидывать назад: просадка до него на полном пакете значит совсем не
/// то же, что тот же вольтаж в конце поездки, и различать эти два случая должен райдер, а не
/// индикатор.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/VoltageStripDrawable.cs</c>: все три константы на
/// банку и способ угадывать размер пакета перенесены без изменений.
/// </para>
/// </summary>
public sealed class VoltageStripDrawable
{
    private const double MaxCellVolts = 4.2;
    private const double MinCellVolts = 3.0;

    /// <summary>
    /// Напряжение на банку, ниже которого полоса желтеет. Своя константа, а не настройка: у
    /// варианта B полоса показывает «сколько осталось», и порог здесь абсолютный. Соседняя настройка
    /// шкалы (<see cref="DashboardOptions.SagWindowVolts"/>) говорит о другом — о видимом куске
    /// просадки от холостого хода, — и брать её сюда значило бы мерить одно другим.
    /// </summary>
    private const double WarnCellVolts = 3.5;

    private readonly Paint _fill = new() { AntiAlias = true };
    private readonly Paint _stroke = new() { AntiAlias = true };

    /// <summary>Наибольшее виденное напряжение — по нему определяется число банок.</summary>
    private double _peakVoltage;

    public VoltageStripDrawable() => _stroke.SetStyle(Paint.Style.Stroke);

    public required DashboardOptions Options { get; init; }

    public double VoltageV { get; set; }

    public void Draw(Canvas canvas, RectF rect, float density)
    {
        // Число банок в пакете нам никто не сообщает, а без него вольты не превратить в долю шкалы:
        // 77 В — это просадка у MTen3 и мёртвый пак у Sherman L. Считаем по максимуму, виденному за
        // сессию: пакет не бывает заряжен выше 4,2 В на банку, поэтому округление вверх даёт
        // ближайший разумный размер и само уточняется, если увидим напряжение выше.
        _peakVoltage = Math.Max(_peakVoltage, VoltageV);
        int cells = (int)Math.Ceiling(_peakVoltage / MaxCellVolts);
        if (cells <= 0) return;

        double top = cells * MaxCellVolts;
        double bottom = cells * MinCellVolts;
        double warn = cells * WarnCellVolts;

        var palette = Options.Palette;
        float Fill(double volts) => (float)Math.Clamp((volts - bottom) / (top - bottom), 0, 1);

        _fill.Color = Color.Argb(20, 255, 255, 255);
        canvas.DrawRect(rect, _fill);

        float height = rect.Height() * Fill(VoltageV);
        _fill.Color = VoltageV < warn ? palette.Danger : palette.Calm;
        canvas.DrawRect(rect.Left, rect.Bottom - height, rect.Right, rect.Bottom, _fill);

        float warnY = rect.Bottom - rect.Height() * Fill(warn);
        _stroke.Color = palette.Accent;
        _stroke.StrokeWidth = 2 * density;
        canvas.DrawLine(rect.Left, warnY, rect.Right, warnY, _stroke);
    }
}
