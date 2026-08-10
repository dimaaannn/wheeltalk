namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Четырнадцать значков шторки — по имени, а не по номеру ресурса в коде хозяина меню
/// (план 32 §1). Контуры перенесены из макета <c>quicksheet-mockup 3c</c>: сетка 24×24, обводка 2,
/// скруглённые концы, цвет задаёт кнопка тинтом.
/// <para>
/// Живут здесь, а не в приложении, по той же причине, что и сама шторка: её видит и стенд, а он
/// ресурсов приложения не видит. Имя в этом списке — то, чем значок зовут в <c>MainActivity</c>, в
/// реестре экранов и в замке <c>QuickSheetLayoutTests</c>: замок держит уникальность знаков именно
/// по этим именам, и «один знак на два дела» — половина жалобы плана 25 §1 — снова ронять сборку
/// будет здесь.
/// </para>
/// </summary>
public static class QuickIcons
{
    public static int Light => Resource.Drawable.ic_quick_light;

    public static int Beep => Resource.Drawable.ic_quick_beep;

    /// <summary>Связь: подключить или отключить — одна команда на оба действия.</summary>
    public static int Power => Resource.Drawable.ic_quick_power;

    public static int Record => Resource.Drawable.ic_quick_record;

    public static int Reset => Resource.Drawable.ic_quick_reset;

    /// <summary>Не гасить экран.</summary>
    public static int Sun => Resource.Drawable.ic_quick_sun;

    /// <summary>Закрепить: показывать панель поверх замка.</summary>
    public static int Lock => Resource.Drawable.ic_quick_lock;

    /// <summary>Не закрывать шторку — булавка.</summary>
    public static int Pin => Resource.Drawable.ic_quick_pin;

    /// <summary>Столбики: экран данных. Шкала со стрелкой — у <see cref="Panel"/>.</summary>
    public static int Data => Resource.Drawable.ic_quick_data;

    public static int Rides => Resource.Drawable.ic_quick_rides;

    public static int Settings => Resource.Drawable.ic_quick_settings;

    /// <summary>Шкала со стрелкой: корешок экрана «Панель».</summary>
    public static int Panel => Resource.Drawable.ic_quick_panel;

    public static int Tiles => Resource.Drawable.ic_quick_tiles;

    /// <summary>Реплей — только у отладочного транспорта.</summary>
    public static int Play => Resource.Drawable.ic_quick_play;
}
