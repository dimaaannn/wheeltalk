using WheelTalk.Core.Contracts;

namespace WheelTalk.Configuration;

/// <summary>
/// Everything the app reads from appsettings.json (section "WheelTalk"), as one typed object
/// instead of loose <c>configuration["Protocol"]</c> string lookups scattered across the
/// composition root: which wheel to connect to, which protocol it speaks, and the decoder
/// behavior defaults.
///
/// One wheel and one protocol at a time — this is a manual test port, so both are edited by hand
/// between runs rather than discovered at runtime.
/// </summary>
public sealed class WheelTalkOptions
{
    /// <summary>appsettings.json section this binds from.</summary>
    public const string SectionName = "WheelTalk";

    /// <summary>
    /// Raw MAC of the wheel, as printed by the Scan scenario ("88:25:83:F2:1A:98"). Checked on
    /// connect (<see cref="Ble.WindowsBleClient.MacToAddress"/>), not here: Scan is how you obtain
    /// the MAC in the first place, and the offline replay never connects at all.
    /// </summary>
    public string WheelAddress { get; set; } = "";

    /// <summary>Protocol of the wheel at <see cref="WheelAddress"/> — picks the decoder.</summary>
    public WheelProtocol Protocol { get; set; } = WheelProtocol.Veteran;

    /// <summary>
    /// Decoder behavior defaults, exposed to Core as <see cref="Core.Ports.IWheelConfig"/>.
    /// The same instance is handed to the decoders, which write their (B) reported settings back
    /// into it at runtime.
    /// </summary>
    public AppWheelConfig WheelConfig { get; set; } = new();
}
