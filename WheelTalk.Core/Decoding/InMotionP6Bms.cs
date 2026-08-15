using Microsoft.Extensions.Logging;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Diagnostics;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// BMS InMotion P6 — что спросить и как разобрать ответ. Не порт: у <see cref="InMotionDecoderV2"/>
/// подкоманда батарей (<c>0x05</c>) распознаётся и отбрасывается для всей линейки, а адресных
/// запросов он не знает вовсе. Живёт эта работа снаружи порта, как и весь P6
/// (<see cref="InMotionDecoderV2_1"/>).
/// <para>
/// <b>Два разных источника, и оба нужны.</b> Периодическая сводка (подкоманда <c>0x05</c>, конверт
/// <c>0x14</c>) несёт ровно два слота по 8 байт: напряжение пака, два тока и слово флагов — ни
/// одной банки и ни одной температуры. Банки и температуры приходят только «прямыми» адресными
/// запросами по отдельному конверту <c>0x16</c>: селектор 1 — режим реального времени пака (там же
/// min/max банки, ёмкость, циклы, список температур), селектор 2 — список напряжений банок целиком.
/// Поэтому сводкой обойтись нельзя, и адресные запросы заведены тем же порядком, что и она.
/// </para>
/// <para>
/// <b>Всё здесь — по чтению стороннего клиента</b> (docs/originals-reference-data.md §8, разбор
/// LoEUC): ни одного кадра BMS в наших двух дампах P6 нет — приложение их не спрашивало, колесо их
/// само не шлёт. Числа не выдуманы, но и не подтверждены: живой приёмкой станет дамп этапа 0. Оттого
/// каждая проверка правдоподобия оставлена на месте, а короткий или неправдоподобный ответ — тишина,
/// а не половина показаний.
/// </para>
/// </summary>
internal sealed partial class InMotionP6Bms
{
    /// <summary>Шесть плат, которые перебирает оригинал. Какие из них отвечают на P6 — неизвестно,
    /// оттого разведка (см. <see cref="NextRequest"/>).</summary>
    private static readonly byte[] Addresses = [36, 37, 38, 39, 50, 52];

    /// <summary>Конверт сводки — общий для всей телеметрии; адресных запросов — свой, отдельный.
    /// По конверту их и различает <see cref="InMotionDecoderV2_1"/>: в адресном кадре на месте
    /// подкоманды стоит адрес платы.</summary>
    private const byte SummaryEnvelope = 0x14;
    public const byte DirectEnvelope = 0x16;

    /// <summary>Подкоманда сводки. У адресного запроса подкоманда 2 — тот же код, что несёт анонс
    /// <c>carType</c>, но по другому конверту: путать нельзя, различает их именно конверт.</summary>
    private const byte SummarySubcmd = 0x05;
    private const byte DirectSubcmd = 0x02;

    /// <summary>Селектор 1 — режим реального времени пака, 2 — список банок. Селектор 4 в оригинале
    /// назван допустимым, но нигде не разбирается: не спрашиваем то, чего не сумеем прочесть.</summary>
    private const byte SelectorRealtime = 1;
    private const byte SelectorCells = 2;

    /// <summary>Адрес приложения в ответе — единственный признак «это ответ нам».</summary>
    private const byte AppAddress = 2;

    /// <summary>Слот пака в сводке: 8 байт, слотов ровно два.</summary>
    private const int SlotSize = 8;

    /// <summary>Биты слова флагов сводки.</summary>
    private const int DetectedBit = 1 << 0;
    private const int FaultBits = 0x3FC0; // биты 6…13 — от ошибки логики до общей ошибки

    /// <summary>Минимум байт ответа селектора 1 и окно правдоподобия для напряжения пака —
    /// обе проверки оригинала, слово в слово.</summary>
    private const int RealtimeLength = 28;
    private const double PackVoltsLow = 50.0;
    private const double PackVoltsHigh = 300.0;

    /// <summary>Окно правдоподобия банки. Проверка «всё-или-ничего»: один выход за окно отбрасывает
    /// весь список — так у оригинала, и это разумно, потому что явной нумерации банок в ответе нет и
    /// проверить смещение больше нечем.</summary>
    private const double CellVoltsLow = 2.0;
    private const double CellVoltsHigh = 5.0;

    /// <summary>Температуры идут хвостом ответа селектора 1; в состояние их помещается шесть.</summary>
    private const int TempsAt = 28;
    private const double TempLow = -40.0;
    private const double TempHigh = 120.0;
    private const int TempSlots = 6;

    private readonly WheelState _state;
    private readonly ILogger _logger;

