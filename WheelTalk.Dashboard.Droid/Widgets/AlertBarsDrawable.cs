using Android.Graphics;

namespace WheelTalk.Dashboard.Droid.Widgets;

/// <summary>
/// Тревога — две горизонтальные полосы, сверху и снизу рабочей области. Не рамка по кругу: рамка
/// растёт внутрь ровно туда, где у лент стоят цветные полосы, и на предельном ШИМ гасит оба
/// прибора и половину цифры скорости, то есть отключает показания в тот момент, ради которого они
/// и нужны. Сдвинуть её внутрь нельзя — шкала стоит у самого края.
/// <para>
/// Заодно площадь падает с четверти экрана до одной девятой, и порог WCAG по мельканию перестаёт
/// применяться по исключению площади — развилка «MIL-STD против WCAG» закрывается геометрией.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/AlertBarsDrawable.cs</c>. Закрывает пробел бенча:
/// <c>WheelTalk.Native/Drawing/DashboardView.AlertBars</c> реализовывал только тревогу по ШИМ (со
/// своим внутренним ритмом мигания на <c>NanoTime()</c>) — здесь, как и в MAUI-версии,
/// <see cref="Lit"/> считает вызывающий (частота мигания — настройка <c>DashboardOptions.BlinkHz</c>,
/// которая приложению, а не библиотеке), а добавлена немигающая мягкая тревога
/// <see cref="SpeedExceeded"/> жёлтым цветом. Ни одна константа (доли площади) не изменилась —
/// абсолютных dp здесь нет, плотность экрана не нужна.
/// </para>
/// </summary>
public sealed class AlertBarsDrawable
{
    /// <summary>Во сколько раз полоса меньше на пороге тревоги, чем в полный голос.</summary>
    private const float MinShare = 0.24f;

    /// <summary>Доля полосы в полный голос, отданная мягкой тревоге.</summary>
    private const float SpeedShare = 0.3f;

    private readonly Paint _fill = new() { AntiAlias = true };

    public required DashboardOptions Options { get; init; }

    /// <summary>0 — тревоги нет, 1 — в полный голос.</summary>
    public double Intensity { get; set; }

    /// <summary>
    /// Светлая фаза моргания. Тёмная — не рисуется вовсе (не заливается фоном), а не гасится
    /// прозрачным цветом: <see cref="Draw"/> в тёмной фазе просто не вызывает <see cref="Bars"/>,
    /// поэтому приборы под полосами видны половину времени, а не половину времени сквозь чёрное.
    /// Проверено при правке 29.07.2026 (WCAG 2.3.1, dashboard-feedback.md «Решения 29.07.2026» →
    /// «Мигание и WCAG 2.3.1») — решение прогона 2 уже было в силе и не потребовало изменений.
    /// </summary>
    public bool Lit { get; set; }

    /// <summary>
    /// Мягкая тревога — превышена скорость. Показывается теми же полосами, тонкими и без моргания:
    /// «есть о чём знать», а не «смотри сюда». Уступает тревоге по ШИМ, когда та поднята: два
    /// сигнала разом в самый близкий к пределу момент не помогают никому.
    /// </summary>
    public bool SpeedExceeded { get; set; }

    /// <param name="fullThickness">
    /// Толщина полосы в полный голос, если вызывающий считает её сам. Не задана — своя мера: доля
    /// <see cref="IDashboardThresholds.AlertBarCoverage"/> от <b>меньшей</b> стороны. Меньшая она
    /// потому, что под полосами панели стоят приборы, и расти вниз полосе некуда.
    /// <para>
    /// Задают её там, где приборов под полосами нет вовсе — на обычных экранах приложения
    /// (<c>AlertOverlayView</c>): места там больше, и доля берётся от высоты (решение владельца
    /// 05.08.2026). Сила тревоги множит толщину в обоих случаях одинаково — этим правилом полосы и
    /// остаются одной и той же тревогой, а не двумя разными.
    /// </para>
    /// </param>
    public void Draw(Canvas canvas, RectF rect, float? fullThickness = null)
    {
        if (!Options.ShowAlertBorder) return;

        float full = fullThickness
            ?? Math.Min(rect.Width(), rect.Height()) * (float)Options.Thresholds.AlertBarCoverage;

        if (Intensity > 0)
        {
            if (!Lit) return;

            Bars(canvas, rect, full * (float)Math.Clamp(Intensity, MinShare, 1), Options.Palette.Danger);
        }
        else if (SpeedExceeded)
        {
            Bars(canvas, rect, full * SpeedShare, Options.Palette.Caution);
        }
    }

    private void Bars(Canvas canvas, RectF rect, float height, Color color)
    {
        _fill.Color = color;
        canvas.DrawRect(rect.Left, rect.Top, rect.Right, rect.Top + height, _fill);
        canvas.DrawRect(rect.Left, rect.Bottom - height, rect.Right, rect.Bottom, _fill);
    }
}
