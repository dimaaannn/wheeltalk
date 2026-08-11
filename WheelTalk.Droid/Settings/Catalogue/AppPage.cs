using WheelTalk.Core.Services;
using WheelTalk.Core.Settings;
using WheelTalk.Droid.Configuration;
using WheelTalk.Storage;

namespace WheelTalk.Droid.Settings.Catalogue;

/// <summary>
/// Descriptors of <see cref="SettingsPage.Application"/> — split out of
/// <c>SettingsCatalogue.Build</c> (plan 14, А2.1), body moved as-is.
/// </summary>
internal static class AppPage
{
    /// <summary>
    /// Ключ настройки «предлагать отправку после сбоя» — тем же приёмом, что
    /// <see cref="ExperimentalPage.CellsKey"/>: <c>MainActivity</c> пишет её галочкой в диалоге, а не
    /// со страницы настроек, и своего дескриптора на руках у него нет.
    /// </summary>
    public const string PromptAfterCrashKey = "Diagnostics:PromptAfterCrash";

    public static IReadOnlyList<SettingDescriptor> Build(
        ConnectionOptions connection, PowerOptions power, ScreenOptions screen, StorageOptions storage,
        DiagnosticsOptions diagnostics, Action share)
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

            // ---- Телефон в поездке -------------------------------------------------------
            //
            // Три ручки об одном: доедет ли приложение до конца поездки живым. Экран не гаснет и
            // ложится поверх замка — чтобы приборы было видно; экономия заряда душит фон — чтобы
            // связь не умирала на полпути. Первые две приехали с «Отображения» (план 30 §3.1, Д6):
            // они про телефон, а не про вид панели, и искать их будут здесь. Ключи прежние.
            new()
            {
                Key = "Screen:KeepOn",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Application,
                SectionKey = "SectionPhoneOnRide",
                LabelKey = "SettingKeepScreenOn",
                HintKey = "SettingKeepScreenOnHint",
                GlobalOnly = true,
                Transient = true,
                Current = () => screen.KeepOn.ToString(),
                Apply = text => screen.KeepOn = SettingsCatalogue.ParseBool(text),
            },
            new()
            {
                Key = "Screen:ShowOverLock",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Application,
                SectionKey = "SectionPhoneOnRide",
                LabelKey = "SettingShowOverLock",
                HintKey = "SettingShowOverLockHint",
                GlobalOnly = true,
                Transient = true,
                Current = () => screen.ShowOverLock.ToString(),
                Apply = text => screen.ShowOverLock = SettingsCatalogue.ParseBool(text),
            },
            new()
            {
                Key = "Power:WarnAboutBatterySaver",
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Application,
                SectionKey = "SectionPhoneOnRide",
                LabelKey = "SettingWarnBatterySaver",
                HintKey = "SettingWarnBatterySaverHint",
                GlobalOnly = true,
                Current = () => power.WarnAboutBatterySaver.ToString(),
                Apply = text => power.WarnAboutBatterySaver = SettingsCatalogue.ParseBool(text),
            },

            // ---- Хранение ----------------------------------------------------------------
            //
            // Срок жизни поездок (план 11 §4.5). Заводское значение — ноль, «не удалять»: поездка
            // весит десятки байт, на накопленном стоит план 9, и умолчание, которое молча стирает
            // историю, — то, чего не прощают. Чистка идёт на старте приложения, после того как слои
            // настроек легли на опции (MainApplication): сам срок лежит настройкой в той же базе,
            // и на её открытии читать его ещё неоткуда.
            new()
            {
                Key = "Storage:RideRetention",
                Kind = SettingKind.Number,
                Page = SettingsPage.Application,
                SectionKey = "SectionStorage",
                LabelKey = "SettingRideRetention",
                HintKey = "SettingRideRetentionHint",
                UnitKey = "UnitDays",
                // Хранилище одно на все колёса: база общая, и «удалять старше месяца» не может
                // значить разное в зависимости от того, к какому колесу сейчас подключены.
                GlobalOnly = true,
                Minimum = 0,
                Maximum = 365,
                Step = 1,
                Decimals = 0,
                Current = () => SettingsCatalogue.Fixed(storage.RideRetention.TotalDays, 0),
                Apply = text => storage.RideRetention =
                    TimeSpan.FromDays(SettingsCatalogue.ParseNumber(text)),
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
                Key = PromptAfterCrashKey,
                Kind = SettingKind.Toggle,
                Page = SettingsPage.Application,
                SectionKey = "SectionDiagnostics",
                LabelKey = "SettingPromptShareAfterCrash",
                HintKey = "SettingPromptShareAfterCrashHint",
                // Привычка телефона, а не колеса: предлагать отправку после сбоя — то же самое
                // независимо от того, к какому колесу приложение в этот момент подключено.
                GlobalOnly = true,
                Current = () => diagnostics.PromptShareAfterCrash.ToString(),
                Apply = text => diagnostics.PromptShareAfterCrash = SettingsCatalogue.ParseBool(text),
            },
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
