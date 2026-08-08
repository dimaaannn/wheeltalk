using WheelTalk.Core.Battery;

namespace WheelTalk.Core.Decoding;

/// <summary>
/// Раскладка кадра <c>RealTimeInfo</c> для InMotion P6 (<c>carType</c> 13/1). Не порт: этой модели
/// нет ни у нас, ни в самом WheelLog — раскладка восстановлена из дампа 02.08.2026, полный разбор
/// с доказательствами лежит в <c>docs/inmotion-p6-protocol.md</c>.
/// <para>
/// <b>Здесь только то, что доказано.</b> Кадр длиной 86 байт похож на V13, но совпадает с ним не
/// весь: <c>motPower</c>, <c>rollAngle</c> и температуры ЦП/IMU на местах V13 дают у P6 постоянные
/// или бессмысленные числа (0 °C, крен ровно 1,00° во всех шестидесяти кадрах). Такие поля не
/// пишутся вовсе — пустое место на панели честнее правдоподобного вранья, а именно из него на этом
/// колесе уже вышли двадцать несуществующих аварий и −176 °C.
/// </para>
/// <para>
/// Отсюда же и молчание ШИМ: тревоги по нему завязаны на число, которого мы не знаем. Его,
/// пробег в кадре, углы и битовые поля состояния закрывает один выезд — см. таблицу «под вопросом»
/// в том же документе.
/// </para>
/// </summary>
internal static class InMotionP6RealTime
{
    /// <summary>
    /// Ячеек в паке. Считано по напряжению: 230,04 В при 97,9 % заряда — 4,108 В на ячейку, и
    /// вторым замером 217 В при 63 % — 3,875 В. Обе точки сходятся только на 56.
    /// </summary>
    public const int Cells = 56;

    /// <summary>Длина данных кадра без байта команды. У P6 она постоянна.</summary>
    private const int DataLength = 86;

    /// <summary>
    /// Пишет в состояние доказанные поля кадра. <c>false</c> — кадр не той длины, состояние не
    /// тронуто: показать половину телеметрии хуже, чем не показать ничего.
    /// </summary>
    public static bool Apply(byte[] data, WheelState state)
    {
        if (data.Length < DataLength) return false;

        // Напряжение, ток и мощность связаны тождеством: смещение 16 во всех шестидесяти кадрах
        // дампа равно произведению первых двух до ватта. Три поля подтверждают друг друга, и это
        // самое твёрдое, что есть в этой раскладке.
        int voltage = MathsUtil.ShortFromBytesLE(data, 0);
        int current = MathsUtil.SignedShortFromBytesLE(data, 2);
        int batPower = MathsUtil.SignedShortFromBytesLE(data, 16);

        // Скорость сверена с одометром из кадра TotalStats: интеграл этого поля за 13,9 с дал
        // 50,7 м, одометр за то же время — 50 м. Расхождение 1 %.
        int speed = MathsUtil.SignedShortFromBytesLE(data, 8);

        // Два процента заряда, как у V13. Постоянны внутри одного дампа, но между замерами при
        // 217 В и при 230 В меняются — значит батарея, а не константа прошивки.
        int batLevel1 = MathsUtil.ShortFromBytesLE(data, 34);
        int batLevel2 = MathsUtil.ShortFromBytesLE(data, 36);

        // Смещение и шаг — общие для всей V2. Обе растут за время дампа монотонно, мотор быстрее
        // силовой части (+6 °C против +1 °C), как и должно быть после езды.
        int mosTemp = (data[58] & 0xff) + 80 - 256;
        int motTemp = (data[59] & 0xff) + 80 - 256;

        state.SetVoltage(voltage);
        state.SetCurrent(current);
        state.SetSpeed(speed);
        state.SetTopSpeed(speed);
        state.SetPower(batPower * 100);
        // Ряд идёт мимо каскада, потому что мимо него идёт и весь P6: настроек этот разбор не видит
        // (у него на руках только состояние), а число здесь не догадка — оно посчитано по двум
        // замерам напряжения с процентом. Источник — знание протокола, каким оно и является.
        state.SetBatteryLevel((int)Math.Round((batLevel1 + batLevel2) / 200.0),
            new CellCount(Cells, CellCountSource.Protocol));
        state.SetTemperature(mosTemp * 100);
        state.SetTemperature2(motTemp * 100);
        return true;
    }
}
