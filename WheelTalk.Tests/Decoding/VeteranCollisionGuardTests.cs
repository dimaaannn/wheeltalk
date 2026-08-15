using WheelTalk.Core.Decoding;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Замок на коллизии опкодов LeaperKim — <c>docs/veteran-commands-import-plan.md</c> §3.
/// <para>
/// <b>От чего замок.</b> Один и тот же байт-опкод в этом протоколе обслуживает по две-три разные
/// команды: 17 — отбой педалей и тревогу скорости, 18 — порог ШИМ и синхронизацию времени, 20 —
/// яркость экрана и чтение журнала, 22 — режим транспортировки, выключение и угол защиты от
/// падения, 25 — режим низкого напряжения и запись пароля (<c>leaperkim-official-app.md</c> §4.2).
/// Различает их байт 6 (иногда вместе с 5), а на опкоде 22 — ещё и форма хвоста.
/// </para>
/// <para>
/// <b>Механизм поломки, который здесь ловится.</b> Однотипные кадры так и просятся под общий
/// сборщик с параметрами <c>(опкод, b5, b6)</c>. Стоит завести такой — и один неверный аргумент на
/// одном вызове даст кадр не той команды, причём ревью этого не увидит: у пары «выключить колесо» /
/// «записать угол защиты от падения» совпадают первые семь байт целиком. Поэтому b6 в нашем
/// сборщике не параметр, а константа 2, и эти тесты — красный CI на любую правку, которая сотрёт
/// различие.
/// </para>
/// </summary>
public class VeteranCollisionGuardTests
{
    private static IVeteranSettingsCommands NewWheel() => (IVeteranSettingsCommands)DecoderHarness.ForVeteran().Decoder.ProtocolDecoder;

    /// <summary>Все кадры записи настроек, что декодер вообще способен построить, — по каждому
    /// допустимому значению каждой команды. Сюда обязан попадать каждый новый билдер: тесты ниже
    /// метут именно этот список.</summary>
    private static IEnumerable<byte[]> EverySettingsFrame()
    {
        var wheel = NewWheel();

        yield return wheel.BuildSetUnitSystem(true);
        yield return wheel.BuildSetUnitSystem(false);
        yield return wheel.BuildSetHighSpeedMode(true);
        yield return wheel.BuildSetHighSpeedMode(false);
        for (int v = 0; v <= 100; v++)
        {
            yield return wheel.BuildSetKeyToneVolume(v)!;
            yield return wheel.BuildSetAccelerationHelper(v)!;
            yield return wheel.BuildSetAccelerationReduction(v)!;
            yield return wheel.BuildSetScreenBacklight(v)!;
        }
        for (int v = 0; v <= 120; v++) yield return wheel.BuildSetMaxChargeVoltage(v)!;
        for (int v = 80; v <= 125; v++) yield return wheel.BuildSetBrakeOverpressureAlarm(v)!;
        for (int v = -15; v <= 15; v++) yield return wheel.BuildSetVoltageCorrection(v)!;
        for (int v = 30; v <= 100; v++) yield return wheel.BuildSetStopPower(v)!;
        for (int v = 10; v <= 120; v++)
        {
            yield return wheel.BuildSetStopSpeed(v)!;
            yield return wheel.BuildSetSpeedAlarm(v)!;
        }
    }

    // ==================== Опкод 17: отбой педалей против тревоги скорости ====================

    /// <summary>
    /// Обе команды сидят на опкоде 17 и обе принимают одну и ту же величину — скорость 10..120 км/ч
    /// (<c>StopSpeedSettingActivity.java:42</c> против <c>SetAlarmSpeedActivity.java:67</c>).
    /// Утверждение сильное: <b>ни один</b> кадр отбоя педалей не совпадает байт-в-байт ни с одной
    /// половиной кадра тревоги — на всём диапазоне, для всех пар значений, а не только для равных.
    /// </summary>
    [Fact]
    public void Opcode17_StopSpeed_NeverEqualsAnyHalfOfSpeedAlarm()
    {
        var wheel = NewWheel();
        var alarmHalves = new HashSet<string>();
        for (int v = 10; v <= 120; v++)
        {
            byte[] pair = wheel.BuildSetSpeedAlarm(v)!;
            alarmHalves.Add(Convert.ToHexString(pair[..17]));
            alarmHalves.Add(Convert.ToHexString(pair[17..]));
        }

        for (int v = 10; v <= 120; v++)
        {
            byte[] stopSpeed = wheel.BuildSetStopSpeed(v)!;
            Assert.DoesNotContain(Convert.ToHexString(stopSpeed), alarmHalves);
        }
    }

    /// <summary>Чем именно различаются: байтом 6 — у отбоя педалей 2, у новой (<c>Ld</c>) половины
    /// тревоги 0. У старой (<c>Lk</c>) половины шестого байта нет вовсе, там заполнитель
    /// <c>0x80</c>, а различает её ещё и второй байт заголовка (<c>k</c> против <c>d</c>).</summary>
    [Fact]
    public void Opcode17_DifferInByte6()
    {
        var wheel = NewWheel();
        byte[] stopSpeed = wheel.BuildSetStopSpeed(30)!;
        byte[] alarm = wheel.BuildSetSpeedAlarm(30)!;
        byte[] alarmLegacy = alarm[..17];
        byte[] alarmModern = alarm[17..];

        Assert.Equal(17, stopSpeed[4]);
        Assert.Equal(17, alarmLegacy[4]);
        Assert.Equal(17, alarmModern[4]);

        Assert.NotEqual(stopSpeed[6], alarmModern[6]);
        Assert.Equal(2, stopSpeed[6]);
        Assert.Equal(0, alarmModern[6]);
        Assert.NotEqual(stopSpeed[1], alarmLegacy[1]); // 'd' против 'k'
    }

