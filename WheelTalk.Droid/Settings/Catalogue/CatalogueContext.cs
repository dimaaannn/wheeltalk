using WheelTalk.Core.Alerts;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Services;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Main;

namespace WheelTalk.Droid.Settings.Catalogue;

/// <summary>
/// The eight things <see cref="SettingsCatalogue.Build"/> used to take as eight parameters,
/// carried as one (plan 14, А2.1 second commit) — a parameter reshuffle, not a redesign: the
/// field names match the old parameter names one for one. <c>RenameWheel</c> уступил место
/// <see cref="WheelIdentity"/>: имя колеса перестало быть записью в общий файл и стало слоевой
/// настройкой этого колеса.
/// </summary>
/// <param name="Protocol">
/// Протокол подключённого колеса — опознанный, а не выбранный, и потому спрашиваемый заново
/// на каждой отрисовке страницы. Делегатом, а не значением: описания строятся один раз при
/// запуске, когда колесо ещё молчит, а настройки Begode обязаны появиться, как только оно
/// назовётся. <c>null</c> — «пока не знаем».
/// </param>
/// <param name="PreviewAlarm">
/// Дать послушать выбранный сигнал тревоги, 0…1. Делегатом, а не самим <c>AlertSignals</c>, по той
/// же причине, что протокол и пароль: описания строятся при запуске, а звук нужен только тому, кто
/// открыл страницу «Предупреждения».
/// </param>
/// <param name="LastFrame">
/// Последний кадр телеметрии — то, по чему кнопка «рассчитать» считает ряд, а строка ряда решает,
/// предупреждать ли о неправдоподобном числе. <c>null</c> — колесо ещё ничего не сказало.
/// </param>
/// <param name="SaveCells">
/// Записать ряд в настройку — через <c>SettingsBinder</c>, а не мимо: слой, крючок правки и
/// признаки у кнопки те же, что у правки руками.
/// </param>
/// <param name="Panels">
/// Варианты панели и живой выбор между ними (план 17 §3). Строка выбора показывается, только когда
/// вариантов больше одного, — в Release он один.
/// </param>
public sealed record CatalogueContext(
    AppWheelConfig Wheel,
    AlertOptions Alerts,
    AlertSignalOptions Channels,
    ConnectionOptions Connection,
    WheelOptions Selected,
    DashboardOptions Dashboard,
    Action Share,
    WheelIdentity Identity,
    ScreenOptions Screen,
    PowerOptions Power,
    Func<WheelProtocol?> Protocol,
    Action RestartAuthentication,
    Action<double> PreviewAlarm,
    Func<TelemetrySnapshot?> LastFrame,
    Action<int> SaveCells,
    PanelVariants Panels);
