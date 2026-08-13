using System.Globalization;
using WheelTalk.Core.Services;

namespace WheelTalk.Core.Diagnostics;

/// <summary>
/// Отметка «фон был жив»: когда и за каким делом. Пишется на диск редко (<see cref="Period"/>) и
/// только пока работа не закончена, читается на следующем возвращении к людям — из файла, если
/// процесса больше нет, или из памяти, если процесс жив, но его морозили.
/// <para>
/// Строкой, а не структурой в файле: одна короткая строка пишется одним <c>WriteAllText</c>, а
/// недописанную такую строку разбор не узнает и промолчит — то есть отказ ведёт к тишине, а не к
/// ложной тревоге.
/// </para>
/// </summary>
public readonly record struct BackgroundBeat(DateTimeOffset At, ConnectionState Phase)
{
    /// <summary>
    /// Как часто фон отмечается. Раз в минуту: диск в кадре запрещён, а пропуск, о котором стоит
    /// говорить, измеряется минутами — точнее мерить незачем.
    /// </summary>
    public static readonly TimeSpan Period = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Пропуск, с которого молчание фона перестаёт быть житейским. Пять пропущенных отметок подряд:
    /// работающий процесс столько не пропускает никогда, а короткая заминка (сборка мусора, тяжёлый
    /// кадр) не должна становиться сообщением человеку.
    /// </summary>
    public static readonly TimeSpan Missed = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Незаконченная работа — это <b>любая</b> живая сессия, кроме отключённой: связь, погоня и
    /// первое подключение одинаково рассчитывают на то, что процесс доживёт до их конца.
    /// </summary>
    public bool WorkUnfinished => Phase != ConnectionState.Disconnected;

    public string Format() =>
        $"{At.ToString("O", CultureInfo.InvariantCulture)} {Phase}";

    public static BackgroundBeat? Parse(string? line)
    {
        var parts = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not [var time, var phase]) return null;

        return DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
               && Enum.TryParse(phase, out ConnectionState state)
            ? new BackgroundBeat(at, state)
            : null;
    }

    /// <summary>
    /// Сколько фон не работал, если об этом стоит говорить, — иначе <c>null</c>. Чистая функция:
    /// отметка и «сейчас» приходят параметрами, поэтому и замок на неё чистый.
    /// <para>
    /// Часы, переставленные назад, дают отрицательный пропуск и молчание: врать про будущее хуже,
    /// чем промолчать об одном перерыве.
    /// </para>
    /// </summary>
    public static TimeSpan? Gap(BackgroundBeat? beat, DateTimeOffset now)
    {
        if (beat is not { WorkUnfinished: true } last) return null;

        var gap = now - last.At;
        return gap >= Missed ? gap : null;
    }
}
