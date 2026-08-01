using WheelTalk.Storage;

using WheelTalk.Droid.Logging;

namespace WheelTalk.Droid.Rides;

/// <summary>
/// Puts an exported ride on disk under the name and in the folder the recorder used to write
/// directly — same wheel folder, same <c>yyyy_MM_dd_HH_mm_ss.csv</c>, so a file off this app and a
/// file off the original still look alike and <c>adb pull</c> still finds them where it did.
/// </summary>
public static class RideCsvExport
{
    public static string Write(RideExporter exporter, RideSummary ride)
    {
        string path = RideFiles.RideLog(ride.Mac, ride.StartedAt);

        // CRLF, as WheelLog's FileUtil.writeLine writes — a StreamWriter on Android defaults to LF
        // and would produce a file subtly unlike the one this format promises.
        using var writer = new StreamWriter(path, append: false) { NewLine = "\r\n" };
        foreach (string line in exporter.Export(ride.Id))
        {
            writer.WriteLine(line);
        }

        return path;
    }
}
