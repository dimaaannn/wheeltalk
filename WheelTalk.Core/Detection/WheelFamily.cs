namespace WheelTalk.Core.Detection;

/// <summary>
/// Семейство колеса, опознанное по дереву GATT. Это не то же самое, что
/// <see cref="Contracts.WheelProtocol"/>: у Begode и Veteran **одно** семейство на двоих
/// (профиль `FFE0`/`FFE1` у них общий), и какой из двух протоколов перед нами, решает уже первый
/// пришедший кадр. Имена — из таблицы оригинала (`bluetooth_services.json`, ключ `adapter`).
/// </summary>
public enum WheelFamily
{
    /// <summary>Begode/Gotway и Veteran — общий профиль, разделяются по заголовку кадра.</summary>
    Gotway,

    KingSong,
    InMotion,
    InMotionV2,
    Ninebot,
    NinebotZ,
}
