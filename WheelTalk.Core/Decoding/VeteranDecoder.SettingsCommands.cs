using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Hashing;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Запись настроек LeaperKim/Nosfet — вторая половина <see cref="VeteranDecoder"/>, та, которой у
/// WheelLog нет. Соседний файл <c>VeteranDecoder.cs</c> этой работой <b>не тронут ни строкой</b>:
/// там порт <c>VeteranAdapter.java</c> 1:1, и он остаётся сверяемым построчно. Здесь — байты
/// родного приложения производителя (<c>com.laoniao.leaperkim</c> 1.4.8, декомпилят
/// <c>C:\Work\repos\loeuc\src_leaper</c>, разбор — <c>loeuc/leaperkim-official-app.md</c>).
/// </summary>
public sealed partial class VeteranDecoder : IVeteranSettingsCommands
{
    /// <summary>Заголовок «нового» кадра <c>LdAp</c> — одиночная запись настройки.</summary>
    private static ReadOnlySpan<byte> LdHeader => [0x4C, 0x64, 0x41, 0x70];

    /// <summary>Заголовок «старого» кадра <c>LkAp</c> — первая половина парных команд.</summary>
    private static ReadOnlySpan<byte> LkHeader => [0x4C, 0x6B, 0x41, 0x70];

    /// <summary>Универсальный заполнитель тела кадра (<c>Byte.MIN_VALUE</c> у производителя). Он же
    /// служит колесу сентинелом «этой настройки у меня нет» во входящей странице настроек
    /// (<c>leaperkim-official-app.md</c> §5.2) — потому и не может быть законным значением.</summary>
    private const byte Filler = 0x80;

    /// <summary>Байт 6 = 2 — вторая половина признака «запись обычной настройки». Признак этот —
    /// <b>пара</b> (b5=1, b6=2), а не один байт: она стоит у шестнадцати одиночных кадров записи, а
    /// правило «b6=2 значит запись» ломается на чужом кадре сброса поездки, где b6=2 при b5=0
    /// (<c>CMD_CLEAR_METER_NEW</c>, <c>BtManager.java:85</c>). Обе половины признака зашиты в общем
    /// сборщике константами, и ровно ими настройка отличается от чужой команды на том же опкоде:
    /// чтение журнала на 20-м, тревога скорости на 17-м, синхронизация времени на 18-м, выключение
    /// колеса и угол защиты от падения на 22-м (<c>leaperkim-official-app.md</c> §4.2).</summary>
    private const byte SettingWriteMarker = 2;

    /// <summary>Версия протокола, вычитанная из телеметрии (<c>VeteranDecoder.cs:95</c>). Свойство
    /// добавлено, а не заменяет существующее: соседний файл этой правкой не задет.</summary>
    internal int ProtocolVersion => _protocolVersion;

    // --- Очередь A: опкод без коллизии ---

    public byte[] BuildSetUnitSystem(bool miles) => BuildSettingWrite(opcode: 23, miles ? 1 : 0);

    public byte[] BuildSetHighSpeedMode(bool enabled) => BuildSettingWrite(opcode: 26, enabled ? 1 : 0);

    public byte[]? BuildSetKeyToneVolume(int percent) =>
        InRange(percent, 0, 100) ? BuildSettingWrite(opcode: 28, percent) : null;

    public byte[]? BuildSetMaxChargeVoltage(int value) =>
        InRange(value, 0, 120) ? BuildSettingWrite(opcode: 29, value) : null;

    public byte[]? BuildSetAccelerationHelper(int percent) =>
        InRange(percent, 0, 100) ? BuildSettingWrite(opcode: 31, percent) : null;

    public byte[]? BuildSetAccelerationReduction(int percent) =>
        InRange(percent, 0, 100) ? BuildSettingWrite(opcode: 33, percent) : null;

    public byte[]? BuildSetBrakeOverpressureAlarm(int percent) =>
        InRange(percent, 80, 125) ? BuildSettingWrite(opcode: 34, percent) : null;

    /// <summary>Единственная настройка с отрицательным значением: −15..15 уходит на провод
    /// дополнительным кодом (−15 → <c>0xF1</c>), как <c>(byte) (i - 15)</c> у производителя
    /// (<c>VolLightSettingActivity.java:15,31</c>).</summary>
    public byte[]? BuildSetVoltageCorrection(int tenthsOfPercent) =>
        InRange(tenthsOfPercent, -15, 15) ? BuildSettingWrite(opcode: 24, tenthsOfPercent) : null;

    // --- Очередь B: опкод делится с чужой командой ---

    public byte[]? BuildSetStopSpeed(int speedKmh) =>
        InRange(speedKmh, 10, 120) ? BuildSettingWrite(opcode: 17, speedKmh) : null;