    // ==================== Опкод 18: порог ШИМ против синхронизации времени ====================

    /// <summary>
    /// Синхронизация времени (<c>Util.getTimeBytes</c>, <c>Util.java:236</c>) сидит на том же
    /// опкоде 18 и различается парой b5/b6 = 0/5 против наших 1/2. Сама команда нам не нужна и в
    /// план не входит (§8: не пользовательская, оригинал шлёт её сам при подключении) — здесь она
    /// присутствует только как образец соседа, с которым нельзя совпасть.
    /// </summary>
    [Fact]
    public void Opcode18_StopPower_NeverLooksLikeTimeSync()
    {
        var wheel = NewWheel();
        for (int percent = 30; percent <= 100; percent++)
        {
            byte[] frame = wheel.BuildSetStopPower(percent)!;
            Assert.Equal(18, frame[4]);
            Assert.Equal(1, frame[5]); // синхронизация времени несёт здесь 0
            Assert.Equal(2, frame[6]); // синхронизация времени несёт здесь 5
        }
    }

    // ==================== Опкод 20: яркость экрана против чтения журнала ====================

    /// <summary>
    /// <c>CMD_READ_LOG_NEW = {76, 100, 65, 112, 20, 1, 0, MIN×8, 1}</c> (<c>BtManager.java:89</c>,
    /// разбор — <c>leaperkim-official-app.md</c> §4.5) — служебное чтение журнала колеса, делит
    /// опкод 20 с яркостью экрана и различается байтом 6 (0 против 2). Экрана-потребителя журнала у
    /// нас нет, команда в план не входит — но эталонный кадр здесь нужен, чтобы утверждать: ни одно
    /// значение яркости его не воспроизводит.
    /// </summary>
    [Fact]
    public void Opcode20_ScreenBacklight_NeverEqualsReadLogFrame()
    {
        byte[] readLog = Convert.FromHexString("4C64417014010080808080808080800157B1E3EC");
        Assert.Equal((byte)readLog.Length, readLog[4]); // эталон соседа сам держит инвариант §4

        var wheel = NewWheel();
        for (int percent = 0; percent <= 100; percent++)
        {
            byte[] frame = wheel.BuildSetScreenBacklight(percent)!;
            Assert.NotEqual(Convert.ToHexString(readLog), Convert.ToHexString(frame));
            Assert.NotEqual(readLog[6], frame[6]);
        }
    }

    // ==================== Общие правила на весь набор ====================

    /// <summary>
    /// Опкод сам по себе никогда не служит единственным признаком команды: каждый кадр записи
    /// настройки несёт b5=1 и b6=2, и ровно этим отличается от всех соседей по своему опкоду.
    /// Половины тревоги скорости — законное исключение (b6=0 и заполнитель), они проверены выше
    /// отдельно.
    /// </summary>
    [Fact]
    public void EverySettingsWrite_CarriesTheWriteMarkerInByte6()
    {
        foreach (byte[] frame in EverySettingsFrame())
        {
            // Тревога скорости — пара кадров в одном буфере; её разбираем отдельным тестом.
            if (frame.Length != frame[4]) continue;

            Assert.Equal(1, frame[5]);
            Assert.Equal(2, frame[6]);
        }
    }

    /// <summary>Инвариант «длина кадра = опкод» на всём наборе значений всех команд разом — то, чего
    /// не даст ни один точечный байтовый тест.</summary>
    [Fact]
    public void EverySettingsWrite_LengthEqualsOpcode()
    {
        foreach (byte[] frame in EverySettingsFrame())
        {
            if (frame.Length == frame[4]) continue;

            // Единственный законный случай другой длины — парный кадр: две самостоятельные,
            // CRC-корректные половины подряд, каждая со своим инвариантом.
            byte[] first = frame[..frame[4]];
            byte[] second = frame[frame[4]..];
            Assert.Equal((byte)first.Length, first[4]);
            Assert.Equal((byte)second.Length, second[4]);
        }
    }

    /// <summary>
    /// Замок вперёд: очередь C (опкод 22 — режим транспортировки, выключение, угол защиты от
    /// падения) и запись пароля (опкод 25) этой работой <b>не</b> реализованы, и ни один
    /// существующий билдер не смеет случайно родить кадр с этими опкодами. Когда очередь C дойдёт
    /// до кода, этот тест обязан быть пересмотрен осознанно, а не молча пройти мимо.
    /// </summary>
    [Fact]
    public void NoBuilderYetEmits_Opcode22Or25()
    {
        foreach (byte[] frame in EverySettingsFrame())
        {
            Assert.NotEqual(22, frame[4]); // питание / transport_mode / fallProtectionAngle (§1.4)
            Assert.NotEqual(25, frame[4]); // low_voltage_mode делит опкод с записью пароля (§8)
        }
    }
}
