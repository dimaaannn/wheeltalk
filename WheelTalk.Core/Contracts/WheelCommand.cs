namespace WheelTalk.Core.Contracts;

/// <summary>
/// Generic command contract (input) — discriminated union standing in for the 41
/// <c>open fun</c> methods of Android <c>BaseAdapter</c>. Only the variants actually
/// implemented by the Veteran slice (§5.5 of the port plan) are handled by
/// <c>VeteranDecoder</c>; everything else is a no-op for now.
/// </summary>
public abstract record WheelCommand
{
    public sealed record Beep : WheelCommand;
    public sealed record SetLight(bool Enabled) : WheelCommand;
    public sealed record SwitchFlashlight : WheelCommand;
    public sealed record SetPedalsMode(int Mode) : WheelCommand;
    public sealed record ResetTrip : WheelCommand;
    public sealed record Calibrate : WheelCommand;

    // --- Запись настроек в колесо (пока умеет только Veteran/LeaperKim, см. IVeteranSettingsCommands) ---
    //
    // Свой тип на каждую настройку, а не общий WriteSetting(opcode, value): опкод в протоколе
    // LeaperKim не уникален — 17, 18, 20, 22 и 25 обслуживают по две-три разные команды. Общий тип
    // впустил бы произвольный опкод снаружи, то есть дал бы экрану шанс собрать «выключить колесо»
    // там, где хотели «записать угол». Закрытое объединение делает это недостижимым.

    /// <summary>Единицы на экране колеса: мили или километры.</summary>
    public sealed record SetUnitSystem(bool Miles) : WheelCommand;
    public sealed record SetHighSpeedMode(bool Enabled) : WheelCommand;
    /// <summary>Громкость звука кнопок, 0..100.</summary>
    public sealed record SetKeyToneVolume(int Percent) : WheelCommand;
    /// <summary>Предел заряда, 0..120.</summary>
    public sealed record SetMaxChargeVoltage(int Value) : WheelCommand;
    /// <summary>Помощник разгона/торможения, 0..100.</summary>
    public sealed record SetAccelerationHelper(int Percent) : WheelCommand;
    /// <summary>Снижение отклика акселерометра, 0..100.</summary>
    public sealed record SetAccelerationReduction(int Percent) : WheelCommand;
    /// <summary>Тревога по перетормаживанию, 80..125.</summary>
    public sealed record SetBrakeOverpressureAlarm(int Percent) : WheelCommand;
    /// <summary>Поправка напряжения, −15..15 десятых процента.</summary>
    public sealed record SetVoltageCorrection(int TenthsOfPercent) : WheelCommand;
    /// <summary>Порог отбоя педалей (tiltback), 10..120 км/ч.</summary>
    public sealed record SetStopSpeed(int SpeedKmh) : WheelCommand;
    /// <summary>Тревога по скорости, 10..120 км/ч.</summary>
    public sealed record SetSpeedAlarm(int SpeedKmh) : WheelCommand;
    /// <summary>Порог ШИМ, после которого колесо отбивает педали, 30..100 %.</summary>
    public sealed record SetStopPower(int Percent) : WheelCommand;
    /// <summary>Яркость экрана колеса, 0..100 %.</summary>
    public sealed record SetScreenBacklight(int Percent) : WheelCommand;
    /// <summary>Режим низкого напряжения (тумблер) — опкод 25, тот же, что у записи пароля.</summary>
    public sealed record SetLowVoltageMode(bool Enabled) : WheelCommand;

    /// <summary>Жёсткость педалей плавной шкалой, 0..100 — вторая, «новая» форма той же настройки,
    /// что <see cref="SetPedalsMode"/> задаёт тремя положениями. Обе вместе колесу не шлются: какая
    /// из двух ему подходит, решает <c>PedalGeneration</c> (план §5.3).</summary>
    public sealed record SetPedalHardness(int Percent) : WheelCommand;
}