    public byte[]? BuildSetStopPower(int percent) =>
        InRange(percent, 30, 100) ? BuildSettingWrite(opcode: 18, percent) : null;

    public byte[]? BuildSetScreenBacklight(int percent) =>
        InRange(percent, 0, 100) ? BuildSettingWrite(opcode: 20, percent) : null;

    /// <summary>
    /// Режим низкого напряжения — обычный тумблер, но на опасном опкоде 25: тот же опкод носит
    /// запись пароля (<c>Util.genPwdCmd</c>). Различие — b5/b6: у настройки 1/2, у пароля 0/5, и
    /// общий сборщик не умеет выдать второе, потому что b6 в нём константа, а не параметр.
    /// </summary>
    public byte[] BuildSetLowVoltageMode(bool enabled) => BuildSettingWrite(opcode: 25, enabled ? 1 : 0);

    /// <summary>
    /// Тревога по скорости — единственная в очереди B, что <b>не</b> строится общим сборщиком:
    /// у неё b6=0 (а не 2) и она уходит парой кадров, <c>SetAlarmSpeedActivity.java:67</c>. Общий
    /// сборщик её собрать не может физически — и это нарочно: b6=2 в нём зашит, а не передаётся
    /// параметром, иначе один неверный аргумент на вызове превратил бы тревогу в отбой педалей
    /// (тот же опкод 17, разница ровно в этом байте).
    /// </summary>
    public byte[]? BuildSetSpeedAlarm(int speedKmh)
    {
        if (!InRange(speedKmh, 10, 120)) return null;

        // Порядок байт — дословно с производителя: у Lk-половины шестого байта нет вовсе (там
        // заполнитель), у Ld-половины он равен 0. Ни та, ни другая не совпадает с записью настройки.
        byte[] legacy = BuildFrame(LkHeader, opcode: 17, [1, Filler, Filler, Filler, Filler, Filler, Filler, (byte)speedKmh]);
        byte[] modern = BuildFrame(LdHeader, opcode: 17, [1, 0, Filler, Filler, Filler, Filler, Filler, (byte)speedKmh]);

        return CombineFrames(legacy, modern);
    }

    // --- Очередь C: опкод 22, тройная коллизия ---

    /// <summary>
    /// Режим транспортировки — обычная одиночная запись настройки, и потому строится общим
    /// сборщиком: пара (b5=1, b6=2) у него та же, что у шестнадцати прочих одиночных записей
    /// (<c>ControlActivity.java:439</c>, разбор — <c>originals-reference-data.md</c> §7.3.1, п. 4).
    /// <para>
    /// Опкод у него, однако, тот же, что у выключения колеса. Общий сборщик здесь <b>не</b>
    /// послабление, а самая надёжная из возможных защит: b6 в нём зашит константой 2, а у обеих
    /// половин выключения он 0 либо заполнитель — значит выключение этим путём непостроимо в
    /// принципе, каким бы ни был вызов.
    /// </para>
    /// </summary>
    public byte[] BuildSetTransportMode(bool enabled) => BuildSettingWrite(opcode: 22, enabled ? 1 : 0);

    /// <summary>
    /// Угол защиты от падения, 35..75° — пара кадров, оба литералом с
    /// <c>SetFallProtectionAngleActivity.java:64</c>. <b>Самая опасная запись протокола:</b> с
    /// командой выключения колеса совпадают шестнадцать байт из восемнадцати, и в обеих половинах
    /// пары. Различает единственный байт 16 — у выключения там жёсткая единица, здесь литеральный
    /// заполнитель; значение угла пишется только в последний байт тела
    /// (<c>progressToValue(i) = i + 35</c>), ветки с зависимостью байта 16 от значения в источнике
    /// нет ни одной. Оттого массивы здесь выписаны целиком, а не собраны параметрами: параметр —
    /// это место, где однажды окажется не то число.
    /// <para>
    /// Диапазон отвергается, а не обрезается, и это <b>строже производителя</b>: у него проверки
    /// нет вовсе, границу задаёт вид ползунка (<c>layout_set_safe_angle.xml:45</c>). Осознанное
    /// отклонение, записано в <c>docs/port-deviations.md</c>.
    /// </para>
    /// </summary>
    public byte[]? BuildSetFallProtectionAngle(int degrees)
    {
        if (!InRange(degrees, 35, 75)) return null;

        byte[] legacy = BuildFrame(LkHeader, opcode: 22,
            [1, Filler, Filler, Filler, Filler, Filler, Filler, Filler, Filler, Filler, Filler, Filler, (byte)degrees]);
        byte[] modern = BuildFrame(LdHeader, opcode: 22,
            [1, 0, Filler, Filler, Filler, Filler, Filler, Filler, Filler, Filler, Filler, Filler, (byte)degrees]);

        return CombineFrames(legacy, modern);
    }