    /// <summary>Адреса, которые ответили. Пак определяется <b>позиционно по возрастанию адреса</b> —
    /// так же, как в оригинале, и это его догадка, а не поле протокола: явного номера пака в ответе
    /// нет. Отозвавшийся позже младший адрес сдвинет пары — оригинал живёт с тем же.</summary>
    private readonly SortedSet<byte> _answered = [];

    /// <summary>Слоты опроса чередуются: сводка, адресный запрос, сводка. Флаг переворачивается до
    /// решения, поэтому первым в круге уходит сводка — она одна покрывает оба пака.</summary>
    private bool _summarySlot;

    /// <summary>Разведка: сколько адресов уже спрошено хоть раз. Стоит шесть запросов на подключение
    /// и не повторяется — колесо, которое молчит на <c>0x16</c>, не должно платить за это вечно.</summary>
    private int _probed;

    /// <summary>Чередование внутри адресного слота, пока разведка не кончилась: шаг разведки, шаг к
    /// уже отозвавшемуся. Без него первый ответивший адрес ждал бы конца всего перебора.</summary>
    private bool _probeTurn = true;

    /// <summary>Указатель по кругу известных запросов.</summary>
    private int _knownStep;

    private readonly bool[] _faulted = new bool[2];
    private bool _overflowReported;

    public InMotionP6Bms(WheelState state, ILogger logger)
    {
        _state = state;
        _logger = logger;
    }

    // --- Запросы ---

    /// <summary>
    /// Что уходит в слоте BMS. Слоты чередуются: сводка, адресный запрос, сводка, адресный — сводка
    /// одна покрывает оба пака сразу и стоит одного кадра, адресные приходится перебирать.
    /// <para>
    /// Порядок адресных: пока не спрошены все шесть плат — через шаг разведка, через шаг опрос уже
    /// отозвавшихся; когда разведка кончилась — только отозвавшиеся, по два запроса на каждый
    /// (реальное время и банки). Не отозвался никто — адресный слот отдаётся сводке, и лишних
    /// кадров колесо больше не получает.
    /// </para>
    /// </summary>
    public byte[] NextRequest()
    {
        _summarySlot = !_summarySlot;
        return _summarySlot ? Summary() : Direct();
    }

    private byte[] Direct()
    {
        bool canProbe = _probed < Addresses.Length;
        bool canAsk = _answered.Count > 0;

        if (canProbe && canAsk) _probeTurn = !_probeTurn;
        else _probeTurn = canProbe;

        if (_probeTurn) return DirectRequest(Addresses[_probed++], SelectorRealtime);
        if (!canAsk) return Summary();

        // По два запроса на отозвавшийся адрес: чётный шаг — реальное время (там температуры и
        // границы банок), нечётный — полный список банок.
        byte[] known = [.. _answered];
        int step = _knownStep % (known.Length * 2);
        _knownStep = step + 1;
        return DirectRequest(known[step / 2], step % 2 == 0 ? SelectorRealtime : SelectorCells);
    }

    private static byte[] Summary() => Frame(SummaryEnvelope, SummarySubcmd);

    private static byte[] DirectRequest(byte address, byte selector) =>
        Frame(DirectEnvelope, DirectSubcmd, address, selector);

    /// <summary>
    /// Сборка кадра. Форма — та же, что у <see cref="InMotionV2Message.WriteBuffer"/>: заголовок
    /// <c>AA AA</c>, конверт, длина, подкоманда, данные, байт XOR. Повторена здесь намеренно: порт
    /// сверяется с оригиналом строка в строку, а ни сводки, ни адресных запросов в нём нет и по
    /// уговору не появится.
    /// </summary>
    private static byte[] Frame(byte envelope, byte subcmd, params byte[] data)
    {
        byte[] body = [envelope, (byte)(data.Length + 1), subcmd, .. data];

        byte check = 0;
        foreach (byte b in body) check ^= b;

        List<byte> wire = [0xAA, 0xAA];
        foreach (byte b in body)
        {
            if (b is 0xAA or 0xA5) wire.Add(0xA5);
            wire.Add(b);
        }
        wire.Add(check);
        return [.. wire];
    }

    // --- Разбор ---

