using System.Reflection.Metadata;
using WheelTalk.Lab.Droid.Ui;

[assembly: MetadataUpdateHandler(typeof(LabHotReload))]

namespace WheelTalk.Lab.Droid.Ui;

/// <summary>
/// HOTRELOAD: мост между горячей перезагрузкой Visual Studio и стендом. Правка тела метода доезжает
/// до телефона сама, но собранную иерархию <c>View</c> она не трогает — числа раскладки прочитаны в
/// конструкторе и там же остались. Поэтому на каждую применённую правку стенд пересобирает
/// показанный экран.
/// <para>
/// Тот же вход есть пальцем (кнопка «♻») и командой
/// (<c>am start -n com.wheeltalk.lab.droid/.LabActivity --es rebuild screen</c>): один и тот же
/// <see cref="Rebuild"/>, три способа его позвать. Здесь — самый удобный: правка применяется без
/// касания телефона вовсе.
/// </para>
/// <para>
/// Среда зовёт эти методы с чужого потока и не ждёт от них исключений: маршалить в UI и глотать
/// ошибки — забота подписчика (<c>LabActivity</c>).
/// </para>
/// </summary>
public static class LabHotReload
{
    /// <summary>Кого будить. Ставит и снимает <c>LabActivity</c> по своему жизненному циклу.</summary>
    public static Action? Rebuild { get; set; }

    /// <summary>Зовётся средой перед применением правки. Кэшей у стенда нет — держать нечего.</summary>
    internal static void ClearCache(Type[]? updated)
    {
    }

    /// <summary>Правка применена — пересобрать экран.</summary>
    internal static void UpdateApplication(Type[]? updated) => Rebuild?.Invoke();
}
