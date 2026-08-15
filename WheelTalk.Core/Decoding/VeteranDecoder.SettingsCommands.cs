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

    /// <summary>Байт 6 = 2 — признак «запись обычной настройки». Ровно этим байтом настройка
    /// отличается от чужой команды на том же опкоде: чтение журнала на 20-м, тревога скорости на
    /// 17-м, синхронизация времени на 18-м, питание и угол защиты от падения на 22-м — все они
    /// несут в шестом байте <b>не</b> 2 (<c>leaperkim-official-app.md</c> §4.2).</summary>
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
        // N = opcode − 12. Сходится со всеми тринадцатью литералами производителя: 17→5, 18→6,
        // 20→8, 21→9, 23→11, 24→12, 25→13, 26→14, 28→16, 29→17, 31→19, 33→21, 34→22.
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