    /// <summary>
    /// Периодическая сводка: два слота по 8 байт. <paramref name="payload"/> — данные кадра после
    /// байта подкоманды. Короче двух слотов — тишина: показать один пак из двух здесь означало бы
    /// показать половину батареи.
    /// </summary>
    public bool ApplySummary(byte[] payload)
    {
        if (payload.Length < 2 * SlotSize) return false;

        bool any = false;
        for (int slot = 0; slot < 2; slot++)
        {
            int at = slot * SlotSize;
            int flags = MathsUtil.ShortFromBytesLE(payload, at + 6);

            // Пак не обнаружен — у него нет ни напряжения, ни тока, и нули писать незачем: пустой
            // раздел на экране честнее раздела с нулями.
            if ((flags & DetectedBit) == 0) continue;

            var pack = slot == 0 ? _state.Bms1 : _state.Bms2;
            pack.Voltage = MathsUtil.ShortFromBytesLE(payload, at) / 100.0;

            // Токов в сводке два, а в состоянии поле одно. Разность — наша сборка, не поле
            // протокола: заряд уводит ток в минус, разряд держит в плюсе, как у прочих марок.
            double charge = MathsUtil.SignedShortFromBytesLE(payload, at + 2) / 100.0;
            double discharge = MathsUtil.SignedShortFromBytesLE(payload, at + 4) / 100.0;
            pack.Current = discharge - charge;

            ReportFault(slot, (flags & FaultBits) != 0);
            any = true;
        }

        return any;
    }

    /// <summary>
    /// Ответ на адресный запрос. <paramref name="frame"/> — кадр целиком, без экранирующих байт:
    /// <c>AA AA</c>, конверт <c>0x16</c>, длина, адрес платы, адрес получателя, селектор, данные,
    /// контрольная сумма.
    /// </summary>
    public bool ApplyDirect(byte[] frame)
    {
        // Заголовок (7 байт) плюс контрольная сумма — меньше этого читать нечего.
        if (frame.Length < 9) return false;

        byte source = frame[4];
        if (frame[5] != AppAddress) return false;
        if (Array.IndexOf(Addresses, source) < 0) return false;

        int selector = frame[6] & 0x1F;
        byte[] data = frame[7..^1];

        return selector switch
        {
            SelectorRealtime => ApplyRealtime(source, data),
            SelectorCells => ApplyCells(source, data),
            // Селектор 4 оригинал допускает и не разбирает. Мы его и не спрашиваем; пришёл сам —
            // молчим, а не гадаем.
            _ => false,
        };
    }

    private bool ApplyRealtime(byte source, byte[] data)
    {
        if (data.Length < RealtimeLength) return false;

        double voltage = MathsUtil.ShortFromBytesLE(data, 6) / 100.0;
        if (voltage is < PackVoltsLow or > PackVoltsHigh) return false;

        var pack = PackOf(source);
        if (pack is null) return false;

        pack.Voltage = voltage;

        double charge = MathsUtil.SignedShortFromBytesLE(data, 8) / 100.0;
        double discharge = MathsUtil.SignedShortFromBytesLE(data, 10) / 100.0;
        pack.Current = discharge - charge;

        // Ёмкости — как приходят, в мА·ч (у оригинала те же числа делятся на 1000 и зовутся А·ч).
        pack.FactoryCap = MathsUtil.ShortFromBytesLE(data, 12);
        pack.RemCap = MathsUtil.ShortFromBytesLE(data, 14);

        int cycles = MathsUtil.ShortFromBytesLE(data, 16);
        if (cycles > 0) pack.FullCycles = cycles;

        // Границы банок приходят и здесь, и списком по селектору 2. Список точнее (он же даёт
        // среднее и номера), но приходит реже — пока его нет, показать разброс уже есть чем.
        double max = MathsUtil.ShortFromBytesLE(data, 18) / 1000.0;
        double min = MathsUtil.ShortFromBytesLE(data, 20) / 1000.0;
        if (max is >= CellVoltsLow and <= CellVoltsHigh && min is >= CellVoltsLow and <= CellVoltsHigh && max >= min)
        {
            pack.MaxCell = max;
            pack.MinCell = min;
            pack.CellDiff = max - min;
        }

        ApplyTemps(data, pack);
        return true;
    }

    /// <summary>Температуры хвостом ответа: байт со сдвигом, окно правдоподобия — оригинала.
    /// Невероятное значение пропускается, а не обнуляет соседей: датчиков в хвосте больше, чем
    /// мест в состоянии, и негодные среди них — обычное дело.</summary>
    private static void ApplyTemps(byte[] data, SmartBms pack)
    {
        var temps = new List<double>(TempSlots);
        for (int at = TempsAt; at < data.Length && temps.Count < TempSlots; at++)
        {
            double temp = InMotionDecoderV2.DecodeTemperatureC(data[at]);
            if (temp is >= TempLow and <= TempHigh) temps.Add(temp);
        }

        if (temps.Count == 0) return;

        pack.Temp1 = At(temps, 0);
        pack.Temp2 = At(temps, 1);
        pack.Temp3 = At(temps, 2);
        pack.Temp4 = At(temps, 3);
        pack.Temp5 = At(temps, 4);
        pack.Temp6 = At(temps, 5);

        static double At(List<double> temps, int index) => index < temps.Count ? temps[index] : 0.0;
    }

