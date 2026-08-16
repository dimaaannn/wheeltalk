using WheelTalk.Core.Decoding;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Байтовые эталоны записи настроек LeaperKim — план импорта команд, этапы 2 и 3
/// (<c>docs/veteran-commands-import-plan.md</c> §1.2, §1.3).
/// <para>
/// <b>Откуда взяты ожидаемые байты.</b> Не из перехваченного трафика (его нет) и не из того, что
/// вернул билдер. Тело каждого кадра — дословный литерал родного приложения производителя
/// (<c>C:\Work\repos\loeuc\src_leaper</c>, ссылка <c>файл:строка</c> над каждым тестом), CRC32
/// досчитан отдельно стандартным IEEE-алгоритмом (тот же <c>zlib.crc32</c>, что и
/// <c>java.util.zip.CRC32</c> у производителя), big-endian — порядок подтверждён на нашем
/// собственном кадре бипа (<c>veteran-loeuc-comparison.md</c>, Вопрос 4).
/// </para>
/// <para>
/// Каждый тест начинается с проверки инварианта «длина кадра = опкод»
/// (<c>leaperkim-official-app.md</c> §1.4, 45 литералов): дешёвая проверка формы, ловящая ошибку
/// раскладки раньше, чем неверное содержимое.
/// </para>
/// </summary>
public class VeteranSettingsCommandBytesTests
{
    private static IVeteranSettingsCommands Commands(DecoderHarness harness) =>
        (IVeteranSettingsCommands)harness.Decoder.ProtocolDecoder;

    private static IVeteranSettingsCommands NewWheel() => Commands(DecoderHarness.ForVeteran());

    /// <summary>Инвариант §1.4 плюс сверка с эталоном — обязательная форма каждого теста ниже.</summary>
    private static void AssertFrame(string expectedHex, byte[]? frame)
    {
        Assert.NotNull(frame);
        Assert.Equal((byte)frame.Length, frame[4]);
        Assert.Equal(Convert.FromHexString(expectedHex), frame);
    }

    // ==================== Очередь A: опкод без коллизии (§1.2) ====================

    /// <summary>
    /// `unit`, опкод 23/<c>0x17</c>, b6=2, значение 1 = мили, 0 = километры.
    /// Литерал — <c>ControlActivity.java:443</c> (и он же дословно в <c>UnitSwitchActivity.java:76</c>,
    /// две независимые точки отправки одной и той же команды в приложении производителя).
    /// Что 1 означает именно мили, видно не по имени: тумблер выставляется из <c>isMiUnit()</c> и
    /// шлёт <c>z ? 1 : 0</c> (<c>ControlActivity.java:357,425-428</c>), а экран выбора единиц зовёт
    /// <c>sendUnitCmd(!z)</c>, где <c>z</c> — «выбраны километры» (<c>UnitSwitchActivity.java:68-73</c>).
    /// </summary>
    [Theory]
    [InlineData(true, "4C6441701701028080808080808080808080011FF96E85")]
    [InlineData(false, "4C64417017010280808080808080808080800068FE5E13")]
    public void SetUnitSystem_MatchesOfficialFrame(bool miles, string expectedHex) =>
        AssertFrame(expectedHex, NewWheel().BuildSetUnitSystem(miles));

    /// <summary>`high_speed_mode`, опкод 26/<c>0x1A</c>, b6=2, тумблер — <c>ControlActivity.java:451</c>.</summary>
    [Fact]
    public void SetHighSpeedMode_On_MatchesOfficialFrame() =>
        AssertFrame("4C6441701A01028080808080808080808080808080012C5FA11F", NewWheel().BuildSetHighSpeedMode(true));

    /// <summary>`key_tone`, опкод 28/<c>0x1C</c>, b6=2, диапазон 0..100 —
    /// <c>KeyToneSettingActivity.java:30</c> (кадр), <c>:9-15</c> (диапазон и тождественное
    /// преобразование ползунка в значение).</summary>
    [Fact]
    public void SetKeyToneVolume_Max_MatchesOfficialFrame() =>
        AssertFrame("4C6441701C0102808080808080808080808080808080806438DA7228", NewWheel().BuildSetKeyToneVolume(100));