    // --- Очередь D: парный кадр со знаком и команда с физическим эффектом ---

    /// <summary>
    /// Наклон педалей, −80..80 десятых градуса (колесо делит на 10) — пара кадров, оба литералом с
    /// <c>SetAngelActivity.java:69</c>. Значение уходит дополнительным кодом, как
    /// <c>(byte) progressToSendValue(i)</c> = <c>(byte)(i − 80)</c> у производителя (<c>:17,69</c>):
    /// −80 → <c>0xB0</c>, +80 → <c>0x50</c>. Заполнителем <c>0x80</c> (−128) значение стать не может
    /// — оно вне диапазона, и диапазон здесь отвергается, а не обрезается.
    /// <para>
    /// <b>На живом колесе не проверено</b> — сверено с байтовым эталоном источника (решение
    /// владельца 16.08.2026, план §0.1).
    /// </para>
    /// </summary>
    public byte[]? BuildSetAngleTrim(int tenthsOfDegree)
    {
        if (!InRange(tenthsOfDegree, -80, 80)) return null;

        byte value = unchecked((byte)tenthsOfDegree);
        byte[] legacy = BuildFrame(LkHeader, opcode: 16, [1, Filler, Filler, Filler, Filler, Filler, value]);
        byte[] modern = BuildFrame(LdHeader, opcode: 16, [1, 0, Filler, Filler, Filler, Filler, value]);

        return CombineFrames(legacy, modern);
    }

    /// <summary>
    /// Калибровка гироскопа — опкод 21, одиночный кадр с фиксированной единицей в значении
    /// (<c>GyroscopeSettingActivity.java:121-123</c>). Обычная запись настройки по форме: b5=1,
    /// b6=2, оттого и собирается общим сборщиком — байт-в-байт литерал производителя.
    /// <para>
    /// <b>Команда меняет поведение колеса самим фактом посылки.</b> Одна и та же посылка и начинает
    /// калибровку, и заканчивает её: разницу несёт не кадр, а состояние колеса, которое оно
    /// сообщает обратно полем <c>gyro</c> (0 — не калибруется, 1 — идёт, 2 — готово,
    /// <c>GyroscopeSettingActivity.java:42-56</c>). Эффект долгоживущий и сам не откатывается —
    /// ошибка здесь портит не цифру на экране, а то, как колесо держит человека.
    /// </para>
    /// <para>
    /// <b>Отсюда запрет на ходу</b> (<c>GyroscopeSettingActivity.java:66-70</c>: производитель
    /// вместо отправки показывает <c>set_gro_hint</c> «Cant set while riding!»). У нас на движении
    /// кадр <b>не строится вовсе</b> — отказ, а не собранный и не отправленный буфер: то, что не
    /// родилось, нельзя отправить по ошибке ниже по течению.
    /// </para>
    /// <para>
    /// Условие строже оригинального <c>getSpeed() &gt; 0</c> — у нас <c>!= 0</c>, и это не
    /// придирка: знак скорости у нас настраиваемый (<c>veteranNegative</c>,
    /// <c>VeteranDecoder.cs:136-145</c>), катящееся назад колесо даёт отрицательное значение, и
    /// сравнение «больше нуля» пустило бы калибровку под едущим человеком.
    /// </para>
    /// <para>
    /// <b>На живом колесе не проверено</b>, и намеренно: владелец 16.08.2026 отказался калибровать
    /// рабочее колесо ради проверки (план §0.1). За командой стоит только совпадение с байтовым
    /// эталоном производителя.
    /// </para>
    /// </summary>
    public byte[]? BuildCalibrateGyro() =>
        _state.Speed == 0 ? BuildSettingWrite(opcode: 21, value: 1) : null;

    // --- Жёсткость педалей и поколение колеса ---

    /// <summary>Вид «режима езды» этого колеса — по версии протокола из телеметрии. К жёсткости
    /// педалей отношения не имеет, см. <see cref="RideModeScales"/>.</summary>
    public RideModeScale RideModeScale => RideModeScales.FromProtocolVersion(ProtocolVersion);

    /// <summary>
    /// Жёсткость педалей плавной шкалой, опкод 15 (<c>PedalSoftnessSettingActivity.java:37</c>,
    /// диапазон — <c>BaseSetProgressActivity.java:46</c> с <c>getProgressMax()==100</c> и
    /// тождественным <c>progressToCmdValue</c>).
    /// <para>
    /// Поколение колеса здесь не спрашивается нарочно: у этого опкода шкала <b>всегда</b> плавная,
    /// а есть ли настройка у колеса, говорит сентинел <c>128</c> в его собственной телеметрии
    /// (<c>ControlActivity.java:392-395</c>) — признак точнее каталога моделей. Пока входящий кадр
    /// настроек не разобран, спрашивать нечего и подменять сентинел таблицей поколений — значит
    /// прятать настройку у колеса, которое ею владеет.
    /// </para>
    /// </summary>
    public byte[]? BuildSetPedalHardness(int percent) =>
        InRange(percent, 0, 100) ? BuildSettingWrite(opcode: 15, percent) : null;

