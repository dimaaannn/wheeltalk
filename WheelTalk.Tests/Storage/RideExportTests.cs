using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Playback;
using WheelTalk.Core.Logging;
using WheelTalk.Storage;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Storage;

/// <summary>
/// The gate the database had to pass before it was allowed to replace the file: a ride written to
/// SQLite and read back out has to be the very same CSV the recorder used to write straight from
/// the wheel. Not similar — identical, line for line.
/// <para>
/// The ride is the real one off a Sherman L on 28.07.2026 (<c>logs/field-2026-07-28/report.md</c>),
/// decoded from the frames the wheel actually sent. A synthetic dump would not do: the two things
/// most likely to come apart here are rounding a computed duty cycle and a timezone, and neither
/// shows up on values someone chose to be round.
/// </para>
/// </summary>
public class RideExportTests
{
    private const string Mac = "88:25:83:F5:75:4A";

    /// <summary>The zone the ride happened in, and the one the export has to print back.</summary>
    private static readonly DateTimeOffset Start =
        new(2026, 7, 28, 0, 47, 44, TimeSpan.FromHours(3));

    [Fact]
    public async Task A_real_ride_exports_to_the_file_it_would_have_been_written_as()
    {
        var snapshots = await RealRide();
        Assert.True(snapshots.Count > 500, $"ожидались сотни отсчётов, получено {snapshots.Count}");

        // What plan 5 wrote: a line per snapshot, straight from the wheel, no database involved.
        var written = new List<string> { RideLog.Header };
        string lastAlert = "";
        for (int i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            string alert = snapshot.Alert == lastAlert ? "" : snapshot.Alert;
            lastAlert = snapshot.Alert;
            written.Add(RideLog.FormatLine(At(i), snapshot with { Alert = alert }));
        }

        using var temp = new TempDatabase();
        var database = temp.Open();
        await using (var store = temp.Store(database))
        {
            store.BeginRide();
            for (int i = 0; i < snapshots.Count; i++)
            {
                store.Write(Mac, nameof(WheelProtocol.Veteran), snapshots[i], At(i));
            }

            await store.CloseRideAsync();
        }

        var exported = new RideExporter(database).Export(1).ToList();

        Assert.Equal(written.Count, exported.Count);
        // Compared line by line rather than as one blob: a mismatch on row 812 of a thousand is
        // unreadable as a diff of two joined strings.
        for (int i = 0; i < written.Count; i++)
        {
            Assert.Equal(written[i], exported[i]);
        }
    }

    /// <summary>
    /// The summary is what a list of rides will be built from, and the two things it is easy to get
    /// wrong are both here: the time in the zone it was ridden in, not UTC, and a row count that
    /// does not require reading the ride.
    /// </summary>
    [Fact]
    public async Task A_ride_can_be_found_again_without_reading_it()
    {
        var snapshots = await RealRide();

        using var temp = new TempDatabase();
        var database = temp.Open();
        await using (var store = temp.Store(database))
        {
            store.BeginRide();
            for (int i = 0; i < snapshots.Count; i++)
            {
                store.Write(Mac, nameof(WheelProtocol.Veteran), snapshots[i], At(i));
            }

            await store.CloseRideAsync();
        }

        var ride = Assert.Single(new RideExporter(database).Rides());

        Assert.Equal(Mac, ride.Mac);
        Assert.Equal("Sherman L", ride.Model);
        Assert.Equal(snapshots.Count, ride.Rows);
        Assert.Equal(Start, ride.StartedAt);
        Assert.Equal(TimeSpan.FromHours(3), ride.StartedAt.Offset);
        Assert.NotNull(ride.Duration);
    }

    /// <summary>Two hundred milliseconds apart, which is roughly what the wheel sends.</summary>
    private static DateTimeOffset At(int index) => Start.AddMilliseconds(200 * index);

    private static async Task<List<TelemetrySnapshot>> RealRide()
    {
        var harness = DecoderHarness.ForVeteran(config => config.GotwayNegative = "0");
        var snapshots = new List<TelemetrySnapshot>();

        string fixture = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "shermanl_raw_ride_20260728.csv");
        var transport = new ReplayTransport(
            () => new StreamReader(fixture), TimeProvider.System, NullLogger<ReplayTransport>.Instance);
        transport.DataReceived += frame =>
        {
            harness.Decoder.Feed(frame);
            var snapshot = harness.Snapshot();
            if (snapshot.VoltageRaw != 0) snapshots.Add(snapshot);
        };
        await transport.PlayAsync(realtime: false);

        return snapshots;
    }
}
