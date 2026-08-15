using System.Text;
using WheelTalk.Core.Decoding;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Byte-level lock on Veteran/LeaperKim outgoing commands — план 35, этап 8. Snapshot of what
/// <see cref="VeteranDecoder"/>'s six command builders <b>actually</b> send today, sverено с
/// <c>docs/originals-reference-data.md</c> §7 (сводная таблица LeaperKim). No behaviour changes
/// here — a mismatch with §7 is recorded in a comment and left for the owner, per plan 35 §8.
/// </summary>
public class VeteranCommandBytesTests
{
    private static VeteranDecoder ProtocolDecoder(DecoderHarness harness) => (VeteranDecoder)harness.Decoder.ProtocolDecoder;

    // --- Beep (§7.2:469, opcode 14/0x0E) ---

    /// <summary>
    /// Legacy branch (_protocolVersion &lt; 3): plain ASCII "b". §7's table only documents the
    /// binary opcode-14 form (родное приложение производителя, новая прошивка) — the legacy ASCII
    /// path has no row of its own in §7, and neither veteran-loeuc-comparison.md nor §7 names an
    /// opcode for it. Locking current behaviour, not asserting §7 coverage.
    /// </summary>
    [Fact]
    public void Beep_LegacyProtocol_SendsAsciiB()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex( // Abrams, protocol version 2 — Decodes_abrams fixture
            "dc5a5c20266d00004aaf00004aaf000000000d9e",
            "0b8800000af00af007d2000300050004");

        byte[] beep = ProtocolDecoder(harness).BuildWheelBeep();

