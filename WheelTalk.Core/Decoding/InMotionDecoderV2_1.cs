using Microsoft.Extensions.Logging;
using WheelTalk.Core.Diagnostics;
using WheelTalk.Core.Ports;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// InMotion V2-1 — не порт, а наша надстройка над <see cref="InMotionDecoderV2"/> для колёс,
/// которых нет в таблице <c>carType</c> оригинала. Первое такое колесо — InMotion P6 (02.08.2026);
/// поддержки этой модели нет и в самом WheelLog.
/// <para>
/// Зачем отдельный протокол. Оригинал уводит любое неопознанное колесо в
/// <c>parseRealTimeInfoV11</c> (ветка <c>else if (protoVer &lt; 2)</c>), и на колесе, которое не
/// V11, ШИМ, температуры и битовое поле ошибок читаются из посторонних байт. На P6 это дало
/// двадцать несуществующих аварий колеса, −176 °C и 63 % ШИМ на стоящем колесе — числа, которым
/// райдер может поверить. Правка внутри <see cref="InMotionDecoderV2"/> сделала бы порт неточным,
/// поэтому решение живёт снаружи: V2 не меняется ни строкой и остаётся эталоном сверки с
/// <c>InmotionAdapterV2.java</c>.
/// </para>
/// <para>
/// Как устроено. Кадры проходят через собственный распаковщик, который ищет ровно одно —
/// кадр <c>carType</c>, и по нему запоминает модель. Дальше всё зависит от неё:
///   - модель из таблицы оригинала (V9/V11/V11y/V12/V12S/V13/V14) — кадр уходит в V2 целиком,
///     байт в байт с провода, и разбирается ровно как раньше;
///   - модель неизвестна — в V2 не уходят только кадры <c>RealTimeInfo</c>. Рукопожатие,
///     серийник, версии и статистика разбираются как обычно (это настоящие данные, терять их
///     незачем), а телеметрия не показывается вовсе, пока раскладка модели не известна.
/// </para>
/// <para>
/// Опрос колеса при этом не выключается: таймер V2 продолжает просить <c>RealTimeInfo</c>, и кадры
/// продолжают приходить — их пишет сырой дамп (он снимает транспорт, до декодера). Именно по этому
/// дампу раскладка новой модели и восстанавливается.
/// </para>
/// </summary>
public sealed partial class InMotionDecoderV2_1 : IWheelDecoder, IDisposable
{
    private readonly InMotionDecoderV2 _v2;
    private readonly InMotionV2Unpacker _unpacker;
    private readonly ILogger<InMotionDecoderV2_1> _logger;

    /// <summary>Кадр как он пришёл с провода, со всеми экранирующими <c>0xA5</c>: в V2 отдаётся
    /// именно он, иначе распаковщик V2 не соберёт то же самое.</summary>
    private readonly List<byte> _wireFrame = [];
    private byte _previous;
    private bool _collecting;

    private InMotionV2Model _model = InMotionV2Model.Unknown;

    public event Action<byte[]>? WriteRequested;

    public InMotionDecoderV2_1(WheelState state, IWheelConfig config, TimeProvider timeProvider, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<InMotionDecoderV2_1>();
        _unpacker = new InMotionV2Unpacker(loggerFactory.CreateLogger<InMotionDecoderV2>());
        _v2 = new InMotionDecoderV2(state, config, timeProvider, loggerFactory.CreateLogger<InMotionDecoderV2>());
        _v2.WriteRequested += bytes => WriteRequested?.Invoke(bytes);
    }

    public bool IsReady => _v2.IsReady;

    public bool Decode(byte[] data)
    {
        bool newDataFound = false;

        foreach (byte c in data)
        {
            TrackWire(c);
            if (!_unpacker.AddChar(c)) continue;

            byte[] frame = [.. _wireFrame];
            _previous = 0;
            _collecting = false;

            if (PassesToV2(_unpacker.GetBuffer())) newDataFound |= _v2.Decode(frame);
        }

        return newDataFound;
    }

    /// <summary>
    /// Копит байты кадра параллельно распаковщику. Границу кадра ищем по тому же признаку, что и
    /// он (<c>AA AA</c> и завершение по длине), но байты не разэкранируем: в V2 кадр обязан уйти
    /// таким же, каким пришёл.
    /// </summary>
    private void TrackWire(byte c)
    {
        if (c == 0xAA && _previous == 0xAA)
        {
            _wireFrame.Clear();
            _wireFrame.Add(0xAA);
            _wireFrame.Add(0xAA);
            _collecting = true;
        }
        else if (_collecting)
        {
            _wireFrame.Add(c);
        }

        _previous = c;
    }

    /// <summary>
    /// Единственное решение этого декодера. Разбирать нечего — нужны только флаги, команда и, у
    /// кадра <c>carType</c>, пара series/type.
    /// </summary>
    private bool PassesToV2(byte[] buffer)
    {
        // Битый кадр отдаём как есть: сообщить о несовпадении контрольной суммы — работа V2,
        // вторая такая же строка в журнале ничего не добавит.
        if (!ChecksumOk(buffer)) return true;

        int flags = buffer[2];
        int len = buffer[3];
        int command = buffer[4] & 0x7F;

        if (flags == (int)InMotionV2Message.Flag.Initial
            && command == (int)InMotionV2Message.Command.MainInfo
            && len >= 6 && buffer[5] == 0x01)
        {
            // Раскладка кадра — та же, что читает InMotionDecoderV2.DecodeMainInfo: данные
            // начинаются с buffer[5], series и type — третий и четвёртый байт данных.
            int series = buffer[7];
            int type = buffer[8];
            _model = InMotionV2Models.FindById(series, type);

            // Пара series/type — единственное, что называет колесо на том конце. Оригинал пишет её
            // в журнал (`findById`), и без неё неподдержанная модель выглядит как поломка декодера.
            LogCarType(series, type, _model.DisplayName());
            if (_model == InMotionV2Model.Unknown) LogModelUnknown(series, type);
            return true;
        }

        return _model != InMotionV2Model.Unknown
            || command != (int)InMotionV2Message.Command.RealTimeInfo;
    }

    private static bool ChecksumOk(byte[] buffer)
    {
        int check = 0;
        for (int i = 0; i < buffer.Length - 1; i++) check ^= buffer[i];
        return (byte)check == buffer[^1];
    }

    public byte[] BuildWheelBeep() => _v2.BuildWheelBeep();

    public byte[] BuildSetLightState(bool enabled) => _v2.BuildSetLightState(enabled);

    public byte[] BuildSwitchFlashlight() => _v2.BuildSwitchFlashlight();

    public byte[]? BuildUpdatePedalsMode(int mode) => _v2.BuildUpdatePedalsMode(mode);

    public byte[]? BuildResetTrip() => _v2.BuildResetTrip();

    public byte[]? BuildCalibrate() => _v2.BuildCalibrate();

    public void Dispose() => _v2.Dispose();

    [LoggerMessage(EventId = LogEvents.Decoding.ImV2CarTypeId, EventName = LogEvents.Decoding.ImV2CarTypeName,
        Level = LogLevel.Information, Message = "InMotion car type {Series}, {Type} — {Model}")]
    private partial void LogCarType(int series, int type, string model);

    [LoggerMessage(EventId = LogEvents.Decoding.ImV2ModelUnknownId, EventName = LogEvents.Decoding.ImV2ModelUnknownName,
        Level = LogLevel.Warning, Message = "InMotion model {Series}, {Type} is not in the carType table — real-time telemetry left undecoded")]
    private partial void LogModelUnknown(int series, int type);
}
