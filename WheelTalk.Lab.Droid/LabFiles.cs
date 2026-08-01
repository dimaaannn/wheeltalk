namespace WheelTalk.Lab.Droid;

/// <summary>
/// Куда стенд кладёт снимки. Внешний каталог приложения, как и у ride-файлов приложения: он виден
/// как <c>Android/data/com.wheeltalk.lab.droid/files/</c>, разрешений не требует и забирается
/// обычным <c>adb pull</c> — а из внутреннего каталога снимки пришлось бы доставать через
/// <c>run-as</c>.
/// <para>
/// Перенесено из <c>WheelTalk.Lab/LabFiles.cs</c> без изменений.
/// </para>
/// </summary>
public static class LabFiles
{
    public static string Root =>
        Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
        ?? throw new InvalidOperationException("Внешний каталог недоступен — снимки класть некуда.");

    public static string ShotsFolder(string scenarioId)
    {
        string folder = Path.Combine(Root, "shots", scenarioId);
        Directory.CreateDirectory(folder);
        return folder;
    }
}
