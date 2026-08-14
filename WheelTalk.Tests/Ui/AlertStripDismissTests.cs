using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Ui;

/// <summary>
/// Смахивание полосы сообщений (решение владельца 15.08.2026 — «сделать возможность убрать любое
/// сообщение смахиванием в сторону»; повод — строка о заморозке фона, которую нельзя было убрать
/// ничем). Путь владельца: сообщение показано → смахнул в сторону → исчезло → не возвращается, пока
/// не сменились слова или не пришло новое.
/// <para>
/// Замки по исходникам: жест и глушение — андроидная механика, поднять её из тестов нельзя, а
/// стеречь надо решения — чем ловится смахивание, кто решает «что заглушить» и где глушение
/// снимается.
/// </para>
/// </summary>
public class AlertStripDismissTests
{
    private static string Strip() =>
        RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/AlertStrip.cs");

    private static string Main() =>
        RepoFiles.Read("WheelTalk.Droid/Main/MainActivity.cs");

    private static string Overlay() =>
        RepoFiles.Read("WheelTalk.Droid/Alerts/AlertOverlayView.cs");

    /// <summary>
    /// Полоса ловит смахивание сама и наружу отдаёт смахнутый текст: решает, что заглушить, хозяин —
    /// у полосы нет состояния, чтобы отличить одноразовое служебное от живой тревоги.
    /// </summary>
    [Fact]
    public void The_strip_catches_the_swipe_and_hands_the_text_to_its_owner()
    {
        string strip = Strip();

        Assert.Contains("public event Action<string>? Dismissed;", strip);
        Assert.Contains("public override bool OnTouchEvent(MotionEvent? e)", strip);

        // Порог — треть ширины: случайное касание полосу не уносит, намеренный жест — уносит.
        Assert.Contains("Math.Abs(TranslationX) > Width / 3f", strip);

        // Спрятанная полоса возвращается на место целой: сдвиг и прозрачность жеста не переживают Hide.
        string hide = RepoFiles.MethodBody(strip, "public void Hide()");
        Assert.Contains("TranslationX = 0;", hide);
    }

    /// <summary>
    /// Главный экран: служебное сообщение гаснет совсем (оно одноразовое), тревога колеса — до
    /// пропажи или смены слов. Глушить тревогу насовсем значило бы прятать следующую беду.
    /// </summary>
    [Fact]
    public void The_owner_hushes_what_was_shown_and_only_while_it_lasts()
    {
        string main = Main();

        Assert.Contains("_alertStrip.Dismissed += OnStripDismissed;", main);
        Assert.Contains("if (shown == _notice) _notice = \"\";", main);
        Assert.Contains("else _hushedAlert = shown;", main);

        // Тревога ушла — глушение снято: вернувшаяся обязана показаться, даже теми же словами.
        Assert.Contains("if (wheel.Length == 0) _hushedAlert = \"\";", main);
        Assert.Contains("wheel != _hushedAlert", main);
    }

    /// <summary>
    /// Наложение на свои экраны: смахивание убирает слова, но не тревогу — полосы краёв остаются
    /// видимыми и остаются насквозь для пальца; конец тревоги снимает глушение.
    /// </summary>
    [Fact]
    public void On_the_overlay_the_swipe_removes_words_but_not_the_alert()
    {
        string overlay = Overlay();

        Assert.Contains("_strip.Dismissed += shown => _hushed = shown;", overlay);
        Assert.Contains("if (text != _hushed)", overlay);

        string hide = RepoFiles.MethodBody(overlay, "public void Hide()");
        Assert.Contains("_hushed = \"\";", hide);

        // Контейнер и полосы краёв не отнимают касаний — насквозь, как и было.
        Assert.Contains("Clickable = false;", overlay);
        Assert.Contains("Focusable = false;", overlay);
    }

    /// <summary>
    /// Поверх ЧУЖИХ приложений смахивания нет и быть не должно: окно системного оверлея не трогает
    /// касаний вовсе — красть жест у чужого приложения нельзя, тревогу там убирает сама тревога.
    /// </summary>
    [Fact]
    public void Over_foreign_apps_the_overlay_still_takes_no_touches()
    {
        Assert.Contains("WindowManagerFlags.NotTouchable",
            RepoFiles.Read("WheelTalk.Droid/Alerts/SystemAlertOverlay.cs"));
    }
}
