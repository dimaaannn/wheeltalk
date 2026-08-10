using Android.Graphics;

namespace WheelTalk.Dashboard.Droid;

/// <summary>
/// Цвета шторки — свои, рядом с <see cref="DashboardPalette"/> и отдельно от неё: та красит
/// показания (ШИМ, ленты, тревогу) и переключается человеком, а эти держат саму поверхность и
/// переключению не подлежат.
/// <para>
/// Тёмные без оглядки на тему телефона, как и фон панели (<see cref="DashboardPalette.Background"/>
/// — <c>#101010</c> при любой теме): шторка лежит поверх панели, и светлеть ей не от чего. Значения
/// — из макета <c>quicksheet-mockup 3a/3b</c>, посчитанные там же на контраст: белое слово на
/// <see cref="Plate"/> и чёрное на янтаре «включено» проходят 4,5:1 (WCAG 1.4.3).
/// </para>
/// </summary>
public static class QuickSheetPalette
{
    /// <summary>Подложка шторки — светлее фона панели, чем и отделяется от неё.</summary>
    public static readonly Color Background = Color.ParseColor("#22222A");

    /// <summary>Верхняя граница подложки: край шторки виден и в темноте.</summary>
    public static readonly Color TopBorder = Color.ParseColor("#383842");

    /// <summary>Плашка кнопки команды. Она же — цвет черты между корешками экранов и командами.</summary>
    public static readonly Color Plate = Color.ParseColor("#33333D");

    /// <summary>
    /// Заливка выбранного корешка экрана. Тот же акцент, что и был в тёмной теме, но теперь <b>при
    /// любой</b> — решение владельца 10.08.2026: шторка тёмная всегда, и притемнённый акцент светлой
    /// темы (<c>#512BD4</c>), выбранный когда-то под белую страницу, давал на этой подложке 2,0:1 —
    /// ниже требуемых 3:1 к нетекстовым элементам (WCAG 1.4.11). Здесь 6,9:1.
    /// </summary>
    public static readonly Color Accent = Color.ParseColor("#AC99EA");

    /// <summary>Слово раздела на боковом корешке.</summary>
    public static readonly Color Spine = Color.ParseColor("#8E8E9A");

    /// <summary>Слово и значок там, где заливки нет: невыбранный корешок экрана, переход.</summary>
    public static readonly Color Ink = Color.ParseColor("#D6D6DE");

    /// <summary>Обводка невыбранного корешка экрана.</summary>
    public static readonly Color TabBorder = Color.ParseColor("#44444F");

    /// <summary>Обводка перехода: тоньше видом, чем корешок, — переход не выбирают.</summary>
    public static readonly Color LinkBorder = Color.ParseColor("#3E3E48");

    /// <summary>Черенок шторки.</summary>
    public static readonly Color Grabber = Color.ParseColor("#55555F");
}
