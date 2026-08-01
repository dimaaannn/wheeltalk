using System.Globalization;
using WheelTalk.Droid.Resources.Strings;
using WheelTalk.Storage;

using WheelTalk.Droid.Configuration;

namespace WheelTalk.Droid.Rides;

/// <summary>
/// Turning a ride's figures into the short strings the list and the detail screen show. Read-only
/// text, so it goes through the device's own culture — unlike the settings, where a value has to be
/// parsed back and is therefore written and read the invariant way.
/// <para>
/// Перенесено из <c>WheelTalk.App/Pages/RideFormat.cs</c> без изменений логики — только namespace
/// (<c>WheelTalk.Droid</c>, как в остальном каркасе) и ссылка на <c>WheelTalk.Droid.Resources.Strings.AppStrings</c>.
/// </para>
/// </summary>
internal static class RideFormat
{
    /// <summary>
    /// "Today, 20:05" for the ride just finished, the date for the ones before it. A rider looking
    /// for this morning's ride is not looking for a date, and a list of dates makes them count.
    /// </summary>
    /// <summary>
    /// Чем подписать поездку: тем же, чем подписан главный экран, — алиас поверх имени
    /// Bluetooth-анонса (<see cref="WheelIdentity"/>), а если ни того ни другого нет, тем, что
    /// знает о колесе сама база (модель или адрес). Имя нигде не дублируется (план 13 §3.1):
    /// подставляется при показе. Алиас и анонс относятся к **выбранному** колесу, поэтому поездки
    /// других колёс подписываются моделью, как раньше.
    /// </summary>
    public static string WheelName(RideSummary ride, WheelOptions wheel, WheelIdentity identity) =>
        string.Equals(ride.Mac, wheel.Address, StringComparison.OrdinalIgnoreCase)
            ? identity.Resolve(ride.Mac, ride.Name)
            : ride.Name;

    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        string time = at.ToString("HH:mm", CultureInfo.CurrentCulture);
        int days = (at.Date - now.Date).Days;

        return days switch
        {
            0 => $"{AppStrings.RidesToday}, {time}",
            -1 => $"{AppStrings.RidesYesterday}, {time}",
            > -7 and < 0 => $"{at.ToString("dddd", CultureInfo.CurrentCulture)}, {time}",
            _ when at.Year == now.Year => $"{at.ToString("d MMMM", CultureInfo.CurrentCulture)}, {time}",
            _ => $"{at.ToString("d MMMM yyyy", CultureInfo.CurrentCulture)}, {time}",
        };
    }

    /// <summary>
    /// Когда ехали, целиком: «Вчера, 03:25 – 03:31». Дата называется один раз — она у обоих концов
    /// общая, и повторять её значит удлинять строку ради ничего.
    /// <para>
    /// Поездка через полночь — исключение: там конец получает свою дату («Вчера, 23:50 – Сегодня,
    /// 00:15»), иначе она читалась бы едущей назад во времени.
    /// </para>
    /// <para>
    /// У незакрытой поездки конца ещё нет, и придумывать его нельзя: остаётся одно время начала, а
    /// что запись идёт — говорит строка под ним.
    /// </para>
    /// </summary>
    public static string Interval(DateTimeOffset start, DateTimeOffset? end, DateTimeOffset now)
    {
        string from = When(start, now);
        if (end is not { } finish) return from;

        string to = finish.Date == start.Date
            ? finish.ToString("HH:mm", CultureInfo.CurrentCulture)
            : When(finish, now);

        return $"{from} – {to}";
    }

    /// <summary>
    /// Hours and minutes, or minutes, or seconds. Never "0:38:12" — a ride is not a stopwatch, and
    /// the seconds of one are noise on a list.
    /// </summary>
    public static string Duration(TimeSpan span)
    {
        if (span.TotalHours >= 1)
        {
            return string.Format(
                CultureInfo.CurrentCulture, AppStrings.RideHoursMinutes, (int)span.TotalHours, span.Minutes);
        }

        return span.TotalMinutes >= 1
            ? string.Format(CultureInfo.CurrentCulture, AppStrings.RideMinutes, (int)span.TotalMinutes)
            : string.Format(CultureInfo.CurrentCulture, AppStrings.RideSeconds, (int)span.TotalSeconds);
    }

    /// <summary>Kilometres to a tenth below a hundred, whole above — a hundredth of a kilometre is ten metres.</summary>
    public static string Distance(double km) =>
        $"{km.ToString(km < 100 ? "F1" : "F0", CultureInfo.CurrentCulture)} {AppStrings.UnitKm}";

    public static string Number(double value, int decimals, string unit) =>
        $"{value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture)} {unit}";

    /// <summary>
    /// The one line under a ride on the list: how far, how long, how close to the limit it came, and
    /// what it cost. PWM is there because it is the number this app exists for.
    /// </summary>
    public static string Summary(RideTotals totals)
    {
        var parts = new List<string>
        {
            Distance(totals.DistanceKm),
            Duration(totals.Duration),
            Number(totals.MaxPwm, 0, AppStrings.UnitPercent),
        };

        if (totals.ConsumptionWhPerKm is { } perKm) parts.Add(Number(perKm, 0, AppStrings.UnitWattHoursPerKm));

        return string.Join(" · ", parts);
    }
}
