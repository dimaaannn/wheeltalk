using WheelTalk.Core.Decoding;

namespace WheelTalk.Tests.TestSupport;

/// <summary>
/// Всё, что <see cref="VeteranDecoder"/> вообще способен отправить в колесо, — единым списком для
/// замков. Список общий нарочно: замки (коллизии опкодов и служебные команды) обязаны мести один и
/// тот же набор, иначе новый билдер попадёт под один замок и проскочит мимо другого.
/// <para>
/// <b>Сюда обязан попадать каждый новый билдер.</b> Забыть — значит тихо снять с него оба замка.
/// </para>
/// </summary>
public static class VeteranOutgoingFrames
{
    /// <summary>Sherman L, версия протокола 6 — фикстура <c>Decodes_sherman_l</c>. Ставит декодер на
    /// «новую» ветку протокола: там бип уходит бинарным кадром, а не ASCII, и парные команды уходят
    /// обеими половинами. То есть худший случай для замка — байт на проводе больше всего.</summary>
    public static DecoderHarness NewProtocolWheel()
    {
        var harness = DecoderHarness.ForVeteran();
        harness.FeedHex(
            "dc5a5c53397afffe0aa400000df10000000a0b3d",
            "0e0e0000037a035217730064000e00b480c80000",
            "808080808080058080808080800ff30ff50ff50f",
            "f50ff50fef0ff20ff30ff30ff30ff30fed0ff30f",
            "f40ff5378c5145");
        return harness;
    }

    /// <summary>Кадры записи настроек — по каждому допустимому значению каждой команды, а не по
    /// одному образцу: замки утверждают своё на всём диапазоне.</summary>
    public static IEnumerable<byte[]> EverySettingsWrite(IVeteranSettingsCommands wheel)
    {
        yield return wheel.BuildSetUnitSystem(true);
        yield return wheel.BuildSetUnitSystem(false);
        yield return wheel.BuildSetHighSpeedMode(true);
        yield return wheel.BuildSetHighSpeedMode(false);
        yield return wheel.BuildSetLowVoltageMode(true);
        yield return wheel.BuildSetLowVoltageMode(false);
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

    /// <summary>Команды старого порта <c>VeteranAdapter</c> — те, что были у нас до записи настроек.
    /// Часть из них уходит текстом, а не кадром (<c>CLEARMETER</c>, <c>SetLightON</c>), и замок на
    /// служебные команды обязан видеть и их: вход в режим прошивки — тоже текст.</summary>
    public static IEnumerable<byte[]> EveryPortedCommand(VeteranDecoder decoder)
    {
        yield return decoder.BuildWheelBeep();
        yield return decoder.BuildResetTrip()!;
        yield return decoder.BuildSetLightState(true);
        yield return decoder.BuildSetLightState(false);
        yield return decoder.BuildSwitchFlashlight();
        for (int mode = 0; mode <= 2; mode++) yield return decoder.BuildUpdatePedalsMode(mode)!;
        if (decoder.BuildCalibrate() is { } calibrate) yield return calibrate; // сегодня всегда null
    }

    /// <summary>Оба набора разом — то, что уходит на провод этим декодером сегодня.</summary>
    public static IEnumerable<byte[]> Everything(VeteranDecoder decoder) =>
        EveryPortedCommand(decoder).Concat(EverySettingsWrite(decoder));
}
