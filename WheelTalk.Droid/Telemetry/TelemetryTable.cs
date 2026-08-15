using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Core.Battery;
using WheelTalk.Core.Contracts;
using WheelTalk.Droid.Resources.Strings;

using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Droid.Ui;

namespace WheelTalk.Droid.Telemetry;

/// <summary>
/// Величины снимка телеметрии, разложенные по разделам. Живёт отдельно от экранов, потому что
/// экранов два: «Данные» показывают этим живое колесо, плеер — записанную поездку. Список полей в
/// двух копиях разошёлся бы при первом же добавленном поле, а расходиться ему нельзя: это одна и та
/// же телеметрия, просто из разных источников.
/// <para>
/// <b>Показывается только то, что это колесо действительно присылает.</b> Протоколы заполняют
/// разные части снимка: у Veteran есть наклон, зарядка и таймер автовыключения, но нет температуры
/// двигателя, тревоги и текстовых сообщений; у Begode наоборот. Раньше показывались все поля
/// сразу, и половина таблицы стояла нулями — по такой таблице нельзя отличить «колесо прислало
/// ноль» от «колесо про это не говорит вовсе». Что кому принадлежит — из самих декодеров
/// (<c>VeteranDecoder.Decode</c>, <c>GotwayDecoder</c>), а не из предположений.
/// </para>
/// <para>
/// Банки BMS проверяются не по протоколу, а по данным: у Veteran они появляются с 5-й версии
/// протокола, у Begode приходят своими кадрами, и в обоих случаях «пакет молчит» видно по нулевому
/// напряжению.
/// </para>
/// </summary>
internal sealed class TelemetryTable
{
    /// <param name="Shown">
    /// Присылает ли это поле колесо, с которого снят снимок. Проверяется на каждом кадре, а не один
    /// раз при сборке: тип колеса известен только после первого кадра, а плеер за одну сессию может
    /// показать записи с разных колёс.
    /// </param>
    private sealed record Field(
        string Section,
        string Label,
        Func<TelemetrySnapshot, string> Read,
        Func<TelemetrySnapshot, bool> Shown);

    private static bool Always(TelemetrySnapshot s) => true;

    /// <summary>Пакет отвечает: молчащий BMS отдаёт нули по всему, включая напряжение.</summary>
    private static bool HasBms(SmartBms bms) => bms.Voltage > 0 || bms.MaxCell > 0;

    /// <summary>Кто из марок кладёт в пакет банки 1—4 температуры (Sherman L все шесть сразу, Begode
    /// только эти четыре, P6 — по ответу платы). Кто из семейств что сообщает — <see cref="WheelReports"/>,
    /// здесь лишь объединение трёх маркеров ради одного предиката на два поля.</summary>
    private static bool ReportsTemp1To4(TelemetrySnapshot s) =>
        WheelReports.Veteran(s) || WheelReports.Gotway(s) || WheelReports.InMotionV2(s);

    /// <summary>Банки 5—6 — только там, где хвост ответа шире четырёх датчиков: Begode пишет ровно
    /// четыре и на пятой с шестой навсегда остаётся 0, показывать которые было бы неправдой.</summary>
    private static bool ReportsTemp5And6(TelemetrySnapshot s) => WheelReports.Veteran(s) || WheelReports.InMotionV2(s);

    /// <summary>Слово источника ряда ячеек для человека — тот же словарь, что у кнопки «рассчитать»
    /// (<c>ExperimentalPage.CellsSourceName</c>), плюс ступень «задано вручную», которой кнопка не
    /// видит никогда (она берёт ответ без верхней ступени каскада).</summary>
    private static string CellsSourceWord(CellCountSource source) => source switch
    {
        CellCountSource.UserSetting => AppStrings.SettingCellsSourceManual,
        CellCountSource.SmartBms => AppStrings.SettingCellsSourceBms,
        CellCountSource.Protocol => AppStrings.SettingCellsSourceProtocol,
        CellCountSource.VoltageWithPercent => AppStrings.SettingCellsSourcePercent,
        _ => AppStrings.SettingCellsSourceGuess,
    };

