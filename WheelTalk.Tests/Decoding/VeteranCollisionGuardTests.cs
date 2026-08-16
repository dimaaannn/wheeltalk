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
/// «записать угол защиты от падения» совпадают <b>шестнадцать байт из восемнадцати</b>, и в обеих
/// половинах пары. Поэтому b6 в нашем общем сборщике не параметр, а константа 2, угол собран
/// отдельными литеральными массивами, и эти тесты — красный CI на любую правку, которая сотрёт
/// различие.
/// </para>
/// </summary>
public class VeteranCollisionGuardTests
{
    private static IVeteranSettingsCommands NewWheel() => (IVeteranSettingsCommands)DecoderHarness.ForVeteran().Decoder.ProtocolDecoder;

    /// <summary>Все кадры записи настроек, что декодер вообще способен построить, — по каждому
    /// допустимому значению каждой команды. Список общий с замком на служебные команды
    /// (<see cref="VeteranOutgoingFrames"/>): новый билдер должен попадать под оба замка сразу.</summary>
    private static IEnumerable<byte[]> EverySettingsFrame() => VeteranOutgoingFrames.EverySettingsWrite(NewWheel());

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

    // ==================== Опкод 25: режим низкого напряжения против записи пароля ====================

    /// <summary>
    /// Запись пароля берёт построитель синхронизации времени и прибавляет к его опкоду 7
    /// (18 → 25, <c>Util.java:257-273</c>), оттого и несёт чужие для настройки b5/b6 = 0/5. Пароль
    /// нам запрещён навсегда (план §8: программного сброса забытого PIN в приложении нет) — здесь
    /// утверждается, что тумблер низкого напряжения ни одним своим значением его не изображает.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Opcode25_LowVoltageMode_NeverLooksLikePassword(bool enabled)
    {
        byte[] frame = NewWheel().BuildSetLowVoltageMode(enabled);

        Assert.Equal(25, frame[4]);
        Assert.Equal(1, frame[5]); // пароль несёт здесь 0
        Assert.Equal(2, frame[6]); // пароль несёт здесь 5
    }

    // ==================== Общие правила на весь набор ====================

    /// <summary>
    /// Опкод сам по себе никогда не служит единственным признаком команды: каждый кадр записи
    /// настройки несёт b5=1 и b6=2, и ровно этим отличается от всех соседей по своему опкоду.
    /// Половины парных команд — тревоги скорости и угла защиты от падения — законное исключение
    /// (b6=0 и заполнитель), их стерегут отдельные замки выше. Здесь они пропускаются по длине
    /// буфера: пара — это два кадра подряд, и его длина опкоду не равна.
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

    // ============ Опкод 22: тройная коллизия — самая опасная пара протокола (план §1.4, §3) ============

    /// <summary>Заполнитель тела кадра (<c>Byte.MIN_VALUE</c> у производителя).</summary>
    private const byte Filler = 0x80;

    /// <summary>
    /// Питание/удержание — <b>дословные кадры производителя</b> (<c>CMD_SET_CLOSE_IN_10</c>,
    /// <c>BtManager.java:81</c>, и <c>CMD_SET_CLOSE_IN_10_NEW</c>, <c>:90</c>), CRC32 досчитан
    /// отдельно стандартным IEEE-алгоритмом. Мы их не строим и не будем никогда (план §8): здесь
    /// они стоят образцом того, с чем нашим кадрам совпадать запрещено. Команда запускает
    /// 10-секундный отсчёт до выключения колеса, повторная посылка сбрасывает отсчёт на новые
    /// 10 секунд, отдельной отмены у неё нет вовсе (<c>originals-reference-data.md</c> §7.3.1).
    /// </summary>
    private static readonly string[] PowerOffFrames =
    [
        "4C6B41701601808080808080808080800180D96E1122", // Lk — старое поколение прошивок
        "4C64417016010080808080808080808001807F2B4D17", // Ld — новое поколение
    ];

    /// <summary>
    /// Замок не спит: эталон питания сам держит инвариант «длина = опкод» и несёт единицу в байте
    /// 16 — том единственном байте, которым он отличается от записи угла. Без этой проверки опечатка
    /// в эталоне обессмыслила бы все замки ниже, и они остались бы зелёными.
    /// </summary>
    [Fact]
    public void Opcode22_PowerOffReference_IsWellFormedAndCarriesOneInByte16()
    {
        foreach (string hex in PowerOffFrames)
        {
            byte[] frame = Convert.FromHexString(hex);

            Assert.Equal((byte)frame.Length, frame[4]);
            Assert.Equal(22, frame[4]);
            Assert.Equal(1, frame[5]);
            Assert.Equal(1, frame[16]);       // у записи угла здесь всегда заполнитель
            Assert.Equal(Filler, frame[17]);  // а у неё здесь — само значение угла
        }
    }

