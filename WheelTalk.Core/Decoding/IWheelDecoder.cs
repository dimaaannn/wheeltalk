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

    /// <summary>
    /// Raised the moment bytes are recognised as a frame of <b>this</b> protocol — header, length
    /// and checksum (where the protocol has one) all check out — regardless of whether the frame's
    /// content turns out to say anything the decoder understands. This is what
    /// <see cref="Services.WheelSession"/>'s connection watchdog feeds on: it answers "is the wheel
    /// still talking our protocol", a narrower question than "did we get any bytes at all" (noise
    /// from a confused module answers that too) and a wider one than "did we get a snapshot"
    /// (InMotion P6's <c>carType</c> frame answers this without ever producing one). See
    /// bugfix-1-reconnect.md §1.1 for the case that forced the distinction.
    /// </summary>
    event Action<byte[]>? FrameRecognized;

    byte[] BuildWheelBeep();
    byte[] BuildSetLightState(bool enabled);
    byte[] BuildSwitchFlashlight();
    byte[]? BuildUpdatePedalsMode(int mode);
    byte[]? BuildResetTrip();
    byte[]? BuildCalibrate();
}
