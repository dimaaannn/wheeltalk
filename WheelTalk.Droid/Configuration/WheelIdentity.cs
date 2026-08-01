using Android.Bluetooth;

namespace WheelTalk.Droid.Configuration;

/// <summary>
/// Как зовут колесо на экране. Цепочка из двух звеньев, и оба принадлежат **колесу**, а не
/// приложению: сначала имя, которым колесо само себя объявляет по Bluetooth, поверх него — алиас,
/// если хозяин его задал.
/// <para>
/// Общего имени колеса не существует: колёса всегда разные, и одно поле на всех означало ровно то,
/// что и означало — имя Begode «GotWay_44028» осталось на экране, когда приложение уже говорило с
/// Sherman L (30.07.2026). Поэтому алиас живёт слоем колеса в таблице настроек, а имя анонса не
/// хранится вовсе: его знает адаптер, и по адресу оно спрашивается заново.
/// </para>
/// </summary>
public sealed class WheelIdentity
{
    /// <summary>
    /// Ответы адаптера по адресам. Словарь, а не одно значение: список поездок спрашивает имена
    /// нескольких колёс подряд, и одна ячейка там бы только билась.
    /// </summary>
    private readonly Dictionary<string, string> _advertised = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _gate = new();

    /// <summary>
    /// Своё имя колеса. Пустая строка — «своего нет», и тогда показывается имя анонса. Значение
    /// приходит из слоя настроек этого колеса, поэтому при переключении колеса меняется само.
    /// </summary>
    public string Alias { get; set; } = "";

    /// <summary>
    /// Чем подписать колесо: алиас, если он задан, иначе имя анонса Bluetooth, а если и его нет
    /// (колесо ни разу не попадалось в скане и не знакомо адаптеру) — модель, которую называет сам
    /// декодер. Последняя ступень — забота вызывающего: ядру про модель здесь знать неоткуда.
    /// </summary>
    public string Resolve(string address, string? model = null)
    {
        if (Alias.Length > 0) return Alias;

        string advertised = Advertised(address);
        if (advertised.Length > 0) return advertised;

        return model ?? "";
    }

    /// <summary>
    /// Забыть, что адаптер отвечал раньше. Зовётся на новом подключении: до первого скана имени у
    /// адаптера может не быть вовсе, а после — появиться, и это единственный момент, когда ответ
    /// на тот же адрес меняется.
    /// </summary>
    public void Forget()
    {
        lock (_gate) _advertised.Clear();
    }

    /// <summary>
    /// Имя, которым колесо объявляет себя по Bluetooth. Спрашивается у адаптера по адресу, а не
    /// хранится у нас: у адаптера оно и так есть после скана или подключения, а вторая копия
    /// разошлась бы с первой — тем более что колесо это имя может и сменить.
    /// <para>
    /// Ответ запоминается до <see cref="Forget"/>. Не ради скорости: имя рисуется на каждом кадре
    /// панели, то есть двадцать раз в секунду, а <c>BluetoothDevice.Name</c> — это binder-вызов в
    /// системный Bluetooth-процесс, из UI-потока, в тот самый процесс, через который идут наши
    /// записи в колесо.
    /// </para>
    /// </summary>
    private string Advertised(string address)
    {
        if (address.Length == 0) return "";

        lock (_gate)
        {
            if (_advertised.TryGetValue(address, out string? known)) return known;
        }

        string name = AskAdapter(address);
        lock (_gate) _advertised[address] = name;
        return name;
    }

    private static string AskAdapter(string address)
    {
        try
        {
            // Без using — ни адаптер, ни устройство нам не принадлежат. `DefaultAdapter` —
            // системный синглтон на всё приложение: освободив его здесь, мы ломаем BLE целиком, и
            // следующая же попытка подключиться падает с ObjectDisposedException. Проверено на
            // телефоне 31.07.2026: имя тогда спрашивалось на каждом кадре, и адаптер умирал в
            // первую же секунду после запуска.
            var adapter = BluetoothAdapter.DefaultAdapter;

            // GetRemoteDevice принимает только верхний регистр — иначе IllegalArgumentException
            // (telemetry-and-ble-reference.md, «Подводные камни BLE»).
            return adapter?.GetRemoteDevice(address.ToUpperInvariant())?.Name ?? "";
        }
        catch (Exception ex) when (ex is Java.Lang.IllegalArgumentException or Java.Lang.SecurityException)
        {
            // Адрес не разобран или разрешения отобрали на ходу: имя — украшение, ронять из-за него
            // экран нельзя.
            return "";
        }
    }
}
