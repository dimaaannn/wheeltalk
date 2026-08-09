using Android.Bluetooth;
using Android.Content;
using AndroidX.Core.Content;

namespace WheelTalk.Droid.Ble;

/// <summary>
/// Единственный приёмник системных сообщений во всём приложении: включили или выключили Bluetooth
/// (план 11 §3.2). Выключение адаптера — та причина отказов, о которой не надо догадываться: она
/// приходит сама.
/// <para>
/// Регистрируется <b>кодом и на время</b>, а не манифестом: манифестный приёмник будили бы системой
/// в любой момент — приложение без открытого экрана и без поездки не имеет к состоянию адаптера
/// никакого дела. Хозяин регистрирует его, пока экран виден, и снимает следом.
/// </para>
/// <para>
/// Промежуточные состояния (<c>TurningOn</c>, <c>TurningOff</c>) сюда не пускаются: ответ у нас
/// двоичный — «работать можно» или «нельзя», — а на полпути он неизвестен, и звать хозяина с
/// догадкой значит дать ему решить дважды.
/// </para>
/// </summary>
public sealed class BluetoothStateReceiver : BroadcastReceiver
{
    private readonly Action<bool> _onChanged;

    private BluetoothStateReceiver(Action<bool> onChanged) => _onChanged = onChanged;

    /// <summary>Подписаться на состояние адаптера. Возвращает приёмник, который обязан быть снят <see cref="Unregister"/>.</summary>
    public static BluetoothStateReceiver Register(Context context, Action<bool> onChanged)
    {
        var receiver = new BluetoothStateReceiver(onChanged);

        // Через ContextCompat и с явным «не для чужих»: с Android 14 приёмник, зарегистрированный
        // кодом, обязан назвать это сам. Системные широковещания вроде нашего исключение, но
        // полагаться на исключение дороже одной строки.
        ContextCompat.RegisterReceiver(
            context,
            receiver,
            new IntentFilter(BluetoothAdapter.ActionStateChanged),
            ContextCompat.ReceiverNotExported);
        return receiver;
    }

    /// <summary>
    /// Снять подписку. Своё исключение проглатывается сознательно: снятие уже снятого приёмника —
    /// <c>IllegalArgumentException</c>, и ронять из-за него уход с экрана было бы смешно.
    /// </summary>
    public void Unregister(Context context)
    {
        try
        {
            context.UnregisterReceiver(this);
        }
        catch (Java.Lang.IllegalArgumentException)
        {
            // Не был зарегистрирован — значит и снимать нечего.
        }
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != BluetoothAdapter.ActionStateChanged) return;

        int state = intent.GetIntExtra(BluetoothAdapter.ExtraState, -1);
        if (state == (int)State.On) _onChanged(true);
        else if (state == (int)State.Off) _onChanged(false);
    }
}
