namespace WheelTalk.Storage;

/// <summary>How <see cref="RideStore"/> spends time and disk.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// How long rows are allowed to pile up before a commit. This is the window that is lost if the
    /// app dies — a second and a half of telemetry, seven or eight rows — bought against a WAL
    /// commit and three index updates five times a second. Zero commits as fast as rows arrive,
    /// which is what tests want and a phone does not.
    /// </summary>
    public TimeSpan CommitInterval { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// How often the slow tables get a row regardless of whether anything changed. They also get
    /// one the moment something does change: a minute is fine for watching a pack warm up, and
    /// useless for catching the instant a charger was plugged in.
    /// </summary>
    public TimeSpan StateInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Сколько живёт поток телеметрии. Сутки — решение владельца 03.08.2026 (план 23 §5.1 п. 6):
    /// база остаётся в единицах мегабайт, а горизонт графика равен этому сроку и не длиннее.
    /// Чистка сносит всё старше срока, без исключений для размеченного поездками: очистки
    /// раздельны и друг о друге не знают.
    /// <para>
    /// Ноль или меньше — не чистить вовсе. Это отладочная лазейка, а не режим: за неделю выйдут
    /// сотни мегабайт.
    /// </para>
    /// </summary>
    public TimeSpan TelemetryRetention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Сколько живут сами поездки — их итоги, имена и отметки времени (план 11 §4.5).
    /// <para>
    /// <b>Ноль по умолчанию, то есть не удалять никогда</b>, и это не осторожность ради
    /// осторожности: поездка весит десятки байт, на ней стоит план 9 (прогнозы по накопленному), и
    /// заводское значение, которое молча стирает историю, — то, чего не прощают. Срок ставит
    /// человек и знает, что делает.
    /// </para>
    /// <para>
    /// Поток телеметрии живёт своим сроком (<see cref="TelemetryRetention"/>) и об этом не знает:
    /// очистки раздельны. Оттого поездка, пережившая свой поток, остаётся девятью числами итогов —
    /// и это её нормальный вид, а не половина записи.
    /// </para>
    /// </summary>
    public TimeSpan RideRetention { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Сколько тишины означает «прошлая сессия уже точно не та». Три часа — решение владельца
    /// 03.08.2026 (план 23 §5.4). Это признак аварии, а не правило конца поездки: конец ставится
    /// явно, кнопкой или выходом из приложения, и только приложение, убитое системой, оставляет
    /// поездку открытой. Меньше порога — поездка продолжается той же: убило посреди покатушки,
    /// перезапустил через пять минут, кадры легли в неё же.
    /// </summary>
    public TimeSpan AbandonedRideGap { get; set; } = TimeSpan.FromHours(3);
}