    // --- Общая сборка ---

    /// <summary>
    /// Кадр обычной записи настройки по формуле производителя (<c>leaperkim-official-app.md:143-148</c>):
    /// заголовок <c>LdAp</c>, опкод, b5=1, b6=2, заполнители, значение, CRC32.
    /// <para>
    /// Опкод приходит сюда <b>литералом</b> из каждого публичного метода, а b6 не приходит вовсе —
    /// он зашит константой. Так сборщик убирает копипасту между двенадцатью однотипными командами,
    /// но не даёт построить ни одной чужой команды на тех же опкодах (§4.2 источника): у всех у них
    /// шестой байт другой.
    /// </para>
    /// </summary>
    private static byte[] BuildSettingWrite(byte opcode, int value)
    {
        // Число заполнителей выведено из инварианта «длина кадра = опкод» (§4 плана): 4 байта
        // заголовка + опкод + b5 + b6 + N заполнителей + значение + 4 байта CRC = opcode, отсюда
        // N = opcode − 12. Сходится со всеми четырнадцатью литералами производителя: 15→3, 17→5, 18→6,
        // 20→8, 21→9, 23→11, 24→12, 25→13, 26→14, 28→16, 29→17, 31→19, 33→21, 34→22.
        // (15 — жёсткость педалей: {…,15,1,2,MIN,MIN,MIN,значение}, три заполнителя.)
        byte[] tail = new byte[opcode - 9]; // b5 + b6 + заполнители + значение
        tail[0] = 1;
        tail[1] = SettingWriteMarker;
        Array.Fill(tail, Filler, 2, tail.Length - 3);
        tail[^1] = unchecked((byte)value); // −15 → 0xF1: дополнительный код, как у производителя

        return BuildFrame(LdHeader, opcode, tail);
    }

    /// <summary>Заголовок + опкод + тело + CRC32 (big-endian, стандартный IEEE — тот же
    /// <c>java.util.zip.CRC32</c>, что у производителя, и тот же, которым мы уже проверяем входящие
    /// кадры в <see cref="VeteranUnpacker"/>).</summary>
    private static byte[] BuildFrame(ReadOnlySpan<byte> header, byte opcode, ReadOnlySpan<byte> tail)
    {
        byte[] frame = new byte[opcode];
        header.CopyTo(frame);
        frame[4] = opcode;
        tail.CopyTo(frame.AsSpan(5));

        uint crc = Crc32.HashToUInt32(frame.AsSpan(0, frame.Length - 4));
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(frame.Length - 4), crc);

        return AssertFrameLength(frame);
    }

    /// <summary>
    /// Порт <c>BtManager.sendBytesDataCombine</c> (<c>BtManager.java:251-267</c>) для парных команд.
    /// Развилка у производителя — <c>fullVersionCode.startsWith("004")</c>: колёса этого семейства
    /// понимают только старый (<c>Lk</c>) кадр, и второй им не тратится. Строку версии колесо
    /// присылает в том же виде, в каком её собираем мы (<c>VeteranDecoder.cs:104</c>), так что
    /// «семейство 004» — это ровно <see cref="ProtocolVersion"/> == 4, то есть Patton.
    /// <para>
    /// Для остальных уходят оба кадра одним буфером: приложение не знает заранее, какой из них
    /// разберёт прошивка, и шлёт оба — это два самостоятельных CRC-корректных кадра подряд, а не
    /// один длинный.
    /// </para>
    /// </summary>
    private byte[] CombineFrames(byte[] legacy, byte[] modern) =>
        ProtocolVersion == 4 ? legacy : [.. legacy, .. modern];

    private static bool InRange(int value, int min, int max) => value >= min && value <= max;

    /// <summary>
    /// Инвариант протокола: длина готового кадра в байтах численно равна опкоду (байту 4).
    /// Подтверждён независимо на 45 литералах производителя (<c>leaperkim-official-app.md</c> §1.4),
    /// на 18 билдерах LoEUC и на нашем собственном кадре бипа. Дешёвая проверка формы, ловящая
    /// ошибку раскладки на этапе разработки — <b>не</b> замена байтовому тесту, который сверяет
    /// содержимое.
    /// </summary>
    internal static byte[] AssertFrameLength(byte[] frame)
    {
        Debug.Assert(frame.Length == frame[4], $"Veteran frame length {frame.Length} != opcode {frame[4]}");
        return frame;
    }
}
