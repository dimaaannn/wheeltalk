using WheelTalk.Core.Alerts;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Services;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Droid.Configuration;

namespace WheelTalk.Droid.Settings.Catalogue;

/// <summary>
/// The eight things <see cref="SettingsCatalogue.Build"/> used to take as eight parameters,
/// carried as one (plan 14, А2.1 second commit) — a parameter reshuffle, not a redesign: the
/// field names match the old parameter names one for one. <c>RenameWheel</c> уступил место
/// <see cref="WheelIdentity"/>: имя колеса перестало быть записью в общий файл и стало слоевой
/// настройкой этого колеса.
/// </summary>
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
    /// <summary>
    /// Протокол подключённого колеса — опознанный, а не выбранный, и потому спрашиваемый заново
    /// на каждой отрисовке страницы. Делегатом, а не значением: описания строятся один раз при
    /// запуске, когда колесо ещё молчит, а настройки Begode обязаны появиться, как только оно
    /// назовётся. <c>null</c> — «пока не знаем».
    /// </summary>
    Func<WheelProtocol?> Protocol);