    /// <summary>`max_charge_voltage`, опкод 29/<c>0x1D</c>, b6=2, диапазон 0..120 —
    /// <c>MaxChargePowerSettingActivity.java:31</c>, <c>:10-16</c>.</summary>
    [Fact]
    public void SetMaxChargeVoltage_Max_MatchesOfficialFrame() =>
        AssertFrame("4C6441701D01028080808080808080808080808080808080781C99D17D", NewWheel().BuildSetMaxChargeVoltage(120));

    /// <summary>`acc_dec_helper`, опкод 31/<c>0x1F</c>, b6=2, диапазон 0..100 —
    /// <c>SetUpDownSpwwdHelpActivity.java:30</c>, <c>:9-15</c>.</summary>
    [Fact]
    public void SetAccelerationHelper_Middle_MatchesOfficialFrame() =>
        AssertFrame("4C6441701F010280808080808080808080808080808080808080328F89D3D5", NewWheel().BuildSetAccelerationHelper(50));

    /// <summary>`acc_reduction`, опкод 33/<c>0x21</c>, b6=2, диапазон 0..100 —
    /// <c>SetUpSpeedCulActivity.java:30</c>, <c>:9-15</c>.</summary>
    [Fact]
    public void SetAccelerationReduction_Min_MatchesOfficialFrame() =>
        AssertFrame("4C64417021010280808080808080808080808080808080808080808000F0FC0BD4", NewWheel().BuildSetAccelerationReduction(0));

    /// <summary>`brake_overpressure_alarm`, опкод 34/<c>0x22</c>, b6=2. Нетривиальный диапазон
    /// 80..125 — ползунок 0..45 плюс сдвиг 80 (<c>BrakeSettingActivity.java:9-15</c>), кадр —
    /// <c>:30</c>. Опкод 34 в шестнадцатеричном виде равен <c>0x22</c>, но это <b>не</b> тот опкод
    /// 22, вокруг которого тройная коллизия: тот — десятичный. Путаница дорогая, потому и сказано.</summary>
    [Fact]
    public void SetBrakeOverpressureAlarm_Max_MatchesOfficialFrame() =>
        AssertFrame("4C644170220102808080808080808080808080808080808080808080807DB42C7D79", NewWheel().BuildSetBrakeOverpressureAlarm(125));

    /// <summary>`voltage_correction`, опкод 24/<c>0x18</c>, b6=2, −15..15 десятых процента
    /// (<c>VolLightSettingActivity.java:10-16,31</c>). Единственная настройка с отрицательным
    /// значением: −15 уходит дополнительным кодом <c>0xF1</c>.</summary>
    [Theory]
    [InlineData(-15, "4C644170180102808080808080808080808080F129076DF6")]
    [InlineData(15, "4C6441701801028080808080808080808080800F7302B2ED")]
    public void SetVoltageCorrection_MatchesOfficialFrame(int tenths, string expectedHex) =>
        AssertFrame(expectedHex, NewWheel().BuildSetVoltageCorrection(tenths));

    // ==================== Очередь B: опкод делится с чужой командой (§1.3) ====================

    /// <summary>`stop_speed` (отбой педалей), опкод 17/<c>0x11</c>, <b>b6=2</b>, 10..120 км/ч —
    /// ползунок 0..110 плюс сдвиг 10 (<c>StopSpeedSettingActivity.java:11-17</c>), кадр — <c>:42</c>.
    /// Одиночный <c>Ld</c>, в отличие от тревоги скорости на том же опкоде.</summary>
    [Theory]
    [InlineData(120, "4C644170110102808080808078C4715523")]
    [InlineData(10, "4C64417011010280808080800A7A7A4533")]
    public void SetStopSpeed_MatchesOfficialFrame(int kmh, string expectedHex) =>
        AssertFrame(expectedHex, NewWheel().BuildSetStopSpeed(kmh));