        Assert.Equal(Encoding.ASCII.GetBytes("b"), beep);
    }

    /// <summary>
    /// New-protocol branch (_protocolVersion &gt;= 3): binary `Lk` frame, opcode 14/0x0E — matches
    /// §7.2:469 ("Гудок/сигнал («Alarm»)", `BtManager.java:73,82`) byte for byte. Header is `Lk`
    /// (`4C 6B 41 70`), not `Ld` — §7's own note that the "value-less" commands go out as a pair
    /// (`Lk`+`Ld`); this frame is the `Lk` half. Also locks the "frame length == opcode byte"
    /// invariant from §7 preamble (line 434): 14 bytes total, opcode byte = 14.
    /// </summary>
    [Fact]
    public void Beep_NewProtocol_SendsOpcode14LkFrame()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex( // Sherman L, protocol version 6 — Decodes_sherman_l fixture
            "dc5a5c53397afffe0aa400000df10000000a0b3d",
            "0e0e0000037a035217730064000e00b480c80000",
            "808080808080058080808080800ff30ff50ff50f",
            "f50ff50fef0ff20ff30ff30ff30ff30fed0ff30f",
            "f40ff5378c5145");

        byte[] beep = ProtocolDecoder(harness).BuildWheelBeep();

        Assert.Equal(new byte[] { 0x4c, 0x6b, 0x41, 0x70, 0x0e, 0x00, 0x80, 0x80, 0x80, 0x01, 0xca, 0x87, 0xe6, 0x6f }, beep);
        Assert.Equal((byte)'L', beep[0]);
        Assert.Equal((byte)'k', beep[1]);
        Assert.Equal(0x0e, beep[4]); // opcode 14
        Assert.Equal(14, beep.Length); // §7 invariant: frame length == opcode byte value
    }

    // --- Reset trip (§7.2:470 / §7.3:488, opcode 11 old / 13 new) ---

    /// <summary>
    /// РАСХОДИТСЯ с §7.2:470/§7.3:488 — на разбор. §7 says the wheel's own generations split this
    /// command by opcode (`CMD_CLEAR_METER` old = 11 via `Lk`, `CMD_CLEAR_METER_NEW` new = 13 via
    /// `Ld`, colliding with the light opcode, §7.3:488). VeteranDecoder sends none of that: always
    /// literal ASCII "CLEARMETER", unconditional on `_protocolVersion` — no branch at all, unlike
    /// <see cref="VeteranDecoder.BuildWheelBeep"/> which does split on protocol generation. Whether
    /// new-generation wheels still accept the legacy ASCII string is unverified here — this test
    /// pins current behaviour only.
    /// </summary>
    [Fact]
    public void ResetTrip_SendsAsciiClearMeter()
    {
        var harness = DecoderHarness.ForVeteran();

        byte[]? resetTrip = ProtocolDecoder(harness).BuildResetTrip();

        Assert.Equal(Encoding.ASCII.GetBytes("CLEARMETER"), resetTrip);
    }

    // --- Light on/off (§7.2:468 / §7.3:488, opcode 13/0x0D) ---

    /// <summary>
    /// РАСХОДИТСЯ с §7.2:468/§7.3:488 — на разбор, тот же характер, что и сброс поездки: §7
    /// documents a binary opcode-13 pair frame for the light toggle (colliding with the new
    /// CLEARMETER opcode, distinguished only by byte 5 — §7.3:488). VeteranDecoder always sends
    /// literal ASCII "SetLightON"/"SetLightOFF", no protocol-version branch. This *is* independently
    /// corroborated by LoEUC (veteran-loeuc-comparison.md:54, "Побайтно совпадает") — two
    /// independent ports agree on ASCII — but that only proves both ports agree with each other, not
    /// necessarily with the newer official-app encoding §7 describes.
    /// </summary>
    [Fact]
    public void SetLightState_On_SendsAsciiSetLightOn()
    {
        var harness = DecoderHarness.ForVeteran();
        var decoder = ProtocolDecoder(harness);

        byte[] frame = decoder.BuildSetLightState(true);

        Assert.Equal(Encoding.ASCII.GetBytes("SetLightON"), frame);
        Assert.True(harness.Config.LightEnabled);
    }

    [Fact]
    public void SetLightState_Off_SendsAsciiSetLightOff()
    {
        var harness = DecoderHarness.ForVeteran();
        var decoder = ProtocolDecoder(harness);

        byte[] frame = decoder.BuildSetLightState(false);

        Assert.Equal(Encoding.ASCII.GetBytes("SetLightOFF"), frame);
        Assert.False(harness.Config.LightEnabled);
    }

    /// <summary>
    /// No separate "high beam" command exists on the wire (§7 has no such row either) — switching
    /// the flashlight just inverts the single light state and re-sends the same on/off text.
    /// Matches veteran-loeuc-comparison.md:55.
    /// </summary>
    [Fact]
    public void SwitchFlashlight_TogglesConfiguredLightState()
    {
        var harness = DecoderHarness.ForVeteran(config => config.LightEnabled = false);
        var decoder = ProtocolDecoder(harness);

        byte[] firstToggle = decoder.BuildSwitchFlashlight();
        Assert.Equal(Encoding.ASCII.GetBytes("SetLightON"), firstToggle);
        Assert.True(harness.Config.LightEnabled);

        byte[] secondToggle = decoder.BuildSwitchFlashlight();
        Assert.Equal(Encoding.ASCII.GetBytes("SetLightOFF"), secondToggle);
        Assert.False(harness.Config.LightEnabled);
    }

    // --- Pedals mode / Ride mode (§7.2:471, opcode 12/0x0C) ---

    /// <summary>
    /// Text presets for old (pre-L/S) Sherman wheels — matches §7.2:471 ("Ride mode, 3 уровня
    /// (SETs/SETm/SETh, старые Sherman)", `BtManager.java:77-79,86-88`) in kind: three named ASCII
    /// presets, no numeric byte. This is <b>not</b> a discrepancy — the owner already closed the
    /// question of matching preset order to the new wheels' 0..100 `pedalHardness` scale
    /// (veteran-loeuc-comparison.md:154-165): different generations, not two encodings of one
    /// setting. Locking only the three ASCII strings and the null fallback for out-of-range modes.
    /// </summary>
    [Theory]
    [InlineData(0, "SETh")]
    [InlineData(1, "SETm")]
    [InlineData(2, "SETs")]
    public void UpdatePedalsMode_SendsNamedAsciiPreset(int mode, string expectedAscii)
    {
        var harness = DecoderHarness.ForVeteran();

        byte[]? frame = ProtocolDecoder(harness).BuildUpdatePedalsMode(mode);

        Assert.Equal(Encoding.ASCII.GetBytes(expectedAscii), frame);
    }

    [Fact]
    public void UpdatePedalsMode_OutOfRange_ReturnsNull()
    {
        var harness = DecoderHarness.ForVeteran();

        Assert.Null(ProtocolDecoder(harness).BuildUpdatePedalsMode(3));
    }

    // --- Calibrate (§7.4:503-513, opcode 21/0x15) ---

    /// <summary>
    /// РАСХОДИТСЯ с §7.4 — на разбор, но a gap rather than a byte mismatch. §7.4 documents a real
    /// wire command for gyro calibration in the official app (fixed `Ld` frame, opcode 21, value
    /// always `1` — `GyroscopeSettingActivity.java:121-123`). VeteranDecoder sends nothing at all:
    /// `BuildCalibrate()` is an unconditional null, because the WheelLog port `VeteranAdapter`
    /// never overrides `wheelCalibration()`. veteran-loeuc-comparison.md:59 treats this as
    /// corroborated by LoEUC also lacking a calibration path — but §7.4 shows the *official* app
    /// does have one, so "both ports agree" is agreement on an omission, not proof the omission is
    /// harmless. Locking current (no-op) behaviour only.
    /// </summary>
    [Fact]
    public void Calibrate_ReturnsNull_NoWireBytes()
    {
        var harness = DecoderHarness.ForVeteran();

        Assert.Null(ProtocolDecoder(harness).BuildCalibrate());
    }

    // --- §7.5 lock: dangerous/service commands never leave this decoder ---

    /// <summary>
    /// Замок на §7.5 ("Служебные и опасные команды"): чтение журнала (opcode 20/0x14, коллизия со
    /// `screenBacklightRate`), пароль/блокировка (opcode 25/0x19, переиспользует билдер
    /// синхронизации времени) и вход в режим прошивки (сырой текст `"AT+RINTOPRO"`) не должны
    /// уходить с этим декодером ни при каком построителе. VeteranDecoder не реализует ни один
    /// численный `Ld`-кадр настроек вообще (только фиксированный опкод-14 бип), так что замок
    /// сегодня тривиален — тест фиксирует это как инвариант на будущее: появится билдер настройки,
    /// эта проверка первой заметит опкод 20/25 или текст прошивки, случайно ушедший на волю.
    /// </summary>
    [Fact]
    public void NeverEmits_ServiceOrFirmwareOpcodes()
    {
        var harness = DecoderHarness.ForVeteran();
        var decoder = ProtocolDecoder(harness);
        harness.FeedHex( // Sherman L — puts the decoder on the "new protocol" branch too
            "dc5a5c53397afffe0aa400000df10000000a0b3d",
            "0e0e0000037a035217730064000e00b480c80000",
            "808080808080058080808080800ff30ff50ff50f",
            "f50ff50fef0ff20ff30ff30ff30ff30fed0ff30f",
            "f40ff5378c5145");

        byte[]?[] allOutgoing =
        [
            decoder.BuildWheelBeep(),
            decoder.BuildResetTrip(),
            decoder.BuildSetLightState(true),
            decoder.BuildSetLightState(false),
            decoder.BuildSwitchFlashlight(),
            decoder.BuildUpdatePedalsMode(0),
            decoder.BuildUpdatePedalsMode(1),
            decoder.BuildUpdatePedalsMode(2),
            decoder.BuildCalibrate(),
        ];

        byte[] firmwareEntry = Encoding.ASCII.GetBytes("AT+RINTOPRO");
        foreach (byte[]? frame in allOutgoing)
        {
            if (frame is null) continue;

            Assert.DoesNotContain(firmwareEntry, ElevenByteWindows(frame));
            if (frame.Length >= 5 && frame[0] == 'L' && (frame[1] == 'k' || frame[1] == 'd') && frame[2] == 'A' && frame[3] == 'p')
            {
                Assert.NotEqual(20, frame[4]); // CMD_READ_LOG (§7.5:519)
                Assert.NotEqual(25, frame[4]); // genPwdCmd (§7.5:520)
            }
        }
    }

    /// <summary>Helper for the firmware-string containment check above: yields every contiguous
    /// 11-byte window so `Assert.DoesNotContain` (which needs matching element types) can compare
    /// windows against the 11-byte "AT+RINTOPRO" needle without a substring search over byte[].</summary>
    private static IEnumerable<byte[]> ElevenByteWindows(byte[] frame)
    {
        const int needleLength = 11; // "AT+RINTOPRO".Length
        for (int i = 0; i + needleLength <= frame.Length; i++)
        {
            yield return frame[i..(i + needleLength)];
        }
    }
}
