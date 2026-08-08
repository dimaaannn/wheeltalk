using WheelTalk.Core.Ports;

namespace WheelTalk.Tests.TestSupport;

/// <summary>
/// A transport whose behaviour the test decides: whether connecting works, when frames arrive and
/// when the link drops. Stands in for a wheel that cannot be switched on and off from a test.
/// </summary>
public sealed class FakeTransport : ITransport
{
    private readonly List<byte[]> _written = [];

    public event Action<byte[]>? DataReceived;
    public event Action? ConnectionLost;

    /// <summary>When true, every ConnectAsync throws — a wheel that is switched off.</summary>
    public bool RefuseConnections { get; set; }

    /// <summary>When set, every WriteAsync throws this instead of completing — a dead link.</summary>
    public Exception? FailWritesWith { get; set; }

    public int ConnectAttempts { get; private set; }

    /// <summary>Сколько из попыток пришло пассивным ожиданием (<see cref="WaitForWheelAsync"/>).</summary>
    public int PassiveWaits { get; private set; }

    public bool IsConnected { get; private set; }

    public IReadOnlyList<byte[]> Written => _written;

    public IAsyncEnumerable<DiscoveredDevice> ScanAsync(CancellationToken ct = default) =>
        AsyncEnumerable.Empty<DiscoveredDevice>();

    /// <summary>
    /// Дерево служб, которое транспорт «нашёл». По умолчанию пустое — как у реплея: сессия тогда
    /// не опознаёт семейство и полагается на заголовки кадров. Тест, которому нужна детекция,
    /// подставляет сюда своё.
    /// </summary>
    public IReadOnlyList<DiscoveredService> Services { get; set; } = [];

    public Task<IReadOnlyList<DiscoveredService>> ConnectAsync(string address, CancellationToken ct = default)
    {
        ConnectAttempts++;
        if (RefuseConnections)
        {
            return Task.FromException<IReadOnlyList<DiscoveredService>>(
                new InvalidOperationException($"{address} is not answering"));
        }

        IsConnected = true;
        return Task.FromResult(Services);
    }

    /// <summary>
    /// Пассивного режима у фейка нет — как у Windows-транспорта: считается и уходит в обычный
    /// <see cref="ConnectAsync"/>, чтобы тесты видели, каким способом сессия просила подключение.
    /// </summary>
    public Task<IReadOnlyList<DiscoveredService>> WaitForWheelAsync(string address, CancellationToken ct = default)
    {
        PassiveWaits++;
        return ConnectAsync(address, ct);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task WriteAsync(byte[] cmd, CancellationToken ct = default)
    {
        if (FailWritesWith is { } failure) return Task.FromException(failure);

        _written.Add(cmd);
        return Task.CompletedTask;
    }

    /// <summary>Delivers a frame exactly as a BLE notification would.</summary>
    public void Deliver(params string[] hexFrames)
    {
        foreach (string frame in hexFrames)
        {
            DataReceived?.Invoke(Convert.FromHexString(frame));
        }
    }

    /// <summary>The wheel went away on its own — switched off, or out of range.</summary>
    public void DropLink()
    {
        IsConnected = false;
        ConnectionLost?.Invoke();
    }
}
