using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using WheelTalk.Ble;
using WheelTalk.Composition;
using WheelTalk.Core.Ports;
using WheelTalk.Lab.Data;
using WheelTalk.Storage;
using WheelTalk.Debug;

namespace WheelTalk;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        // Готовая база стенда: `dotnet run -- history <файл> [часы]`. Здесь она делается за секунды,
        // на телефоне те же сутки писались бы минутами — а всё это время графики пусты. Отдельным
        // входом до всей BLE-обвязки: ни адаптера, ни колеса ей не нужно.
        if (args is ["history", var path, ..])
        {
            double hours = args.Length > 2 && double.TryParse(args[2], out double given) ? given : 24;
            Console.WriteLine(await LabHistoryFile.CreateAsync(path, TimeSpan.FromHours(hours), new StorageOptions
            {
                // Реже боевого: строки идут сотнями тысяч подряд, и коммит на каждые сто миллисекунд
                // разложил бы их тысячами транзакций.
                CommitInterval = TimeSpan.FromSeconds(1),
            }));
            Console.WriteLine($"Файл: {Path.GetFullPath(path)}");

            return;
        }

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
