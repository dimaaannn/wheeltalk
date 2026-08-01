using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Logging;

namespace WheelTalk.Droid.App.Composition;

/// <summary>
/// The ride recorder and the raw-frame dump — split out of <c>MainApplication.OnCreate</c>
/// (plan 14, А2.2), registrations moved as-is.
/// </summary>
public static class RecordingServiceCollectionExtensions
{
    public static IServiceCollection AddRecording(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LoggingOptions>(configuration.GetSection(LoggingOptions.SectionName));

        services.AddSingleton<RideRecorder>();
        services.AddSingleton<RawFrameRecorder>();

        return services;
    }
}
