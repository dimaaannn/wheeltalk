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
///   - P6 — в V2 не уходит только <c>RealTimeInfo</c>: его разбирает
///     <see cref="InMotionP6RealTime"/> по своей раскладке. Всё остальное — рукопожатие, серийник,
///     версии, статистика (в ней и одометр) — идёт в V2 нетронутым, это общая для всей V2 часть
///     протокола;
///   - модель неизвестна вовсе — <c>RealTimeInfo</c> отбрасывается, а телеметрия не показывается,
///     пока раскладка не известна.
/// </para>
/// <para>
/// Опрос колеса при этом не выключается ни в одном из случаев: таймер V2 продолжает просить
/// <c>RealTimeInfo</c>, и кадры продолжают приходить — их пишет сырой дамп (он снимает транспорт,
/// до декодера). Именно по такому дампу раскладка новой модели и восстанавливается.
/// </para>
/// </summary>
public sealed partial class InMotionDecoderV2_1 : IWheelDecoder, IDisposable
{
    /// <summary>Пара <c>series</c>/<c>type</c> модели P6 — единственное, чем колесо себя называет.</summary>
    private const int P6Series = 13;
    private const int P6Type = 1;

    /// <summary>Имя в стиле таблицы оригинала — панель показывает модели одинаково.</summary>
    private const string P6Name = "Inmotion P6";

    /// <summary>Куда уходит кадр телеметрии. Всё остальное всегда идёт в нетронутый V2.</summary>
    private enum Layout
    {
        /// <summary>Модель из таблицы оригинала — разбирает V2, как и раньше.</summary>
        Original,

        /// <summary>P6 — разбирает <see cref="InMotionP6RealTime"/>.</summary>
        P6,

        /// <summary>Модель не опознана — телеметрию не показываем вовсе.</summary>
        Unknown,
    }

    private readonly InMotionDecoderV2 _v2;
    private readonly InMotionV2Unpacker _unpacker;
    private readonly WheelState _state;
    private readonly ILogger<InMotionDecoderV2_1> _logger;

    /// <summary>Кадр как он пришёл с провода, со всеми экранирующими <c>0xA5</c>: в V2 отдаётся
    /// именно он, иначе распаковщик V2 не соберёт то же самое.</summary>
    private readonly List<byte> _wireFrame = [];
    private byte _previous;
    private bool _collecting;

    private Layout _layout = Layout.Unknown;

    public event Action<byte[]>? WriteRequested;

    public InMotionDecoderV2_1(WheelState state, IWheelConfig config, TimeProvider timeProvider, ILoggerFactory loggerFactory)
    {
        _state = state;
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

            newDataFound |= Dispatch(frame, _unpacker.GetBuffer());
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
    /// Единственное решение этого декодера — кому отдать собранный кадр. Разбирать его целиком
    /// незачем: нужны флаги, команда и, у кадра <c>carType</c>, пара series/type.
    /// <para>
    /// <paramref name="wireFrame"/> — байты как с провода, для V2; <paramref name="buffer"/> — тот
    /// же кадр без экранирующих <c>0xA5</c>, для нас и для <see cref="InMotionP6RealTime"/>.
    /// </para>
    /// </summary>
    private bool Dispatch(byte[] wireFrame, byte[] buffer)
    {
        // Битый кадр отдаём как есть: сообщить о несовпадении контрольной суммы — работа V2,
        // вторая такая же строка в журнале ничего не добавит. По той же причине заголовок читается
        // здесь по индексам, а не через InMotionV2Message.Verify — тот пишет ту самую строку.
        if (!ChecksumOk(buffer)) return _v2.Decode(wireFrame);

        int flags = buffer[2];
        int len = buffer[3];
        int command = buffer[4] & 0x7F;

        if (flags == (int)InMotionV2Message.Flag.Initial
            && command == (int)InMotionV2Message.Command.MainInfo
            && len >= 6 && buffer[5] == 0x01)
        {
            // Раскладка кадра — та же, что читает InMotionDecoderV2.DecodeMainInfo: данные
            // начинаются с buffer[5], series и type — третий и четвёртый байт данных.
            RememberModel(buffer[7], buffer[8]);
            bool decoded = _v2.Decode(wireFrame);

            // Имя ставится после V2 и поверх него: в таблице оригинала P6 нет, и V2 честно
            // напишет «Inmotion Unknown» — знать модель по имени наша забота, не его.
            if (_layout == Layout.P6) _state.SetModel(P6Name);
            return decoded;
        }

        if (command != (int)InMotionV2Message.Command.RealTimeInfo) return _v2.Decode(wireFrame);

        return _layout switch
        {
            Layout.Original => _v2.Decode(wireFrame),

            // Данные кадра — buffer[5..len+4]: распаковщик добирает ровно len + 5 байт (AA AA,
            // флаги, длина, len байт тела, контрольная сумма), поэтому срез всегда в границах.
            Layout.P6 => InMotionP6RealTime.Apply(buffer[5..(len + 4)], _state),

            _ => false,
        };
    }

    private void RememberModel(byte series, byte type)
    {
        var model = InMotionV2Models.FindById(series, type);
        _layout = model != InMotionV2Model.Unknown ? Layout.Original
            : series == P6Series && type == P6Type ? Layout.P6
            : Layout.Unknown;

        // Пара series/type — единственное, что называет колесо на том конце. Оригинал пишет её в
        // журнал (`findById`), и без неё неподдержанная модель выглядит как поломка декодера.
        LogCarType(series, type, _layout == Layout.P6 ? P6Name : model.DisplayName());
        if (_layout == Layout.Unknown) LogModelUnknown(series, type);
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
