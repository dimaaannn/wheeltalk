using Microsoft.Extensions.Logging;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Diagnostics;
using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Begode или Veteran — решает первый же кадр, а не человек. Порт `GotwayVirtualAdapter`
/// оригинала: у этих двух протоколов общий профиль `FFE0`/`FFE1`, по дереву GATT они неразличимы
/// в принципе, и единственное, чем они отличаются с самого начала, — заголовок кадра.
/// <code>
/// DC 5A 5C … → Veteran
/// 55 AA …    → Begode/Gotway
/// AA 55 …    → KingSong
/// иначе      → ещё не знаем, ждём следующий кадр
/// </code>
/// <para>
/// До первого узнанного кадра декодера внутри нет, и это честное состояние, а не ошибка: колесо
/// молчит примерно долю секунды после подписки на уведомления. Команды в это время строить не из
/// чего — <see cref="BuildWheelBeep"/> и остальные бросают
/// <see cref="ProtocolNotDetectedException"/>, а не гадают.
/// </para>
/// <para>
/// Работает и на записанной поездке: в дампе те же кадры, что были в эфире, поэтому реплею тоже не
/// нужно спрашивать протокол снаружи.
/// </para>
/// </summary>
public sealed partial class AutoDecoder : IWheelDecoder
{
    private readonly WheelState _state;
    private readonly IWheelConfig _config;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AutoDecoder> _logger;

    private IWheelDecoder? _inner;

    public AutoDecoder(WheelState state, IWheelConfig config, TimeProvider timeProvider, ILoggerFactory loggerFactory)
    {
        _state = state;
        _config = config;
        _timeProvider = timeProvider;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<AutoDecoder>();
    }

    /// <summary>Протокол, если он уже опознан. До первого кадра — <c>null</c>.</summary>
    public WheelProtocol? Protocol { get; private set; }

    /// <summary>Поднимается один раз, когда протокол опознан: сессии надо знать, с чем она говорит.</summary>
    public event Action<WheelProtocol>? Detected;

    public bool IsReady => _inner?.IsReady ?? false;

    public event Action<byte[]>? WriteRequested;

    public bool Decode(byte[] data)
    {
        if (_inner is null)
        {
            if (Recognise(data) is not { } protocol) return false;

            Protocol = protocol;
            _inner = WheelDecoderFactory.Create(protocol, _state, _config, _timeProvider, _loggerFactory);

            // Подписка переносится на настоящий декодер: двухступенчатые команды Gotway и его же
            // опрос «V»/«N» идут именно оттуда, и без этого они бы молча пропали.
            _inner.WriteRequested += OnInnerWriteRequested;

            LogDetected(protocol);
            Detected?.Invoke(protocol);
        }

        return _inner.Decode(data);
    }

    /// <summary>
    /// Заголовок кадра — точно как у оригинала, по началу пришедшего пакета. Кадр приходит целиком
    /// в первом уведомлении, поэтому искать заголовок внутри буфера не нужно: этим занимаются
    /// распаковщики уже внутри выбранного декодера.
    /// </summary>
    private static WheelProtocol? Recognise(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xDC && data[1] == 0x5A && data[2] == 0x5C) return WheelProtocol.Veteran;
        if (data.Length >= 2 && data[0] == 0x55 && data[1] == 0xAA) return WheelProtocol.Gotway;
        if (data.Length >= 2 && data[0] == 0xAA && data[1] == 0x55) return WheelProtocol.KingSong;

        return null;
    }

    private void OnInnerWriteRequested(byte[] bytes) => WriteRequested?.Invoke(bytes);

    private IWheelDecoder Ready => _inner ?? throw new ProtocolNotDetectedException();

    public byte[] BuildWheelBeep() => Ready.BuildWheelBeep();
    public byte[] BuildSetLightState(bool enabled) => Ready.BuildSetLightState(enabled);
    public byte[] BuildSwitchFlashlight() => Ready.BuildSwitchFlashlight();
    public byte[]? BuildUpdatePedalsMode(int mode) => Ready.BuildUpdatePedalsMode(mode);
    public byte[]? BuildResetTrip() => Ready.BuildResetTrip();
    public byte[]? BuildCalibrate() => Ready.BuildCalibrate();

    [LoggerMessage(EventId = LogEvents.Service.ProtocolSelectedId, EventName = "Protocol.Detected",
        Level = LogLevel.Information, Message = "Protocol.Detected {Protocol} — по заголовку первого кадра")]
    private partial void LogDetected(WheelProtocol protocol);
}

/// <summary>
/// Команду попросили раньше, чем колесо сказало хоть слово. Своим типом, а не общим отказом: это
/// не поломка и не отсутствие связи, а «подождите долю секунды» — и на экране оно должно читаться
/// именно так.
/// </summary>
public sealed class ProtocolNotDetectedException()
    : InvalidOperationException("Протокол ещё не опознан — колесо не прислало ни одного кадра");
