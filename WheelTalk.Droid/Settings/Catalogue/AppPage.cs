using WheelTalk.Core.Services;
using WheelTalk.Core.Settings;
using WheelTalk.Droid.Configuration;

namespace WheelTalk.Droid.Settings.Catalogue;

/// <summary>
/// Descriptors of <see cref="SettingsPage.Application"/> — split out of
/// <c>SettingsCatalogue.Build</c> (plan 14, А2.1), body moved as-is.
/// </summary>
internal static class AppPage
{
    public static IReadOnlyList<SettingDescriptor> Build(ConnectionOptions connection, PowerOptions power, Action share)
    {
        return
        [
            // ---- Application -------------------------------------------------------------
            new()
            {
                Key = "Connection:FirstRetryDelay",
                Kind = SettingKind.Number,
                Page = SettingsPage.Application,
                SectionKey = "SectionConnection",
                LabelKey = "SettingFirstRetryDelay",
                HintKey = "SettingFirstRetryDelayHint",
                UnitKey = "UnitSeconds",
                // Обе паузы — свойство приложения, а не колеса: повторы живут в сессии и одинаковы
                // для любого адреса, к которому она стучится. Первая короткая потому, что
                // полуоткрытая связь расходится с первого же повтора; сессия берёт меньшую из двух.
                GlobalOnly = true,
                Minimum = 0.1,
                Maximum = 10,
                Step = 0.1,
                Decimals = 1,
                Current = () => SettingsCatalogue.Fixed(connection.FirstRetryDelay.TotalSeconds, 1),
                Apply = text => connection.FirstRetryDelay = TimeSpan.FromSeconds(SettingsCatalogue.ParseNumber(text)),
            },
            new()
            {
                Key = "Connection:RetryDelay",
                Kind = SettingKind.Number,
                Page = SettingsPage.Application,
                SectionKey = "SectionConnection",
                LabelKey = "SettingRetryDelay",
                HintKey = "SettingRetryDelayHint",
                UnitKey = "UnitSeconds",
                GlobalOnly = true,
                Minimum = 1,
                Maximum = 60,
                Step = 1,
                // Знаков ровно столько, сколько у Decimals: иначе заводской слой держит «5.0», а
                // правка пишет «5» — два представления одного значения в одной базе, и слой,
                // совпавший с нижним, перестаёт совпадать.
                Current = () => SettingsCatalogue.Fixed(connection.RetryDelay.TotalSeconds, 0),
                Apply = text => connection.RetryDelay = TimeSpan.FromSeconds(SettingsCatalogue.ParseNumber(text)),
            },
            new()
            {
                // Тоже свойство приложения, а не колеса, и стоит рядом с повторами по той же
                // причине: и то и другое — про то, доживёт ли связь до конца поездки.
                Key = "Power:WarnAboutBatterySaver",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Application,
                SectionKey = "SectionConnection",
                LabelKey = "SettingWarnBatterySaver",
                HintKey = "SettingWarnBatterySaverHint",
                GlobalOnly = true,
                Current = () => power.WarnAboutBatterySaver.ToString(),
                Apply = text => power.WarnAboutBatterySaver = SettingsCatalogue.ParseBool(text),
            },

            // ---- Разбор поломок ----------------------------------------------------------
            //
            // Здесь стояло «своего журнала в файл у приложения нет» — с 01.08.2026 неверно: есть
            // (FileLog), пишется всегда, потому что системный буфер отдают не все прошивки.
            //
            // Переключателя журнала нет по-прежнему, и это осознанно: журнал за галочкой уже стоил
            // нам разбора одного выезда. Кнопка ниже — для случая, когда приложение ведёт себя
            // странно, но не падает: она собирает окно своего журнала и хвост системного буфера в
            // один файл и отдаёт системному диалогу. Падение собирается само при следующем
            // запуске, по отсутствию метки штатного завершения.
            new()
            {
                Key = "Diagnostics:Share",
                Kind = SettingKind.Action,
                Page = SettingsPage.Application,
                SectionKey = "SectionDiagnostics",
                LabelKey = "SettingShareDiagnostics",
                HintKey = "SettingShareDiagnosticsHint",
                Current = () => "",
                Apply = _ => share(),
            },

            // ---- О приложении ------------------------------------------------------------
            // Справка, не настройка: номер сборки — первое, что нужно разбору любой жалобы, а
            // увидеть его до сих пор было негде, кроме системных настроек Android.
            new()
            {
                Key = "App:Version",
                Kind = SettingKind.Note,
                Page = SettingsPage.Application,
                SectionKey = "SectionAbout",
                LabelKey = "SettingAppVersion",
                GlobalOnly = true,
                Current = AppVersion,
                Apply = _ => { },
            },
        ];
    }

    /// <summary>
    /// «1.4.0 (14)» — имя и код сборки из манифеста, как их видит Android. Из пакета, а не из
    /// констант: вторая копия номера разошлась бы с манифестом при первом же выпуске.
    /// </summary>
    private static string AppVersion()
    {
        var context = Android.App.Application.Context;
        var manager = context.PackageManager!;
        string package = context.PackageName!;

        var info = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? manager.GetPackageInfo(package, Android.Content.PM.PackageManager.PackageInfoFlags.Of(0L))!
            : manager.GetPackageInfo(package, 0)!;

        return $"{info.VersionName} ({info.LongVersionCode})";
    }
}
