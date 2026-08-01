namespace WheelTalk.Core.Ports;

/// <summary>
/// One BLE peripheral seen while scanning. <paramref name="Address"/> is the MAC in the
/// colon-separated form <see cref="ITransport.ConnectAsync"/> expects, so a scan result can be
/// handed straight back to connect. <paramref name="Name"/> is the advertised local name and is
/// empty until the peripheral announces one (it usually arrives in a separate scan-response
/// packet) — how to present a nameless device is the caller's decision.
/// </summary>
public sealed record DiscoveredDevice(string Name, string Address, int Rssi);
