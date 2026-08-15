namespace WheelTalk.Core.Decoding;

/// <summary>
/// Словарь кодов неисправностей колеса KingSong — 66 записей, диапазон 101–2235. Источник:
/// <c>docs/originals-data/ks-troublecode.json</c> (сервер производителя, запрос
/// <c>api/equipment/troublecode</c>, снято 15.08.2026 — разбор и оговорки в
/// <c>docs/kingsong-trouble-codes.md</c> §1). Данные, не код — тот же приём, что и
/// <see cref="InMotionP6DiagnosticFlags"/>: таблица лежит статикой в C#, не читается из ресурса на
/// каждый разбор кадра. Тексты — дословно английские, перевода в ответе сервера нет (см. источник).
/// <para>
/// BMS-словарь (<c>ks-bmstroublecode.json</c>, 34 записи) сюда намеренно не включён: код BMS живёт
/// в отдельной нумерации кадров (<c>0xF1/0xF2</c>-страницы, см. класс-doc <see cref="KingsongDecoder"/>),
/// которые этот декодер не разбирает вовсе — заводить словарь без потребителя было бы мёртвым
/// грузом. Пригодится, если BMS-страницы когда-нибудь будут портированы.
/// </para>
/// </summary>
internal static class KingsongTroubleCodes
{
    private static readonly IReadOnlyDictionary<int, string> ByCode = new Dictionary<int, string>
    {
        [101] = "E3-Hall Sensor Error",
        [102] = "Ir-Over Current",
        [103] = "E1-Block Error",
        [105] = "be voltage detect need calibration",
        [107] = "over ODC",
        [114] = "BMS warning",
        [115] = "BMS ALARM",
        [116] = "BMS no data",
        [117] = "BL-Low Voltage.",
        [118] = "BH-Over Voltage V1",
        [119] = "E5-Throttle Handle Error",
        [120] = "E4-Brake Handle Error",
        [121] = "H1-Motor High Temperature",
        [122] = "H2-MOS High Temperature T1",
        [123] = "H3-MOS High Temperature T2",
        [125] = "S1-Motor phase short circuit or the main board output current is too large",
        [126] = "S2-Drive Cruit is not functioning correctly, please restart or replace drive circuit.",
        [127] = "E0-Instrument communication failure, please check if communication is in working order.",
        [128] = "Sn-Wrong serial number",
        [131] = "BP-Over Voltage V2",
        [132] = "bms err",
        [201] = "Motor Hall sensor error, please check the hall ensor and repair accordingly.",
        [202] = "Over Current or locked rotor.",
        [203] = "The motor is blocked, please check whether the motor is rotating smoothly or remove obstacles before riding.",
        [205] = "Drive Cruit is not functioning correctly, please restart or replace drive circuit.",
        [206] = "The motherboard output wire has short circuited, please check whether the battery output wire has short circuited or whether the motherboard MOS is damaged.",
        [207] = "Gyroscope failure, please replace motherboard.",
        [208] = "the coef of batvol have no just",
        [213] = "bms setting err",
        [217] = "Motor Hall sensor error, please check the hall ensor and repair accordingly.",
        [218] = "The output power is too high, please do not accelerate or climb steep slopes in a hurry (please check whether the power alarm parameter settings are too low)",
        [219] = "Device is outputting at max.",
        [220] = "Motherboard output over current. Please ride with caution.",
        [221] = "Motor is experiencing high temperature, please allow Motor to cool down before riding again.",
        [222] = "MOS is experiencing high temperature, please allow MOS to cool down before riding again.",
        [223] = "Charging is over voltage or over current.",
        [224] = "The battery reaches the preset charging value, Adjust the charging ratio to 100%",
        [225] = "bms charg high temperature",
        [226] = "BMS Warnning",
        [227] = "BMS get no data",
        [228] = "Serial Number Error.",
        [229] = "Low voltage, please charge your device!",
        [230] = "Reserve power is missing, please replace motherboard.",
        [231] = "Overvoltage, please beware of your safety and avoid riding downhill.",
        [232] = "Lift switch is out of order, please release the handlebar or check whether the lift switch sensor has experienced a short circuit.",
        [233] = "BMS CELL OVER VOL",
        [234] = "Battery High Temperature",
        [235] = "BMS mode version wrong",
        [1209] = "mttool,vol err",
        [1210] = "mttool,over time",
        [1211] = "mttool,block err",
        [1212] = "mttool,speed err",
        [2222] = "The output current is at max, please ride with caution.",
        [2223] = "The motherboard temperature is too high, please stop and ride after the it has cooled down.",
        [2224] = "The motor temperature is too high, please stop and ride after the it has cooled down.",
        [2225] = "No serial number or serial number error",
        [2226] = "Please check if the motor hall line connection and is functioning normally.",
        [2227] = "The output current of the main board has exceeded. Please check if the motor is damaged or if the phase line is shorted.",
        [2228] = "Gyroscope error, please contact your seller and replacement motherboard.",
        [2229] = "Low battery, please charge.",
        [2230] = "The voltage is too high, please remove the charger.",
        [2231] = "The voltage is too high, please do not ride downhill for an extended time.",
        [2232] = "Sensor A is not connected or the sensor is damaged, the sensor has been closed for use.",
        [2233] = "Sensor data A is reversed, this sensor is closed.",
        [2234] = "Sensor B is not connected or the sensor is damaged, the sensor has been closed for use.",
        [2235] = "Sensor data is reversed, or line fault, this sensor has now been turned off.",
    };

    /// <summary>Непустой известный код → его текст; неизвестный или нулевой → false (вызывающий
    /// решает, что показать — ноль это тишина, неизвестный это номер плюс строка в журнал).</summary>
    public static bool TryGetText(int code, out string text) => ByCode.TryGetValue(code, out text!);
}
