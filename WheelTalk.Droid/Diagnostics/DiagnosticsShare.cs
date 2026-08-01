using Android.Content;
using AndroidX.Core.Content;
using Application = Android.App.Application;

namespace WheelTalk.Droid.Diagnostics;

/// <summary>
/// Кнопка «передать отладочную информацию»: собрать и отдать файл системному диалогу «поделиться».
/// <para>
/// Никуда сами не отправляем и никакого сервера не заводим — выбирает получателя человек, в
/// обычном диалоге Android. Это и проще, и честнее: файл содержит журнал устройства, и решать,
/// кому он уходит, должен тот, чьё это устройство.
/// </para>
/// <para>
/// MAUI-версия отдавала это <c>Share.Default.RequestAsync</c>, который сам заводил временный
/// <c>FileProvider</c>. Здесь `FileProvider` объявлен явно в манифесте (опись §6), и путь до
/// диалога — обычный <see cref="Intent.ActionSend"/> с <c>content://</c>-Uri от него.
/// </para>
/// </summary>
public static class DiagnosticsShare
{
    private const string Authority = "com.wheeltalk.droid.fileprovider";

    public static void Send() => _ = SendAsync();

    private static async Task SendAsync()
    {
        // Сбор синхронный и недолгий (тысяча строк), но diskIO на потоке разметки — плохая
        // привычка, а кнопка всё равно ждёт диалога.
        string path = await Task.Run(CrashReport.CollectOnDemand);

        var context = Application.Context;
        var uri = FileProvider.GetUriForFile(context, Authority, new Java.IO.File(path));

        var send = new Intent(Intent.ActionSend);
        send.SetType("text/plain");
        send.PutExtra(Intent.ExtraStream, uri);
        send.AddFlags(ActivityFlags.GrantReadUriPermission);

        var chooser = Intent.CreateChooser(send, Resources.Strings.AppStrings.SettingShareDiagnostics)!;
        chooser.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(chooser);
    }
}
