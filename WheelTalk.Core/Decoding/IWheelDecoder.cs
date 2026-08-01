namespace WheelTalk.Core.Decoding;

/// <summary>
/// Contract every protocol decoder (VeteranDecoder, GotwayDecoder, …) implements, so
/// <see cref="Services.Decoder"/> and <see cref="Services.WheelService"/> stay protocol-agnostic.
/// Mirrors Android's BaseAdapter (decode/isReady + the command builders), minus the
/// singleton/WheelData coupling — state lives in the caller-owned <see cref="WheelState"/>.
/// </summary>
public interface IWheelDecoder
{
    bool Decode(byte[] data);
    bool IsReady { get; }

    /// <summary>
    /// Raised for writes the decoder needs to make on its own initiative — not in direct
    /// response to a WheelCommand — e.g. Begode's "V"/"N" handshake polling, or the delayed
    /// second half of a two-step command (Gotway's calibrate "c" then "y" 300ms later).
    /// Never raised by VeteranDecoder (purely passive protocol).
    /// </summary>
    event Action<byte[]>? WriteRequested;

    byte[] BuildWheelBeep();
    byte[] BuildSetLightState(bool enabled);
    byte[] BuildSwitchFlashlight();
    byte[]? BuildUpdatePedalsMode(int mode);
    byte[]? BuildResetTrip();
    byte[]? BuildCalibrate();
}
