namespace WheelTalk.Core.Decoding;

/// <summary>
/// Запись настроек в колесо LeaperKim/Nosfet — то, чего у WheelLog не было вовсе.
/// <para>
/// Отдельным интерфейсом, а не полями <see cref="IWheelDecoder"/>: четырём остальным декодерам
/// (Gotway, KingSong, InMotion ×2) два десятка Veteran-специфичных методов не нужны и искажали бы
/// общий контракт для всех сразу. Тот же приём, что у <see cref="IPasswordProtected"/> — снаружи
/// находится через <c>as IVeteranSettingsCommands</c>.
/// </para>
/// <para>
/// <b>Источник — не WheelLog.</b> Ни один метод ниже не имеет аналога в <c>VeteranAdapter.java</c>;
/// байты взяты из декомпилята родного приложения производителя (<c>com.laoniao.leaperkim</c> 1.4.8,
/// разбор — <c>loeuc/leaperkim-official-app.md</c>). Поэтому реализация живёт во втором partial-файле
/// <see cref="VeteranDecoder"/>, а не в самом <c>VeteranDecoder.cs</c>: тот файл несёт расписку
/// «порт 1:1», и дописывать в него код без оригинала значило бы стереть эту гарантию для читателя.
/// </para>
/// <para>
/// <b>Почему методов много, а не один <c>WriteSetting(opcode, value)</c>.</b> Опкод в этом протоколе
/// не уникален: 17, 18, 20, 22 и 25 обслуживают по две-три разные команды, различаясь лишь байтами
/// 5-6 (<c>leaperkim-official-app.md</c> §4.2). Параметризуемая запись впустила бы произвольный
/// опкод снаружи — то есть дала бы UI шанс собрать «выключить колесо» вместо «записать угол». Здесь
/// вызвать можно только то, для чего есть именованный метод.
/// </para>
/// <para>
/// Все методы возвращают <c>null</c> на значении вне диапазона производителя — негодное число не
/// уходит к колесу вовсе, а <c>WheelService</c> пишет в журнал «команда пропущена».
/// </para>
/// </summary>
public interface IVeteranSettingsCommands
{
    // --- Очередь A: опкод без коллизии (leaperkim-official-app.md §4.1) ---

    /// <summary>Единицы на экране колеса: <c>true</c> — мили, <c>false</c> — километры.
    /// Опкод 23/<c>0x17</c>, <c>ControlActivity.java:443</c>, <c>UnitSwitchActivity.java:76</c>.</summary>
    byte[] BuildSetUnitSystem(bool miles);

    /// <summary>Режим высокой скорости, тумблер. Опкод 26/<c>0x1A</c>, <c>ControlActivity.java:451</c>.</summary>
    byte[] BuildSetHighSpeedMode(bool enabled);

    /// <summary>Громкость звука кнопок, 0..100. Опкод 28/<c>0x1C</c>, <c>KeyToneSettingActivity.java:30</c>.</summary>
    byte[]? BuildSetKeyToneVolume(int percent);

    /// <summary>Предел заряда, 0..120. Опкод 29/<c>0x1D</c>, <c>MaxChargePowerSettingActivity.java:31</c>.</summary>
    byte[]? BuildSetMaxChargeVoltage(int value);

    /// <summary>Помощник разгона/торможения, 0..100. Опкод 31/<c>0x1F</c>,
    /// <c>SetUpDownSpwwdHelpActivity.java:30</c>.</summary>
    byte[]? BuildSetAccelerationHelper(int percent);

    /// <summary>Снижение отклика акселерометра, 0..100. Опкод 33/<c>0x21</c>,
    /// <c>SetUpSpeedCulActivity.java:30</c>.</summary>
    byte[]? BuildSetAccelerationReduction(int percent);

    /// <summary>Тревога по перетормаживанию, 80..125. Опкод 34/<c>0x22</c>,
    /// <c>BrakeSettingActivity.java:30</c>.</summary>
    byte[]? BuildSetBrakeOverpressureAlarm(int percent);

    /// <summary>Поправка напряжения, −15..15 десятых процента (колесо делит на 10). Опкод 24/<c>0x18</c>,
    /// <c>VolLightSettingActivity.java:31</c>.</summary>
    byte[]? BuildSetVoltageCorrection(int tenthsOfPercent);

    // --- Очередь B: опкод делится с чужой командой, различие в байте 6 (§4.2) ---

    /// <summary>Порог отбоя педалей (tiltback), 10..120 км/ч. Опкод 17/<c>0x11</c>, b6=2 — тот же
    /// опкод, что у тревоги скорости (b6=0). <c>StopSpeedSettingActivity.java:42</c>.</summary>
    byte[]? BuildSetStopSpeed(int speedKmh);

    /// <summary>Тревога по скорости, 10..120 км/ч. Опкод 17/<c>0x11</c>, парный кадр — тот же опкод,
    /// что у отбоя педалей. <c>SetAlarmSpeedActivity.java:67</c>.</summary>
    byte[]? BuildSetSpeedAlarm(int speedKmh);

    /// <summary>Порог ШИМ, после которого колесо отбивает педали, 30..100 %. Опкод 18/<c>0x12</c>,
    /// b6=2 — тот же опкод, что у служебной синхронизации времени (b5/b6 = 0/5).
    /// <c>StopPowerSettingActivity.java:30</c>.</summary>
    byte[]? BuildSetStopPower(int percent);

    /// <summary>Яркость экрана колеса, 0..100 %. Опкод 20/<c>0x14</c>, b6=2 — тот же опкод, что у
    /// служебного чтения журнала (b6=0). <c>ScreenBacklightSettingActivity.java:30</c>.</summary>
    byte[]? BuildSetScreenBacklight(int percent);

    /// <summary>Режим низкого напряжения, тумблер. Опкод 25/<c>0x19</c>, b5/b6 = 1/2 —
    /// <c>ControlActivity.java:446-448</c>, вызов <c>:352</c> (<c>z ? 1 : 0</c>). Тот же опкод носит
    /// запись пароля с b5/b6 = <b>0/5</b> (<c>Util.genPwdCmd</c>, <c>Util.java:257-273</c>: она берёт
    /// построитель синхронизации времени и прибавляет к его опкоду 7, оттого и b5/b6 у неё чужие).
    /// Пароль запрещён навсегда (план §8) — но запрещена комбинация, а не опкод.</summary>
    byte[] BuildSetLowVoltageMode(bool enabled);

    // --- Педали: жёсткость и поколение (план §1.6, §5; архитектура настроек §7) ---

    /// <summary>Жёсткость педалей плавной шкалой, 0..100. Опкод 15/<c>0x0F</c>, b6=2 —
    /// <c>PedalSoftnessSettingActivity.java:37</c>. Шкала у этого опкода всегда плавная, поколение
    /// колеса здесь ни при чём; принимает ли колесо эту настройку вообще, скажет сентинел
    /// <c>128</c> входящего кадра настроек, когда тот будет разобран.</summary>
    byte[]? BuildSetPedalHardness(int percent);

    /// <summary>Каким видом принимает это колесо <b>«режим езды»</b> — три положения или плавную
    /// шкалу (<see cref="RideModeScales.FromProtocolVersion"/> по версии протокола из телеметрии).
    /// Настройка это отдельная от жёсткости педалей и на другом опкоде; спрашивает признак тот, кто
    /// решает, какой <b>вид</b> строки показать, а не какую команду разрешить.</summary>
    RideModeScale RideModeScale { get; }
}
