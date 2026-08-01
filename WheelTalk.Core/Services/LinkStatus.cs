namespace WheelTalk.Core.Services;

/// <summary>
/// Почему подключиться нечем — типизированная причина вместо готовой строки показа (план 19 Б4).
/// Тексты остаются в <c>AppStrings</c> по месту показа; здесь только то, что нужно ядру, чтобы
/// честно решить фазу связи, и что можно проверить тестом, не сверяя строки.
/// </summary>
public enum LinkProblem
{
    /// <summary>Причина не известна. Отключённое состояние при ней — просто покой.</summary>
    None,

    /// <summary>Не хватает разрешений ОС (Bluetooth на новых Android, геолокация — на старых).</summary>
    NoPermissions,

    /// <summary>
    /// Bluetooth выключен, или — на Android до 12, где скан BLE завязан на неё, — выключена
    /// системная геолокация.
    /// </summary>
    BluetoothOff,

    /// <summary>Колесо не выбрано — нечего подключать.</summary>
    NoWheelSelected,

    /// <summary>Колесо ответило, но не подошло: не то семейство или не поддержанный протокол.</summary>
    WheelRefused,
}

/// <summary>
/// Состояние связи так, как его показывает главный экран: каждая фаза отвечает на свой вопрос.
/// Связь и свежесть данных — разные вещи: линк может держаться, пока колесо молчит (заснуло,
/// ушло в защиту, потерялось за телом на повороте), поэтому подключённое, но замолчавшее колесо —
/// отдельная фаза, которую экран показывает так же, как переподключение: данных нет в обоих
/// случаях.
/// </summary>
public enum WheelLink
{
    /// <summary>Подключено, но отсчёты не идут дольше порога свежести.</summary>
    NoData,

    /// <summary>Связь жива, кадры идут.</summary>
    Connected,

    /// <summary>Первая попытка подключения.</summary>
    Connecting,

    /// <summary>Связь потеряна, идёт погоня.</summary>
    Reconnecting,

    /// <summary>Отключено, и причина известна: нет разрешений, выключен Bluetooth и т. п.</summary>
    Failed,

    /// <summary>Отключено хозяином. Это покой, а не беда.</summary>
    Idle,
}

/// <summary>
/// Решающая логика фаз связи и свежести кадра. Вынесена из <c>MainActivity</c> (план 14, Б2):
/// чистая функция от состояния сессии и возраста последнего отсчёта, проверяется тестами, а не
/// глазами на телефоне. Тексты и цвета — дело экрана; здесь только решение.
/// </summary>
public static class LinkStatus
{
    /// <summary>
    /// Возраст кадра, после которого показания считаются несвежими: вуаль на цифрах и жёлтая
    /// плашка «данных нет». Колесо шлёт отсчёты каждые ~200 мс, так что полторы секунды — это
    /// семь пропущенных пакетов подряд, а не дрогнувший интервал.
    /// </summary>
    public const double StaleSeconds = 1.5;

    /// <summary>Несвежи ли показания на экране. Порог строгий: ровно на пороге кадр ещё свеж.</summary>
    public static bool IsStale(double staleForSeconds) => staleForSeconds > StaleSeconds;

    /// <param name="state">Где сессия стоит со своим колесом.</param>
    /// <param name="staleForSeconds">Сколько секунд не приходило отсчётов.</param>
    /// <param name="problem">Известна ли причина, по которой подключиться нечем, и какая.</param>
    public static WheelLink Evaluate(ConnectionState state, double staleForSeconds, LinkProblem problem)
    {
        switch (state)
        {
            case ConnectionState.Connected when IsStale(staleForSeconds):
                return WheelLink.NoData;

            case ConnectionState.Connected:
                return WheelLink.Connected;

            case ConnectionState.Connecting:
                return WheelLink.Connecting;

            case ConnectionState.Reconnecting:
                return WheelLink.Reconnecting;

            default:
                // Отключено — это либо покой (нажали «Отключить»), либо беда (нечем подключаться).
                // Разводятся они не догадкой, а тем, знаем ли мы причину.
                return problem != LinkProblem.None ? WheelLink.Failed : WheelLink.Idle;
        }
    }
}
