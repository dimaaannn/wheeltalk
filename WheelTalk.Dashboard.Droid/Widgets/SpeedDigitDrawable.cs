using Android.Graphics;

namespace WheelTalk.Dashboard.Droid.Widgets;

/// <summary>
/// Крупная цифра скорости с подписью. Отдельной частью, потому что её показывают почти все
/// варианты, и правило «выше порога скрывать десятые, освободившееся место отдать кеглю» должно
/// работать во всех одинаково — иначе сравнивать варианты будет нечестно.
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Layouts/SpeedDigit.cs</c>. Там это был контрол из двух
/// <c>Label</c>, здесь — часть канвы: <see cref="Draw"/> ставит цифру и подпись по центру
/// отведённого прямоугольника. Костыли MAUI-версии (жёсткие <c>WidthRequest</c>/<c>HeightRequest</c>,
/// <c>LineBreakMode.NoWrap</c>) не перенесены — они там стояли против перекладки разметки при
/// смене числа знаков, а на канве разметки нет вовсе. Вместо них кегль ужимается под ширину так же,
/// как в <see cref="SpeedBlockDrawable"/>: «24,5» и «147» разной длины, а место одно и то же.
/// </para>
/// </summary>
public sealed class SpeedDigitDrawable
{
    /// <summary>Кегль подписи «км/ч» — как в MAUI-исходнике.</summary>
    private const float UnitFontSize = 16;

    private readonly Paint _bold = new() { AntiAlias = true };
    private readonly Paint _unit = new() { AntiAlias = true };

    public SpeedDigitDrawable(double baseFontSize, bool showUnit = true)
    {
        BaseFontSize = baseFontSize;
        ShowUnit = showUnit;
        _bold.SetTypeface(Typeface.Create(Typeface.Monospace, TypefaceStyle.Bold));
        _unit.SetTypeface(Typeface.Default);
    }

    public required DashboardOptions Options { get; init; }

    public double BaseFontSize { get; }

    public bool ShowUnit { get; }

    /// <summary>Во что упирается рост кегля, когда десятые скрыты. Ноль — не расти.</summary>
    public double GrownFontSize { get; set; }

    /// <summary>Кегль, заданный раскладкой вместо своего: вариант E ужимает цифру, когда растёт дуга.</summary>
    public double? ForcedFontSize { get; set; }

    public void Draw(Canvas canvas, RectF rect, DashboardReading reading, float density)
    {
        var palette = Options.Palette;
        bool tenthsHidden = Options.HideTenthsAbove > 0 && reading.SpeedKmh >= Options.HideTenthsAbove;

        double requested = ForcedFontSize
            ?? (tenthsHidden && GrownFontSize > 0 ? GrownFontSize : BaseFontSize);

        string text = Options.FormatSpeed(reading.SpeedKmh);
        float font = Fit(text, rect.Width(), (float)requested * density);
        float unitFont = UnitFontSize * density;

        float block = font * 1.05f + (ShowUnit ? unitFont * 1.6f : 0);
        float top = rect.CenterY() - block / 2;

        _bold.Color = palette.Ink;
        _bold.TextSize = font;
        canvas.DrawString(_bold, text, rect.Left, top, rect.Width(), font * 1.05f, HAlign.Center, VAlign.Center);

        if (!ShowUnit) return;

        _unit.Color = WithAlpha(palette.Ink, 0.7f);
        _unit.TextSize = unitFont;
        canvas.DrawString(_unit, "км/ч", rect.Left, top + font * 1.05f, rect.Width(), unitFont * 1.6f,
            HAlign.Center, VAlign.Center);
    }

    private float Fit(string text, float available, float font)
    {
        _bold.TextSize = font;
        float width = _bold.MeasureText(text);
        return width <= available ? font : font * available / width;
    }

    private static Color WithAlpha(Color color, float alpha) =>
        Color.Argb((int)Math.Round(alpha * 255), color.R, color.G, color.B);
}