    private bool ApplyCells(byte source, byte[] data)
    {
        double[]? cells = ReadCells(data);
        if (cells is null) return false;

        var pack = PackOf(source);
        if (pack is null) return false;

        double total = 0.0;
        double min = double.MaxValue;
        double max = 0.0;
        int minNum = 0;
        int maxNum = 0;

        for (int i = 0; i < pack.Cells.Length; i++)
        {
            // Хвост прошлого, более длинного ответа не должен пережить короткий: банка, о которой
            // сейчас не сказано, — ноль, а не позавчерашние вольты.
            double cell = i < cells.Length ? cells[i] : 0.0;
            pack.Cells[i] = cell;
            if (cell <= 0.0) continue;

            total += cell;
            if (cell > max) (max, maxNum) = (cell, i + 1);
            if (cell < min) (min, minNum) = (cell, i + 1);
        }

        pack.CellCount = cells.Length;
        pack.MaxCell = max;
        pack.MinCell = minNum == 0 ? 0.0 : min;
        pack.CellDiff = pack.MaxCell - pack.MinCell;
        pack.AvgCell = cells.Length > 0 ? total / cells.Length : 0.0;
        pack.MaxCellNum = maxNum;
        pack.MinCellNum = minNum;
        return true;
    }

    /// <summary>Список банок: пары байт подряд, всё-или-ничего. Лишнее сверх места в состоянии
    /// отбрасывается — но проверяется всё, иначе проверка перестала бы быть всё-или-ничего.</summary>
    private double[]? ReadCells(byte[] data)
    {
        int count = data.Length / 2;
        if (count == 0) return null;

        // Мест в состоянии столько же, сколько банок в паке P6, — 56, и у обоих паков поровну.
        var cells = new double[Math.Min(count, _state.Bms1.Cells.Length)];
        for (int i = 0; i < count; i++)
        {
            double cell = MathsUtil.ShortFromBytesLE(data, i * 2) / 1000.0;
            if (cell is < CellVoltsLow or > CellVoltsHigh) return null;
            if (i < cells.Length) cells[i] = cell;
        }

        return cells;
    }

    /// <summary>
    /// Какому паку принадлежит ответ. Явного номера в ответе нет — только адрес платы, и оригинал
    /// раскладывает их позиционно по возрастанию. Третий и дальше отозвавшийся адрес деть некуда:
    /// в состоянии два пака, и придумывать третий мы не станем.
    /// </summary>
    private SmartBms? PackOf(byte source)
    {
        if (_answered.Add(source)) LogBmsAddress(source);

        int index = 0;
        foreach (byte address in _answered)
        {
            if (address == source) break;
            index++;
        }

        if (index < 2) return index == 0 ? _state.Bms1 : _state.Bms2;

        if (!_overflowReported)
        {
            _overflowReported = true;
            LogBmsTooManyAddresses(_answered.Count);
        }
        return null;
    }

    /// <summary>Неисправность пака пишется на фронте: сводка приходит раз в несколько секунд, и
    /// строка на каждую из них превратила бы журнал в шум.</summary>
    private void ReportFault(int slot, bool faulted)
    {
        if (faulted == _faulted[slot]) return;
        _faulted[slot] = faulted;
        if (faulted) LogBmsFault(slot + 1);
    }

    [LoggerMessage(EventId = LogEvents.Decoding.ImV2P6BmsAddressId, EventName = LogEvents.Decoding.ImV2P6BmsAddressName,
        Level = LogLevel.Information, Message = "InMotion P6 BMS board {Address} answered a direct request")]
    private partial void LogBmsAddress(byte address);

    [LoggerMessage(EventId = LogEvents.Decoding.ImV2P6BmsFaultId, EventName = LogEvents.Decoding.ImV2P6BmsFaultName,
        Level = LogLevel.Warning, Message = "InMotion P6 BMS pack {Pack} reports a fault bit")]
    private partial void LogBmsFault(int pack);

    [LoggerMessage(EventId = LogEvents.Decoding.ImV2P6BmsTooManyAddressesId, EventName = LogEvents.Decoding.ImV2P6BmsTooManyAddressesName,
        Level = LogLevel.Warning, Message = "InMotion P6 answered from {Count} BMS boards — state holds two packs, the rest are dropped")]
    private partial void LogBmsTooManyAddresses(int count);
}
