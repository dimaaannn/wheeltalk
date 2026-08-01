using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WheelTalk.Configuration;
using WheelTalk.Core.Ports;

namespace WheelTalk.Composition;

/// <summary>
/// Binds <see cref="WheelTalkOptions"/> and exposes the piece Core wants
/// (<see cref="IWheelConfig"/>) — the config half of the composition root, kept apart from the
/// domain wiring in <see cref="BusinessLogicServiceCollectionExtensions"/>.
/// </summary>
public static class ConfigurationServiceCollectionExtensions
{
    public static IServiceCollection AddWheelTalkOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // Bound once, up front: nothing here reloads config at runtime, so the IOptions<T>
        // indirection would only add ceremony over a plain singleton.
        var options = new WheelTalkOptions();
        configuration.GetSection(WheelTalkOptions.SectionName).Bind(options);

        services.AddSingleton(options);
        // Same instance, not a copy: decoders write their (B) reported settings back into it.
        services.AddSingleton<IWheelConfig>(options.WheelConfig);

        return services;
    }
}
