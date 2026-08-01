using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using WheelTalk.Ble;
using WheelTalk.Composition;
using WheelTalk.Core.Ports;
using WheelTalk.Debug;

namespace WheelTalk;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true; // let us shut down gracefully (DisconnectAsync) instead of hard-killing
            cts.Cancel();
        };

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory, // host auto-loads appsettings.json from here
            Args = args,
        });

        builder.Logging.ClearProviders();
        var serilog = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .CreateLogger();
        builder.Logging.AddSerilog(serilog, dispose: true);

        // --- Shared / infrastructure dependencies ---

        // "WheelTalk" section -> WheelTalkOptions (wheel address, protocol, IWheelConfig defaults)
        builder.Services.AddWheelTalkOptions(builder.Configuration);

        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddSingleton<WindowsBleClient>();
        builder.Services.AddSingleton<ITransport>(sp => sp.GetRequiredService<WindowsBleClient>());

        // --- Business logic (state/decoder/service/presenter/harness) ---

        builder.Services.AddWheelBusinessLogic();

        using var host = builder.Build();
        var harness = host.Services.GetRequiredService<TestHarness>();

        // Uncomment exactly one scenario at a time, then run.

        //await harness.Scan(cts.Token);
        //await harness.RawDump(cts.Token);
        //await harness.LiveSpeedPwmVoltage(cts.Token);
        //await harness.HeadlightOn(cts.Token);
        await harness.RecordTelemetryCsv(cts.Token);
        // await harness.ReplayRawFile(@"C:\path\to\RAW_veteran.csv", cts.Token);

        // No manual loggerFactory.Dispose() needed — `using host` tears down the whole DI
        // container, and AddSerilog(dispose: true) closes the Serilog logger along with it.
    }
}
