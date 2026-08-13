using WheelTalk.Core.Dashboard;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Widgets;
using WheelTalk.Lab.Droid.Scenarios;

namespace WheelTalk.Lab.Droid;

/// <summary>
/// Всё, что крутится на устройстве, в одном месте: настройки панели и правка записи.
/// <para>
/// Один экземпляр на приложение — иначе экран стенда и страница ручек показывали бы разное. В
/// MAUI-версии его раздавал контейнер; здесь Activity создаёт сам Android, конструктор с
/// параметрами ей не передать, а заводить контейнер ради одного объекта незачем — отсюда
/// <see cref="Current"/>.
/// </para>
/// </summary>
public sealed class LabSettings
{
    public static LabSettings Current { get; } = new();

    /// <summary>
    /// Настройки панели стенда. Слова — свои литералами (ресурсы приложения стенду не видны), и это
    /// тот же порядок, каким слова получают плитки: паритет со словами приложения стережёт
    /// <c>LabTileWordsParityTests</c>. Без них подписи справочного блока центра стояли бы сырыми
    /// ключами на всех снимках стенда.
    /// </summary>
    public DashboardOptions Options { get; } = new()
    {
        Words = Ui.LabMetricWords.Get,
        CentreRows = CenterReadings.Known(CenterLayout.Sane(new LabCentreLayoutFile().Load())),
    };

    /// <summary>Правка записи. Меняется целиком, потому что запись после неё пересобирается.</summary>
    public TimelineTweaks Tweaks { get; set; } = TimelineTweaks.None;

    /// <summary>
    /// Имя колеса, которое показывает панель на стоянке. В приложении это настройка на колесо (по
    /// умолчанию — рекламное имя из Bluetooth, дальше правится хозяином); стенду достаточно образца,
    /// чтобы было видно, как оно выглядит и когда гаснет.
    /// </summary>
    public string WheelName { get; set; } = "Мой Шерман";

    /// <summary>Идёт ли запись — точка в углу поля панели. У стенда это просто переключатель.</summary>
    public bool Recording { get; set; } = true;

    /// <summary>
    /// Ряд ячеек, «заданный человеком»: в приложении это настройка колеса, здесь — ручка. Ноль —
    /// не задано. Стенду она нужна, потому что на ленту банки пускается только записанное число, а
    /// записи поездок такого числа не несут (план 27 §27.4).
    /// </summary>
    public int CellsInSeries { get; set; }

    /// <summary>Подсказка про шторку быстрых команд внизу.</summary>
    public bool ShowSheetHint { get; set; } = true;

    /// <summary>Вуаль устаревших данных. В приложении её включает возраст кадра, у стенда — тумблер.</summary>
    public bool Stale { get; set; }

    /// <summary>Полоса тревоги колеса над панелью. Видна только в режиме «экран целиком».</summary>
    public bool WheelAlarm { get; set; }

    /// <summary>
    /// Панель под системной строкой — как в приложении, где она уходит под статус-бар. От этого
    /// зависят две вещи, которых иначе на стенде не увидеть: тень под значками системы и затухание
    /// разметки лент у верхней кромки.
    /// <para>
    /// Включено по умолчанию: стенд существует, чтобы показывать панель такой, какая она в
    /// приложении, а не такой, какой её удобнее рисовать. Выключатель оставлен, чтобы посмотреть на
    /// панель без наложения — например, когда правишь сами ленты.
    /// </para>
    /// <para>
    /// Высота берётся настоящая, у окна стенда (<c>LabActivity.SystemBarHeight</c>), а не
    /// придуманная: на разных телефонах бар разный, и подгонять отступы под выдуманное число значило
    /// бы проверять не то.
    /// </para>
    /// </summary>
    public bool UnderSystemBar { get; set; } = true;

    /// <summary>Поднимается после любой правки; стенд пересобирает сценарий и перерисовывает панель.</summary>
    public event Action? Changed;

    public void Notify()
    {
        Options.Notify();
        Changed?.Invoke();
    }
}
