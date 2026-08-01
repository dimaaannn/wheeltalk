namespace WheelTalk.Core.Contracts;

/// <summary>
/// Wire protocol the app talks — i.e. which <see cref="Decoding.IWheelDecoder"/> the composition
/// root builds (see <see cref="Decoding.WheelDecoderFactory"/>). Deliberately narrower than
/// <see cref="WheelType"/>: <see cref="WheelType"/> is the model family the wheel *reports* into
/// <see cref="TelemetrySnapshot"/> (written by a decoder), while this is the decoder the host
/// *selects* up front, before a single byte has arrived. Only protocols that have a decoder in
/// this port are listed; the remaining WheelLog protocols (Inmotion×2, Ninebot×2, Inmotion 24V)
/// get members when their decoders are ported.
/// </summary>
public enum WheelProtocol
{
    /// <summary>Veteran / Leaperkim (Sherman L, Abrams, Patton, Lynx, Oryx) — passive protocol.</summary>
    Veteran = 0,

    /// <summary>
    /// Gotway/Begode family (MTen3, …) — active protocol, the decoder polls "V"/"N" itself.
    /// </summary>
    Gotway = 1,

    /// <summary>
    /// Alias of <see cref="Gotway"/> — same protocol family under its current brand name, kept so
    /// <c>"Protocol": "Begode"</c> in appsettings.json binds (the docs and the wheel itself say
    /// "Begode", the WheelLog sources say "Gotway").
    /// </summary>
    Begode = Gotway,

    /// <summary>
    /// KingSong — active protocol, but the request/response loop lives inside the decoder itself
    /// (reacting to each decoded frame), not on a timer like <see cref="Gotway"/>'s handshake.
    /// </summary>
    KingSong = 2,

    /// <summary>
    /// InMotion (V5/V8/V10/Glide…) — active protocol: a 6-digit password is sent (six times) before
    /// the wheel answers, then the decoder polls on a 25 ms timer, same shape as <see cref="Gotway"/>'s
    /// handshake but running for the life of the connection rather than just at bootstrap.
    /// </summary>
    InMotion = 3,

    /// <summary>
    /// InMotion V2 (V9/V11/V11y/V12·HS·HT·PRO/V12S/V13·PRO/V14·s·g) — Nordic-UART-based successor
    /// to <see cref="InMotion"/>. Same active-protocol shape: multi-stage bootstrap, then continuous
    /// polling on a timer.
    /// </summary>
    InMotionV2 = 4,
}