    /// <summary>
    /// <b>Главный замок этапа 5.</b> Ни один кадр угла защиты от падения не совпадает с кадром
    /// питания — на всём диапазоне 35..75, обеими половинами пары, а не только при равных значениях.
    /// <para>
    /// Почему совпадения не будет никогда: шестнадцать байт из восемнадцати у этих команд дословно
    /// одинаковы (заголовок, опкод 22, b5=1, b6, девять заполнителей), различает их <b>только байт
    /// 16</b> — у питания там жёсткая единица, у угла литеральный заполнитель <c>0x80</c>
    /// (<c>SetFallProtectionAngleActivity.java:64</c>: значение пишется единственно в последний байт,
    /// <c>progressToValue(i) = i + 35</c>, и ветки с зависимостью байта 16 от значения в источнике
    /// нет ни одной). Потому здесь утверждается и неравенство целых кадров, и форма этого одного
    /// байта: первое ловит совпадение, второе — причину, по которой его не бывает.
    /// </para>
    /// </summary>
    [Fact]
    public void Opcode22_FallProtectionAngle_NeverEqualsPowerOff_OverWholeRange()
    {
        var wheel = NewWheel();

        for (int degrees = 35; degrees <= 75; degrees++)
        {
            foreach (byte[] half in VeteranOutgoingFrames.SplitFrames(wheel.BuildSetFallProtectionAngle(degrees)!))
            {
                Assert.Equal(22, half[4]);
                Assert.DoesNotContain(Convert.ToHexString(half), PowerOffFrames);
                Assert.Equal(Filler, half[16]);  // единственный различающий байт — и он не значение
                Assert.Equal(degrees, half[^5]); // значение живёт только в последнем байте тела
            }
        }
    }

    /// <summary>
    /// Режим транспортировки из опасной пары выпадает: у него b6=2 против 0 (<c>Ld</c>) и
    /// заполнителя (<c>Lk</c>) у обеих её половин — он отличим уже на седьмом байте, как обычные
    /// коллизии очереди B. Утверждается и это, и полное неравенство кадров — с питанием и со всем
    /// диапазоном угла разом.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Opcode22_TransportMode_NeverEqualsPowerOffNorFallProtectionAngle(bool enabled)
    {
        var wheel = NewWheel();
        byte[] frame = wheel.BuildSetTransportMode(enabled);

        Assert.Equal(22, frame[4]);
        Assert.Equal(1, frame[5]);
        Assert.Equal(2, frame[6]); // питание и угол несут здесь 0 либо заполнитель, но не 2
        Assert.DoesNotContain(Convert.ToHexString(frame), PowerOffFrames);
        Assert.DoesNotContain(Convert.ToHexString(frame), EveryFallProtectionAngleHalf(wheel));
    }

    /// <summary>
    /// Опкод 22 больше не запрещён целиком — на нём живут две наши законные команды. Взамен запрета
    /// стоит перечисление: каждый кадр с опкодом 22, который декодер вообще способен родить, обязан
    /// быть либо режимом транспортировки, либо половиной пары угла защиты. <b>Третьей формы на этом
    /// опкоде у нас нет</b> — третья форма производителя это питание, и первой же строкой
    /// проверяется, что перечень его не впустил (иначе замок пропускал бы всё подряд).
    /// </summary>
    [Fact]
    public void Opcode22_EveryFrameWeCanBuild_IsTransportModeOrFallProtectionAngle()
    {
        var wheel = NewWheel();
        var allowed = EveryFallProtectionAngleHalf(wheel);
        allowed.Add(Convert.ToHexString(wheel.BuildSetTransportMode(true)));
        allowed.Add(Convert.ToHexString(wheel.BuildSetTransportMode(false)));

        foreach (string powerOff in PowerOffFrames) Assert.DoesNotContain(powerOff, allowed);

        foreach (byte[] outgoing in EverySettingsFrame())
        {
            foreach (byte[] frame in VeteranOutgoingFrames.SplitFrames(outgoing))
            {
                if (frame[4] != 22) continue;

                Assert.Contains(Convert.ToHexString(frame), allowed);
            }
        }
    }

    /// <summary>Обе половины пары угла на всём диапазоне — множеством, чтобы сравнивать «ни один с
    /// ни одним», а не только совпадающие значения.</summary>
    private static HashSet<string> EveryFallProtectionAngleHalf(IVeteranSettingsCommands wheel)
    {
        var halves = new HashSet<string>();
        for (int degrees = 35; degrees <= 75; degrees++)
        {
            foreach (byte[] half in VeteranOutgoingFrames.SplitFrames(wheel.BuildSetFallProtectionAngle(degrees)!))
            {
                halves.Add(Convert.ToHexString(half));
            }
        }
        return halves;
    }

    /// <summary>
    /// Признак записи настройки — <b>пара</b> (b5, b6) = (1, 2), а не один шестой байт. Правило
    /// «b6=2 значит запись» ломается на кадре самого производителя:
    /// <c>CMD_CLEAR_METER_NEW = {76, 100, 65, 112, 13, 0, 2, MIN, 1}</c> (<c>BtManager.java:85</c>) —
    /// сброс поездки, где b6=2 при b5=<b>0</b>. Здесь это записано исполняемым: упростишь признак до
    /// одного байта — и чужой кадр станет неотличим от записи настройки.
    /// </summary>
    [Fact]
    public void WriteMarker_IsThePairOfBytes_NotByte6Alone()
    {
        byte[] clearMeterNew = Convert.FromHexString("4C6441700D00028001D4C081F2");
        Assert.Equal((byte)clearMeterNew.Length, clearMeterNew[4]); // эталон держит инвариант §4

        Assert.Equal(2, clearMeterNew[6]);    // по одному байту — «запись настройки»
        Assert.NotEqual(1, clearMeterNew[5]); // по паре — чужая команда, и это верный ответ

        byte[] transportMode = NewWheel().BuildSetTransportMode(true);
        Assert.Equal(1, transportMode[5]);
        Assert.Equal(2, transportMode[6]);
        Assert.NotEqual(Convert.ToHexString(clearMeterNew), Convert.ToHexString(transportMode));
    }
}