    /// <summary>`stop_power` (порог ШИМ), опкод 18/<c>0x12</c>, <b>b5/b6 = 1/2</b>, 30..100 % —
    /// ползунок 0..70 плюс сдвиг 30 (<c>StopPowerSettingActivity.java:9-15</c>), кадр — <c>:30</c>.
    /// Тот же опкод носит служебная синхронизация времени с b5/b6 = 0/5 (<c>Util.java:236</c>).</summary>
    [Theory]
    [InlineData(30, "4C6441701201028080808080801EBDFC027F")]
    [InlineData(100, "4C644170120102808080808080640D2C9A5D")]
    public void SetStopPower_MatchesOfficialFrame(int percent, string expectedHex) =>
        AssertFrame(expectedHex, NewWheel().BuildSetStopPower(percent));

    /// <summary>`screen_backlight`, опкод 20/<c>0x14</c>, <b>b6=2</b>, 0..100 % —
    /// <c>ScreenBacklightSettingActivity.java:9-15,30</c>. Тот же опкод носит служебное чтение
    /// журнала с b6=0 (<c>BtManager.java:89</c>).</summary>
    [Theory]
    [InlineData(100, "4C6441701401028080808080808080646E9CA606")]
    [InlineData(0, "4C64417014010280808080808080800024430347")]
    public void SetScreenBacklight_MatchesOfficialFrame(int percent, string expectedHex) =>
        AssertFrame(expectedHex, NewWheel().BuildSetScreenBacklight(percent));

    /// <summary>
    /// `low_voltage_mode`, опкод 25/<c>0x19</c>, <b>b5/b6 = 1/2</b>, тумблер — литерал
    /// <c>ControlActivity.java:446-448</c>, вызов <c>:352</c> (<c>sendLowVolCmd(z ? 1 : 0)</c>).
    /// Тринадцать заполнителей между b6 и значением — их в литерале ровно столько, и это сходится
    /// с инвариантом «длина = опкод»: 4+1+1+1+13+1+4 = 25.
    /// <para>
    /// Тот же опкод носит запись пароля (<c>Util.genPwdCmd</c>, <c>Util.java:257-273</c>) с
    /// b5/b6 = 0/5 — потому эта настройка проверяется ещё и замком
    /// <see cref="VeteranCollisionGuardTests"/>, а не одними байтами.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(true, "4C644170190102808080808080808080808080800140700CAB")]
    [InlineData(false, "4C644170190102808080808080808080808080800037773C3D")]
    public void SetLowVoltageMode_MatchesOfficialFrame(bool enabled, string expectedHex) =>
        AssertFrame(expectedHex, NewWheel().BuildSetLowVoltageMode(enabled));

    /// <summary>
    /// `speed_alarm`, опкод 17/<c>0x11</c>, парой <c>Lk</c>+<c>Ld</c> — <c>SetAlarmSpeedActivity.java:67</c>,
    /// значение = ползунок + 10 (<c>:62-67</c>), диапазон 10..120 км/ч.
    /// <para>
    /// <b>Здесь LoEUC ошибся:</b> у них у этой команды <c>commandId=19</c>
    /// (<c>loeuc-leaperkim-commands.md:125</c>), а опкода 19 в протоколе нет вовсе — проверено
    /// программно на всех 45 литералах производителя (<c>leaperkim-official-app.md</c> §1.4, §9.2).
    /// Настоящий опкод — 17, тот же, что у отбоя педалей.
    /// </para>
    /// <para>
    /// Обе половины — самостоятельные CRC-корректные кадры по 17 байт, уходящие одним буфером
    /// (порт <c>sendBytesDataCombine</c>, <c>BtManager.java:251-267</c>), а не один кадр на 34 байта.
    /// Инвариант «длина = опкод» проверяется поэтому отдельно на каждой половине.
    /// </para>
    /// </summary>
    [Fact]
    public void SetSpeedAlarm_MatchesOfficialPairOfFrames()
    {
        const string legacyHex = "4C6B417011018080808080801E0DBDF5E4"; // Lk, BtManager: старое поколение
        const string modernHex = "4C64417011010080808080801EF73F8067"; // Ld, новое поколение

        // Sherman L, версия протокола 6 — не семейство «004», значит уходят обе половины.
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(
            "dc5a5c53397afffe0aa400000df10000000a0b3d",
            "0e0e0000037a035217730064000e00b480c80000",
            "808080808080058080808080800ff30ff50ff50f",
            "f50ff50fef0ff20ff30ff30ff30ff30fed0ff30f",
            "f40ff5378c5145");

        byte[]? pair = Commands(harness).BuildSetSpeedAlarm(30);

        Assert.NotNull(pair);
        Assert.Equal(Convert.FromHexString(legacyHex + modernHex), pair);
        AssertFrame(legacyHex, pair[..17]);
        AssertFrame(modernHex, pair[17..]);
    }