    private static readonly Field[] Fields =
    [
        new(AppStrings.TelemetrySectionMotion, AppStrings.TelemetrySpeed,
            s => string.Format(AppStrings.ValueSpeedKmh, s.SpeedKmh), Always),
        new(AppStrings.TelemetrySectionMotion, AppStrings.TelemetryTopSpeed,
            s => string.Format(AppStrings.ValueSpeedKmh, s.TopSpeedKmh), Always),
        new(AppStrings.TelemetrySectionMotion, AppStrings.MetricSpeedLimit,
            s => string.Format(AppStrings.ValueSpeedKmh, s.SpeedLimit), WheelReports.KingSong),
        new(AppStrings.TelemetrySectionMotion, AppStrings.TelemetryPwm,
            s => $"{s.Pwm:F1} %", Always),
        new(AppStrings.TelemetrySectionMotion, AppStrings.TelemetryMaxPwm,
            s => $"{s.MaxPwm:F1} %", Always),
        new(AppStrings.TelemetrySectionMotion, AppStrings.MetricHardwarePwm,
            s => $"{s.Output} %", WheelReports.KingSong),
        new(AppStrings.TelemetrySectionMotion, AppStrings.TelemetryAngle,
            s => $"{s.Angle:F1} °", WheelReports.Veteran),
        new(AppStrings.TelemetrySectionMotion, AppStrings.MetricRoll,
            s => $"{s.Roll:F1} °", WheelReports.InMotion),

        new(AppStrings.TelemetrySectionPower, AppStrings.TelemetryVoltage,
            s => $"{s.VoltageV:F2} В", Always),
        new(AppStrings.TelemetrySectionPower, AppStrings.TelemetryCurrent,
            s => $"{s.CurrentA:F2} А", Always),
        new(AppStrings.TelemetrySectionPower, AppStrings.TelemetryPhaseCurrent,
            s => $"{s.PhaseCurrentA:F2} А", Always),
        new(AppStrings.TelemetrySectionPower, AppStrings.TelemetryPower,
            s => $"{s.PowerW:F0} Вт", Always),
        new(AppStrings.TelemetrySectionPower, AppStrings.TelemetryBattery,
            s => $"{s.Battery} %", Always),
        // Код зарядки колесо отдаёт числом; смысла кроме «идёт / не идёт» мы за ним не знаем, и
        // придумывать его тут не будем.
        new(AppStrings.TelemetrySectionPower, AppStrings.TelemetryCharging,
            s => s.ChargingStatus != 0 ? AppStrings.Yes : AppStrings.No, WheelReports.Veteran),
        new(AppStrings.TelemetrySectionPower, AppStrings.MetricCurrentLimit,
            s => $"{s.CurrentLimit:F1} А", WheelReports.InMotionV2),
        new(AppStrings.TelemetrySectionPower, AppStrings.MetricTorque,
            s => $"{s.Torque:F1} Н·м", WheelReports.InMotionV2),
        new(AppStrings.TelemetrySectionPower, AppStrings.MetricMotorPower,
            s => $"{s.MotorPower:F0} Вт", WheelReports.InMotionV2),

        new(AppStrings.TelemetrySectionHeat, AppStrings.TelemetryBoardTemp,
            s => $"{s.TemperatureC} °C", Always),
        new(AppStrings.TelemetrySectionHeat, AppStrings.TelemetryMotorTemp,
            s => $"{s.Temperature2C} °C", WheelReports.Gotway),
        new(AppStrings.TelemetrySectionHeat, AppStrings.MetricCpuTemp,
            s => $"{s.CpuTemp} °C", WheelReports.InMotionV2),
        new(AppStrings.TelemetrySectionHeat, AppStrings.MetricImuTemp,
            s => $"{s.ImuTemp} °C", WheelReports.InMotion),
        // Тот же код «крутится / не крутится», что и зарядка Veteran выше — своего смысла не знаем.
        new(AppStrings.TelemetrySectionHeat, AppStrings.MetricFanStatus,
            s => s.FanStatus != 0 ? AppStrings.Yes : AppStrings.No, WheelReports.KingSong),

        new(AppStrings.TelemetrySectionDistance, AppStrings.TelemetryTrip,
            s => string.Format(AppStrings.ValueTripKm, s.WheelDistanceKm), Always),
        new(AppStrings.TelemetrySectionDistance, AppStrings.TelemetryFromStart,
            s => string.Format(AppStrings.ValueTripKm, s.DistanceFromStartKm), Always),
        new(AppStrings.TelemetrySectionDistance, AppStrings.TelemetryTotal,
            s => string.Format(AppStrings.ValueTripKm, s.TotalDistanceKm), Always),

        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetryModel,
            s => s.Model, s => s.Model.Length > 0),
        // Имя из Bluetooth-объявления — не то же самое, что модель: KS-S18-0205 против «S18».
        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetryName,
            s => s.Name, s => s.Name.Length > 0),
        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetryFirmware,
            s => s.Version, s => s.Version.Length > 0),
        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetryProtocol,
            s => s.WheelType.ToString(), Always),
        // Тот же словарь и то же число, что кнопка «рассчитать» в настройках — тут просто без
        // кнопки, каскад уже посчитан декодером.
        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetryCellsRow,
            s => string.Format(AppStrings.SettingCellsFound, s.PackCells.Cells, CellsSourceWord(s.PackCells.Source)),
            s => s.PackCells.IsKnown),
        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetryMode,
            s => s.ModeStr, s => s.ModeStr.Length > 0),
        new(AppStrings.TelemetrySectionWheel, AppStrings.MetricCpuLoad,
            s => $"{s.CpuLoad} %", WheelReports.KingSong),
        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetrySerial,
            s => s.Serial, s => s.Serial.Length > 0),
        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetrySleep,
            s => string.Format(AppStrings.ValueSeconds, s.SleepTimerSec), WheelReports.Veteran),
        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetryAlarm,
            s => s.WheelAlarm ? AppStrings.Yes : AppStrings.No, WheelReports.Gotway),
        // Строка сообщения приходит не всегда даже у Begode — пустую показывать незачем.
        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetryAlert,
            s => s.Alert, s => s.Alert.Length > 0),

        .. BmsFields(AppStrings.TelemetrySectionBms1, s => s.Bms1),
        .. BmsFields(AppStrings.TelemetrySectionBms2, s => s.Bms2),
    ];

    /// <summary>Один и тот же набор на оба пакета — у Sherman L их два, и разница между ними и есть то, ради чего сюда смотрят.</summary>
    private static Field[] BmsFields(string section, Func<TelemetrySnapshot, SmartBms> pack) =>
    [
        new(section, AppStrings.TelemetryBmsVoltage,
            s => $"{pack(s).Voltage:F2} В", s => HasBms(pack(s))),
        new(section, AppStrings.TelemetryBmsCurrent,
            s => $"{pack(s).Current:F2} А", s => HasBms(pack(s))),
        new(section, AppStrings.TelemetryBmsCharge,
            s => $"{pack(s).RemPerc} %", s => HasBms(pack(s))),
        new(section, AppStrings.TelemetryBmsCells,
            s => $"{pack(s).MinCell:F3} / {pack(s).MaxCell:F3} В", s => pack(s).MaxCell > 0),
        new(section, AppStrings.TelemetryBmsSpread,
            s => $"{pack(s).CellDiff:F3} В", s => pack(s).MaxCell > 0),
        new(section, AppStrings.TelemetryBmsHealth,
            s => $"{pack(s).Health} %", s => HasBms(pack(s)) && pack(s).Health > 0),
        new(section, AppStrings.TelemetryBmsAvgCell,
            s => $"{pack(s).AvgCell:F3} В", s => pack(s).MaxCell > 0),
        new(section, AppStrings.TelemetryBmsMinCellNum,
            s => $"{pack(s).MinCellNum}", s => pack(s).MaxCell > 0),
        new(section, AppStrings.TelemetryBmsMaxCellNum,
            s => $"{pack(s).MaxCellNum}", s => pack(s).MaxCell > 0),
        new(section, AppStrings.TelemetryBmsTemp1,
            s => $"{pack(s).Temp1:F1} °C", s => HasBms(pack(s)) && ReportsTemp1To4(s)),
        new(section, AppStrings.TelemetryBmsTemp2,
            s => $"{pack(s).Temp2:F1} °C", s => HasBms(pack(s)) && ReportsTemp1To4(s)),
        new(section, AppStrings.TelemetryBmsTemp3,
            s => $"{pack(s).Temp3:F1} °C", s => HasBms(pack(s)) && ReportsTemp1To4(s)),
        new(section, AppStrings.TelemetryBmsTemp4,
            s => $"{pack(s).Temp4:F1} °C", s => HasBms(pack(s)) && ReportsTemp1To4(s)),
        new(section, AppStrings.TelemetryBmsTemp5,
            s => $"{pack(s).Temp5:F1} °C", s => HasBms(pack(s)) && ReportsTemp5And6(s)),
        new(section, AppStrings.TelemetryBmsTemp6,
            s => $"{pack(s).Temp6:F1} °C", s => HasBms(pack(s)) && ReportsTemp5And6(s)),
        // Ноль здесь — «ещё не пришло», а не «ноль циклов»: декодер сам не пишет 0 поверх счётчика
        // (InMotionP6Bms.ApplyRealtime), так что нулём этому полю и оставаться, пока правда не пришла.
        new(section, AppStrings.TelemetryBmsFullCycles,
            s => $"{pack(s).FullCycles}", s => pack(s).FullCycles > 0),
        // Ёмкости приходят вместе, одним и тем же ответом (InMotionP6Bms.ApplyRealtime, только P6):
        // завод — первый ненулевой признак, что ответ действительно был.
        new(section, AppStrings.TelemetryBmsFactoryCap,
            s => $"{pack(s).FactoryCap} мА·ч", s => pack(s).FactoryCap > 0),
        new(section, AppStrings.TelemetryBmsRemCap,
            s => $"{pack(s).RemCap} мА·ч", s => pack(s).FactoryCap > 0),
        // Половина пакета — только у Begode (GotwayDecoder.DecodeFrame01): чётный/нечётный номер BMS
        // пишет то одну половину, то другую в один и тот же объект пакета.
        new(section, AppStrings.TelemetryBmsSemiVoltage1,
            s => $"{pack(s).SemiVoltage1:F1} В", s => WheelReports.Gotway(s) && pack(s).SemiVoltage1 != 0),
        new(section, AppStrings.TelemetryBmsSemiVoltage2,
            s => $"{pack(s).SemiVoltage2:F1} В", s => WheelReports.Gotway(s) && pack(s).SemiVoltage2 != 0),
    ];

    private readonly List<(Field Field, View Row, TextView Value)> _rows = [];
    private readonly List<(string Section, TextView Header)> _headers = [];

    /// <param name="nameSp">Кегль подписи и значения. Плеер просит мельче: там таблица делит экран с
    /// панелью, а читают её сидя, не на ходу.</param>
    public View Build(Context context, float nameSp = 14, float valueSp = 15, Color? ink = null)
    {
        var column = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
        Color color = ink ?? UiKit.PlainText(context);

        string? section = null;
        foreach (var field in Fields)
        {
            if (field.Section != section)
            {
                section = field.Section;
                var header = new TextView(context) { Text = field.Section };
                header.SetTextSize(ComplexUnitType.Sp, nameSp - 1);
                header.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
                header.SetTextColor(color);
                header.Alpha = 0.5f;
                header.SetPadding(0, context.Dp(_headers.Count == 0 ? 0 : 14), 0, context.Dp(4));
                column.AddView(header);
                _headers.Add((field.Section, header));
            }

            var row = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Horizontal };
            row.SetPadding(0, context.Dp(3), 0, context.Dp(3));

            var label = new TextView(context) { Text = field.Label };
            label.SetTextSize(ComplexUnitType.Sp, nameSp);
            label.SetTextColor(color);
            label.Alpha = 0.75f;
            // Подпись забирает остаток строки, значение прижато к правому краю: так числа стоят
            // столбиком и читаются на ходу, а не разъезжаются вслед за длиной подписи.
            row.AddView(label, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

            var value = new TextView(context) { Text = "—", Gravity = GravityFlags.Right };
            value.SetTextSize(ComplexUnitType.Sp, valueSp);
            value.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal);
            value.SetTextColor(color);
            row.AddView(value, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { LeftMargin = context.Dp(12) });

            column.AddView(row);
            _rows.Add((field, row, value));
        }

        return column;
    }

    public void Show(TelemetrySnapshot snapshot)
    {
        foreach (var (field, row, value) in _rows)
        {
            bool shown = field.Shown(snapshot);
            row.Visibility = shown ? ViewStates.Visible : ViewStates.Gone;
            if (shown) value.SetText(field.Read(snapshot));
        }

        // Заголовок без единой видимой строки — это заголовок пустоты: у Begode так уходит весь
        // раздел «Батарея», у Veteran без BMS — оба.
        foreach (var (section, header) in _headers)
        {
            bool any = _rows.Exists(r => r.Field.Section == section && r.Row.Visibility == ViewStates.Visible);
            header.Visibility = any ? ViewStates.Visible : ViewStates.Gone;
        }
    }
}
