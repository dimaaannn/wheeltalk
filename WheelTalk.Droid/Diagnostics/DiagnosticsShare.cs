using Android.Content;
using AndroidX.Core.Content;
using Application = Android.App.Application;

namespace WheelTalk.Droid.Diagnostics;

/// <summary>
/// Кнопка «передать отладочную информацию» — <b>вход на экран состава</b>, а не отправка.
/// <para>
/// Собирает комплект и отдаёт его системному диалогу <see cref="DiagnosticsShareActivity"/>: сперва
/// человек видит, что уходит и сколько весит, и только его нажатие открывает диалог «поделиться»
/// (план 11 §4.4). Прежде кнопка отдавала голый <c>diagnostics.log</c> прямо в диалог — уходило то,
/// чего никто не видел.
/// </para>
/// <para>
/// Никуда сами не отправляем и никакого сервера не заводим — получателя выбирает человек, в
/// обычном диалоге Android. Это и проще, и честнее: файл содержит журнал устройства, и решать,
/// кому он уходит, должен тот, чьё это устройство. <c>FileProvider</c> объявлен в манифесте
/// (опись §6), и путь наружу — <c>content://</c>-Uri от него.
/// </para>
/// </summary>
public static class DiagnosticsShare
{
    /// <summary>
    /// Кнопка больше не открывает системный диалог сама: сперва — экран состава
    /// (<see cref="DiagnosticsShareActivity"/>, план 11 §4.4), где видно, что именно уйдёт и
    /// сколько весит. Отправку делает он, по нажатию «Отправить».
    /// <para>
    /// Сбор и упаковка переехали туда же: делать их здесь значило бы собирать комплект и для того,
    /// кто нажал случайно и тут же передумал.
    /// </para>
    /// </summary>
    public static void Send()
    {
        var context = Application.Context;

        // NewTask — потому что зовут из описания настройки, у которого живой активности на руках
        // нет: каталог настроек статичен и об экранах не знает.
        var screen = new Intent(context, typeof(DiagnosticsShareActivity));
        screen.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(screen);
    }
}
