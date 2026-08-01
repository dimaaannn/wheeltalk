using System.Globalization;

namespace WheelTalk.Droid.Logging;

/// <summary>
/// Where ride files live and what they are called. The external files directory is the useful
/// spot: it needs no storage permission, it shows up over USB as
/// <c>Android/data/com.wheeltalk.droid/files/</c> so a dump can be pulled off or pushed in with
/// <c>adb</c>, and it goes away when the app is uninstalled.
/// <para>
/// Names and the per-wheel folder are the original WheelLog's, so files off either app look the
/// same and nothing mixes two wheels together.
/// </para>
/// </summary>
public static class RideFiles
{
    public static string Root =>
        Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
        ?? throw new InvalidOperationException("External storage is unavailable — no place to keep ride files.");

    /// <summary>A bare file name lives in <see cref="Root"/>; a full path is taken as given.</summary>
    public static string Resolve(string fileName) =>
        Path.IsPathRooted(fileName) ? fileName : Path.Combine(Root, fileName);

    public static string RideLog(string mac, DateTimeOffset startedAt) =>
        InWheelFolder(mac, $"{Stamp(startedAt)}.csv");

    public static string RawDump(string mac, DateTimeOffset startedAt) =>
        InWheelFolder(mac, $"RAW_{Stamp(startedAt)}.csv");

    private static string Stamp(DateTimeOffset time) =>
        time.ToString("yyyy_MM_dd_HH_mm_ss", CultureInfo.InvariantCulture);

    private static string InWheelFolder(string mac, string fileName)
    {
        // A MAC has colons in it and a folder name cannot, which is the same substitution the
        // original makes.
        string folder = Path.Combine(Root, mac.Replace(':', '_'));
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, fileName);
    }
}
