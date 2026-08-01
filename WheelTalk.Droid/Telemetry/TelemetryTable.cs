using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using Android.Widget;
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
    private static bool Veteran(TelemetrySnapshot s) => s.WheelType == WheelType.Veteran;
    private static bool Gotway(TelemetrySnapshot s) => s.WheelType == WheelType.GotWay;

    /// <summary>Пакет отвечает: молчащий BMS отдаёт нули по всему, включая напряжение.</summary>
    private static bool HasBms(SmartBms bms) => bms.Voltage > 0 || bms.MaxCell > 0;

    private static readonly Field[] Fields =
    [
        new(AppStrings.TelemetrySectionMotion, AppStrings.TelemetrySpeed,
            s => string.Format(AppStrings.ValueSpeedKmh, s.SpeedKmh), Always),
        new(AppStrings.TelemetrySectionMotion, AppStrings.TelemetryTopSpeed,
            s => string.Format(AppStrings.ValueSpeedKmh, s.TopSpeedKmh), Always),
        new(AppStrings.TelemetrySectionMotion, AppStrings.TelemetryPwm,
            s => $"{s.Pwm:F1} %", Always),
        new(AppStrings.TelemetrySectionMotion, AppStrings.TelemetryMaxPwm,
            s => $"{s.MaxPwm:F1} %", Always),
        new(AppStrings.TelemetrySectionMotion, AppStrings.TelemetryAngle,
            s => $"{s.Angle:F1} °", Veteran),

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
            s => s.ChargingStatus != 0 ? AppStrings.Yes : AppStrings.No, Veteran),

        new(AppStrings.TelemetrySectionHeat, AppStrings.TelemetryBoardTemp,
            s => $"{s.TemperatureC} °C", Always),
        new(AppStrings.TelemetrySectionHeat, AppStrings.TelemetryMotorTemp,
            s => $"{s.Temperature2C} °C", Gotway),

        new(AppStrings.TelemetrySectionDistance, AppStrings.TelemetryTrip,
            s => string.Format(AppStrings.ValueTripKm, s.WheelDistanceKm), Always),
        new(AppStrings.TelemetrySectionDistance, AppStrings.TelemetryFromStart,
            s => string.Format(AppStrings.ValueTripKm, s.DistanceFromStartKm), Always),
        new(AppStrings.TelemetrySectionDistance, AppStrings.TelemetryTotal,
            s => string.Format(AppStrings.ValueTripKm, s.TotalDistanceKm), Always),

        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetryModel,
            s => s.Model, s => s.Model.Length > 0),
        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetryFirmware,
            s => s.Version, s => s.Version.Length > 0),
        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetryProtocol,
            s => s.WheelType.ToString(), Always),
        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetrySleep,
            s => string.Format(AppStrings.ValueSeconds, s.SleepTimerSec), Veteran),
        new(AppStrings.TelemetrySectionWheel, AppStrings.TelemetryAlarm,
            s => s.WheelAlarm ? AppStrings.Yes : AppStrings.No, Gotway),
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