    /// <summary>
    /// Семейство прошивок <c>004</c> понимает только старый (<c>Lk</c>) кадр, и второй ему не
    /// тратится — <c>BtManager.java:254-259</c>, развилка <c>fullVersionCode.startsWith("004")</c>.
    /// Строку версии мы собираем той же формулой, что производитель (<c>VeteranDecoder.cs:104</c>),
    /// так что «семейство 004» — это версия протокола 4, то есть Patton. Фикстура — та же, что у
    /// <c>VeteranDecoderTests.Decodes_patton_crc</c> (версия «004.0.12»).
    /// </summary>
    [Fact]
    public void SetSpeedAlarm_OnFirmwareFamily004_SendsLegacyHalfOnly()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(
            "dc5a5c452abe00003edc00008562003500000b5c",
            "0dfe000002bc07d00fac000219fb0000006f0000",
            "80808080808004000014ffffffffff32ee029109",
            "df0fd303cb000000006f9a79c2");
        Assert.Equal("004.0.12", harness.Snapshot().Version); // фикстура действительно того семейства

        AssertFrame("4C6B417011018080808080801E0DBDF5E4", Commands(harness).BuildSetSpeedAlarm(30));
    }

    // ==================== Очередь C: тройная коллизия опкода 22 (§1.4) ====================

    /// <summary>
    /// `transport_mode`, опкод 22/<c>0x16</c>, b5/b6 = 1/2, тумблер — <c>ControlActivity.java:439</c>,
    /// вызов <c>:342</c> (<c>z ? 1 : 0</c>, то есть 1 = режим транспортировки включён).
    /// <para>
    /// Единственный из трёх смыслов опкода 22, кто уходит <b>одиночным</b> кадром
    /// (<c>sendBytesData</c>, без развилки поколений) и собирается общим сборщиком записи настройки:
    /// пара (b5=1, b6=2) у него та же, что у шестнадцати прочих одиночных записей
    /// (<c>originals-reference-data.md</c> §7.3.1, п. 4). Особой ветки он не заслуживает — и это
    /// не послабление, а защита: в общем сборщике b6 зашит константой 2, а у выключения колеса он
    /// 0 либо заполнитель, значит выключение этим путём непостроимо в принципе.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(true, "4C64417016010280808080808080808080012B37C934")]
    [InlineData(false, "4C64417016010280808080808080808080005C30F9A2")]
    public void SetTransportMode_MatchesOfficialFrame(bool enabled, string expectedHex) =>
        AssertFrame(expectedHex, NewWheel().BuildSetTransportMode(enabled));

    /// <summary>
    /// `fallProtectionAngle`, опкод 22/<c>0x16</c>, пара <c>Lk</c>+<c>Ld</c>, 35..75° —
    /// <c>SetFallProtectionAngleActivity.java:64</c> (оба литерала), диапазон — ползунок
    /// <c>layout_set_safe_angle.xml:45</c> (<c>android:max="40"</c>) плюс смещение
    /// <c>progressToValue(i) = i + 35</c> (<c>:17</c>).
    /// <para>
    /// <b>Самая опасная запись протокола.</b> С командой выключения колеса у неё совпадают
    /// шестнадцать байт из восемнадцати, и в обеих половинах пары; различает только байт 16 — у
    /// выключения там жёсткая единица, здесь литеральный заполнитель. Значение угла пишется
    /// единственно в последний байт тела. Замок на это — <see cref="VeteranCollisionGuardTests"/>,
    /// и он написан раньше самого билдера.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(35, "4C6B41701601808080808080808080808023B4294A7A", "4C6441701601008080808080808080808023126C164F")]
    [InlineData(55, "4C6B41701601808080808080808080808037AEF39E07", "4C644170160100808080808080808080803708B6C232")]
    [InlineData(75, "4C6B4170160180808080808080808080804BF740A310", "4C644170160100808080808080808080804B5105FF25")]
    public void SetFallProtectionAngle_MatchesOfficialPairOfFrames(int degrees, string legacyHex, string modernHex)
    {
        byte[]? pair = Commands(VeteranOutgoingFrames.NewProtocolWheel()).BuildSetFallProtectionAngle(degrees);

        Assert.NotNull(pair);
        Assert.Equal(Convert.FromHexString(legacyHex + modernHex), pair);
        AssertFrame(legacyHex, pair[..22]);
        AssertFrame(modernHex, pair[22..]);
    }

    /// <summary>Семейству прошивок <c>004</c> (версия протокола 4, Patton) уходит только старая
    /// половина — та же развилка <c>sendBytesDataCombine</c>, что у тревоги скорости выше.</summary>
    [Fact]
    public void SetFallProtectionAngle_OnFirmwareFamily004_SendsLegacyHalfOnly()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(
            "dc5a5c452abe00003edc00008562003500000b5c",
            "0dfe000002bc07d00fac000219fb0000006f0000",
            "80808080808004000014ffffffffff32ee029109",
            "df0fd303cb000000006f9a79c2");
        Assert.Equal("004.0.12", harness.Snapshot().Version);

        AssertFrame("4C6B41701601808080808080808080808037AEF39E07", Commands(harness).BuildSetFallProtectionAngle(55));
    }

    // ==================== Границы диапазонов ====================

    /// <summary>
    /// Значение вне диапазона производителя не уходит к колесу вовсе: билдер отдаёт <c>null</c>, и
    /// <c>WheelService</c> штатно пишет «команда пропущена». Обрезать до края было бы хуже молчания —
    /// человек увидел бы «записано» там, где записалось не то, что он просил.
    /// </summary>
    [Fact]
    public void OutOfRange_ReturnsNull_NothingLeavesTheDecoder()
    {
        var wheel = NewWheel();

        Assert.Null(wheel.BuildSetKeyToneVolume(101));
        Assert.Null(wheel.BuildSetKeyToneVolume(-1));
        Assert.Null(wheel.BuildSetMaxChargeVoltage(121));
        Assert.Null(wheel.BuildSetAccelerationHelper(101));
        Assert.Null(wheel.BuildSetAccelerationReduction(101));
        Assert.Null(wheel.BuildSetBrakeOverpressureAlarm(79));
        Assert.Null(wheel.BuildSetBrakeOverpressureAlarm(126));
        Assert.Null(wheel.BuildSetVoltageCorrection(-16));
        Assert.Null(wheel.BuildSetVoltageCorrection(16));
        Assert.Null(wheel.BuildSetStopSpeed(9));
        Assert.Null(wheel.BuildSetStopSpeed(121));
        Assert.Null(wheel.BuildSetSpeedAlarm(9));
        Assert.Null(wheel.BuildSetSpeedAlarm(121));
        Assert.Null(wheel.BuildSetStopPower(29));
        Assert.Null(wheel.BuildSetStopPower(101));
        Assert.Null(wheel.BuildSetScreenBacklight(101));
        Assert.Null(wheel.BuildSetScreenBacklight(-1));
    }

    /// <summary>
    /// <b>Здесь мы строже производителя — осознанно</b> (<c>docs/port-deviations.md</c>). У него
    /// проверки диапазона угла нет вовсе: границу 35..75 задаёт единственно вид ползунка
    /// (<c>layout_set_safe_angle.xml:45</c>), а между ползунком и отправкой нет ни условия, ни
    /// обрезания, ни отказа — кадр уходит на отпускание пальца. У нас негодное значение кадра не
    /// рождает: команда на опкоде 22 стоит соседом выключения колеса, и «примерно верное» число
    /// здесь стоит дороже молчания.
    /// </summary>
    [Theory]
    [InlineData(34)]
    [InlineData(76)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(128)] // 0x80 — заполнитель тела, значением быть не может по построению кадра
    public void SetFallProtectionAngle_OutOfRange_ReturnsNull(int degrees) =>
        Assert.Null(NewWheel().BuildSetFallProtectionAngle(degrees));
}
