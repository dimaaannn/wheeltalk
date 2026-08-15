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
///   - P6 — в V2 не уходят <c>RealTimeInfo</c> (его разбирает <see cref="InMotionP6RealTime"/> по
///     своей раскладке), диагностика и BMS (<see cref="InMotionP6Bms"/> — сводка паков и ответы на
///     адресные запросы). Всё остальное — рукопожатие, серийник, версии, статистика (в ней и
///     одометр) — идёт в V2 нетронутым, это общая для всей V2 часть протокола;
///   - модель неизвестна вовсе — <c>RealTimeInfo</c> отбрасывается, а телеметрия не показывается,
///     пока раскладка не известна.
/// </para>
/// <para>
/// Опрос колеса при этом не выключается ни в одном из случаев: надстройка продолжает просить
/// <c>RealTimeInfo</c>, и кадры продолжают приходить — их пишет сырой дамп (он снимает транспорт,
/// до декодера). Именно по такому дампу раскладка новой модели и восстанавливается.
/// </para>
/// <para>
/// <b>Опрос ведёт надстройка</b> (план 36 Л3, мастер-план §8). У порта он ответозависимый: тик
/// 25 мс, а счётчик обнуляется на каждом принятом кадре (<c>InMotionDecoderV2.cs:93-95,154</c>) —
/// то есть чем быстрее отвечает колесо, тем чаще мы его спрашиваем, 16–40 сообщений в секунду
/// против 1–2 у производителя и 4–5 у DarknessBot. Здесь опрос <b>времязависимый</b>: приём кадра
/// не запускает ничего, следующий запрос уходит по своему сроку — см. <see cref="Poll"/>. Порт при
/// этом не тронут ни знаком: ему отданы часы без таймеров
/// (<see cref="Ports.TimerlessTimeProvider"/>), и его собственный опрос молчит.
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

    /// <summary>Ступени лестницы опроса — те же и в том же порядке, что у порта
    /// (<c>InMotionDecoderV2.OnKeepAliveTick</c>): пока колесо себя не назвало, телеметрию просить
    /// не о чем.</summary>
    private const int StageCarType = 0;
    private const int StageSerial = 1;
    private const int StageVersions = 2;
    private const int StageSettings = 3;
    private const int StageUselessData = 4;
    private const int StageCycle = 5;

    /// <summary>
    /// Круг установившегося опроса: телеметрия, одометр, телеметрия. Ровно так устроен круг
    /// DarknessBot (телеметрия дважды за круг, общий пробег однажды), и это <b>неразделимая часть</b>
    /// правки: у порта телеметрия и статистика чередуются один к одному, и если сменить только
    /// принцип, телеметрия упала бы вдвое против нынешнего (мастер-план §8.2). При заводских 250 мс
    /// круг занимает 750 мс: 4 запроса в секунду, из них телеметрия — 2,7.
    /// </summary>
    private const int CycleLength = 3;
    private const int CycleStatsStep = 1;

    /// <summary>
    /// У P6 круг вдвое длиннее, и лишний шаг взят <b>не у телеметрии</b>: в каждом втором круге
    /// место одометра занимает запрос BMS. Телеметрия остаётся ровно там же, где была, — две трети
    /// всех запросов, 2,7 в секунду при заводских 250 мс; платит за BMS общий пробег, шаг которого
    /// 10 метров и которому 1,3 опроса в секунду не нужны никогда.
    /// <para>
    /// Отчего именно каждый второй круг. Слот BMS чередует сводку и один адресный запрос
    /// (<see cref="InMotionP6Bms.NextRequest"/>), то есть при 250 мс сводка приходит раз в 3 с, а
    /// оригинал повторяет её раз в секунду — мы медленнее втрое, и это осознанно: напряжения паков и
    /// банок за секунду не меняются, а каждый лишний слот отнимается у чего-то живого. Верхняя
    /// граница шага (1000 мс) растягивает круг до 12 с — для банок это по-прежнему часто.
    /// </para>
    /// </summary>
    private const int P6CycleLength = CycleLength * 2;
    private const int P6CycleBmsStep = CycleLength + CycleStatsStep;

    /// <summary>Границы шага опроса — как у LoEUC, чей период тоже настройка. Ниже 250 мс начинается
    /// наша же прежняя болезнь, выше 1000 показания становятся ступенчатыми.</summary>
    private const int MinPollPeriodMs = 250;
    private const int MaxPollPeriodMs = 1000;

    /// <summary>Пауза до первого запроса: столько же ждёт порт и столько же — круг DarknessBot.</summary>
    private static readonly TimeSpan FirstRequestDelay = TimeSpan.FromMilliseconds(100);

    private readonly InMotionDecoderV2 _v2;
    private readonly InMotionV2Unpacker _unpacker;
    private readonly WheelState _state;
    private readonly IWheelConfig _config;
    private readonly ILogger<InMotionDecoderV2_1> _logger;
    private readonly ITimer _pollTimer;

    /// <summary>BMS P6 — свои запросы и свой разбор. Заводится всегда, а спрашивается только у P6:
    /// решение принимает <see cref="NextRequest"/>, а не сам объект.</summary>
    private readonly InMotionP6Bms _bms;

    /// <summary>Ступень лестницы; дошла до <see cref="StageCycle"/> — дальше круг без конца.</summary>
    private int _stage;
    private int _cycleStep;
    private bool _carTypeAnswered;

    /// <summary>Кадр как он пришёл с провода, со всеми экранирующими <c>0xA5</c>: в V2 отдаётся
    /// именно он, иначе распаковщик V2 не соберёт то же самое.</summary>
    private readonly List<byte> _wireFrame = [];
    private byte _previous;
    private bool _collecting;

    private Layout _layout = Layout.Unknown;

    public event Action<byte[]>? WriteRequested;

    /// <summary>
    /// Raised straight off this decoder's own checksum check (<see cref="ChecksumOk"/>) — not
    /// forwarded from <see cref="_v2"/>. A recognised frame here is decided once, on the same bytes
    /// <see cref="_v2"/> would re-verify anyway, so forwarding both would double-count every frame
    /// this wrapper hands to it (the <see cref="Layout.Original"/> and <c>carType</c> paths).
    /// </summary>
    public event Action<byte[]>? FrameRecognized;

    public InMotionDecoderV2_1(WheelState state, IWheelConfig config, TimeProvider timeProvider, ILoggerFactory loggerFactory)
    {
        _state = state;
        _config = config;
        _logger = loggerFactory.CreateLogger<InMotionDecoderV2_1>();
        _unpacker = new InMotionV2Unpacker(loggerFactory.CreateLogger<InMotionDecoderV2>());
        // Своей категории журнала у BMS нет намеренно: он часть этой надстройки и виден в журнале
        // как она — тем же порядком, каким распаковщик делит категорию с владеющим декодером.
        _bms = new InMotionP6Bms(state, _logger);

        // Часы без таймеров: опрос порта молчит, расписание ведёт эта надстройка (см. doc класса).
        // Команды порт по-прежнему строит и шлёт, поэтому подписка на его записи остаётся.
        _v2 = new InMotionDecoderV2(state, config, new TimerlessTimeProvider(timeProvider),
            loggerFactory.CreateLogger<InMotionDecoderV2>());
        _v2.WriteRequested += bytes => WriteRequested?.Invoke(bytes);

        // Часы опроса. Шаг берётся из настройки в этот момент и держится всё подключение: правка
        // настройки действует со следующего разговора с колесом.
        _pollTimer = timeProvider.CreateTimer(_ => Poll(), null, FirstRequestDelay, PollPeriod);
    }

    /// <summary>
    /// Шаг опроса: <b>ровно один запрос</b>, что бы ни происходило на приёме. Здесь и держится вся
    /// правка — отправку заводит таймер, а не кадр. Ответ колеса двигает ступень лестницы
    /// (<see cref="NextRequest"/>), но темпа коснуться не может: сколько бы кадров ни пришло между
    /// тиками, запрос уйдёт один.
    /// </summary>
    private void Poll() => RequestWrite(NextRequest());

    /// <summary>Шаг опроса из настройки, приведённый к разумным границам.</summary>
    private TimeSpan PollPeriod =>
        TimeSpan.FromMilliseconds(Math.Clamp(_config.InMotionPollPeriodMs, MinPollPeriodMs, MaxPollPeriodMs));

    /// <summary>
    /// Что спросить на этом шаге. Лестница — из порта, ступень в ступень: тип колеса и серийник
    /// переспрашиваются, пока колесо не ответит (без них раскладка телеметрии неизвестна), версии,
    /// настройки и «бесполезные данные» спрашиваются по разу. Дальше расходимся: у порта телеметрия
    /// и статистика чередуются один к одному, здесь — круг DarknessBot, где одометр спрашивается
    /// однажды за круг (<see cref="CycleLength"/>). У P6 круг вдвое длиннее, и в каждом втором место
    /// одометра занимает BMS (<see cref="P6CycleLength"/>).
    /// <para>
    /// Ступень двигает ответ колеса, и это не возврат ответозависимости: от ответа зависит
    /// <b>что</b> спросят, а <b>когда</b> — только от таймера. Проверяется замком
    /// <c>InMotionPollTests</c>: два прогона по виртуальному времени, с ответами и без, дают одно и
    /// то же число исходящих.
    /// </para>
    /// </summary>
    private byte[] NextRequest()
    {
        if (_stage == StageCarType && _carTypeAnswered) _stage = StageSerial;
        if (_stage == StageSerial && _state.Serial.Length > 0) _stage = StageVersions;

        switch (_stage)
        {
            case StageCarType:
                return InMotionV2Message.GetCarType().WriteBuffer();
            case StageSerial:
                return InMotionV2Message.GetSerialNumber().WriteBuffer();
            case StageVersions:
                _stage = StageSettings;
                return InMotionV2Message.GetVersions().WriteBuffer();
            case StageSettings:
                _stage = StageUselessData;
                return InMotionV2Message.GetCurrentSettings().WriteBuffer();
            case StageUselessData:
                _stage = StageCycle;
                return InMotionV2Message.GetUselessData().WriteBuffer();
            default:
                int step = _cycleStep;
                // Круг P6 длиннее на один оборот, и лишний его шаг — BMS. У прочих моделей длина
                // круга и его состав те же, что были: BMS-запросов они не видят вовсе.
                _cycleStep = (step + 1) % (_layout == Layout.P6 ? P6CycleLength : CycleLength);

                if (step % CycleLength != CycleStatsStep) return InMotionV2Message.GetRealTimeData().WriteBuffer();
                if (step == P6CycleBmsStep && _layout == Layout.P6) return _bms.NextRequest();
                return InMotionV2Message.GetStatistics().WriteBuffer();
        }
    }

    private void RequestWrite(byte[] bytes) => WriteRequested?.Invoke(bytes);

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

        // Header, length and checksum all check out here — recognised, whatever the model or the
        // command turn out to be. InMotion P6's carType frame (series 13/type 1, not in the
        // original's table) and its RealTimeInfo both pass this line; that is the point of it.
        FrameRecognized?.Invoke(buffer);

        int flags = buffer[2];
        int len = buffer[3];
        int command = buffer[4] & 0x7F;

        // Ответ на адресный запрос BMS: свой конверт, и в нём на месте подкоманды стоит адрес
        // платы, а не команда — читать этот кадр общими правилами нельзя. Порт о таком конверте не
        // знает и молча его роняет; здесь он идёт в разбор, но только у P6, потому что только P6 мы
        // о нём и спрашиваем.
        if (flags == InMotionP6Bms.DirectEnvelope)
        {
            return _layout == Layout.P6 && _bms.ApplyDirect(buffer);
        }

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

        // Диагностику (subcmd 3) V2 распознаёт и отбрасывает для всей линейки — у остальных моделей
        // тревоги идут из битов RealTimeInfo (InMotionDecoderV2.GetError). У P6 такого обхода нет,
        // поэтому только для него подкоманда уходит в свой разбор; прочим моделям и порту это не
        // трогаем.
        if (command == (int)InMotionV2Message.Command.Diagnostic && _layout == Layout.P6)
        {
            return DecodeP6Diagnostics(buffer[5..(len + 4)]);
        }

        // Сводка BMS. Порт распознаёт её и возвращает false для всей линейки: у оригинала она
        // сохраняется только при carType 131, и P6 в его таблице нет. Наш разбор — по тому же
        // условию: сводка только у P6, прочим моделям — прежнее поведение порта.
        if (command == (int)InMotionV2Message.Command.BatteryRealTimeInfo && _layout == Layout.P6)
        {
            return _bms.ApplySummary(buffer[5..(len + 4)]);
        }

        if (command != (int)InMotionV2Message.Command.RealTimeInfo) return _v2.Decode(wireFrame);

        return _layout switch
        {
            Layout.Original => _v2.Decode(wireFrame),

            // Данные кадра — buffer[5..len+4]: распаковщик добирает ровно len + 5 байт (AA AA,
            // флаги, длина, len байт тела, контрольная сумма), поэтому срез всегда в границах.
            Layout.P6 => InMotionP6RealTime.Apply(buffer[5..(len + 4)], _state, _config),

            _ => false,
        };
    }

    /// <summary>Пишет слова тревоги по раскладке <see cref="InMotionP6DiagnosticFlags"/>; пустая
    /// подкоманда — пустая строка, тишина, а не молчаливое сохранение старой тревоги.</summary>
    private bool DecodeP6Diagnostics(byte[] data)
    {
        var result = InMotionP6Diagnostics.Decode(data);
        _state.SetAlert(result.AlertText);
        if (result.HasUnknownBit) LogP6DiagnosticUnknownBit();
        return true;
    }

    private void RememberModel(byte series, byte type)
    {
        // Колесо назвалось — лестнице опроса есть куда шагнуть. Сам шаг делает NextRequest, и
        // делает его по таймеру: ответ решает, что спросят, но не когда.
        _carTypeAnswered = true;

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

    public void Dispose()
    {
        _pollTimer.Dispose();
        _v2.Dispose();
    }

    [LoggerMessage(EventId = LogEvents.Decoding.ImV2CarTypeId, EventName = LogEvents.Decoding.ImV2CarTypeName,
        Level = LogLevel.Information, Message = "InMotion car type {Series}, {Type} — {Model}")]
    private partial void LogCarType(int series, int type, string model);

    [LoggerMessage(EventId = LogEvents.Decoding.ImV2ModelUnknownId, EventName = LogEvents.Decoding.ImV2ModelUnknownName,
        Level = LogLevel.Warning, Message = "InMotion model {Series}, {Type} is not in the carType table — real-time telemetry left undecoded")]
    private partial void LogModelUnknown(int series, int type);

    [LoggerMessage(EventId = LogEvents.Decoding.ImV2P6DiagnosticUnknownBitId, EventName = LogEvents.Decoding.ImV2P6DiagnosticUnknownBitName,
        Level = LogLevel.Warning, Message = "InMotion P6 diagnostic frame set a bit beyond the proven 45 flags")]
    private partial void LogP6DiagnosticUnknownBit();
}
