using Microsoft.Extensions.Options;
using WheelTalk.Core.Services;
using WheelTalk.Core.Settings;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Settings.Catalogue;
using WheelTalk.Storage;

namespace WheelTalk.Droid.Scan;

/// <summary>
/// Привязанное колесо, как его показывает поиск. <paramref name="Alias"/> пуст, если человек имени
/// не давал, — тогда подпись строке ищет сам экран (имя анонса, а его нет — MAC).
/// </summary>
public readonly record struct BoundWheel(string Mac, string Alias, DateTimeOffset LastConnectedAt);

/// <summary>
/// Привязанные колёса для экрана поиска (план 24 §А2, §А3): что показать вверху списка и что значит
/// «забыть». Собирает два хранилища, которые друг о друге не знают, — историю подключений
/// (<see cref="KnownWheels"/>) и слои настроек, — и потому живёт здесь, а не в любом из них.
/// <para>
/// <b>Слой колеса читается прямо из хранилища</b>, мимо <see cref="LayeredSettings"/>: его область
/// одна на весь процесс, и её смена перестраивает все живые настройки — от порогов тревог до
/// палитры. Ради подписи в списке двигать её нельзя.
/// </para>
/// </summary>
public sealed class BoundWheels(
    KnownWheels known,
    ISettingsStore settings,
    UserSettingsStore userSettings,
    IOptions<WheelOptions> selected,
    WheelSession session)
{
    /// <summary>Свежие первыми — порядок задаёт история подключений.</summary>
    public IReadOnlyList<BoundWheel> All() =>
        [.. known.All().Select(w => new BoundWheel(w.Mac, Alias(w.Mac), w.LastConnectedAt))];

    /// <summary>
    /// «Забыть все настройки этого колеса» — три вещи одним жестом: слой настроек, отметка
    /// подключения и, если забыли выбранное, сам выбор. Настройки прочих колёс не трогаются ни при
    /// каких условиях.
    /// <para>
    /// Связь рвётся первой и только с этим колесом: забывать пароль и пороги того, с чем прямо
    /// сейчас разговариваем, значит оставить сессию жить по настройкам, которых больше нет.
    /// </para>
    /// </summary>
    public async Task ForgetAsync(string mac)
    {
        // Пустой MAC — это общий слой настроек, и снести его здесь значило бы стереть настройки
        // всех колёс разом. Единственная проверка на весь путь: дальше адрес идёт как есть.
        if (mac.Length == 0) return;

        if (Same(session.Address, mac)) await session.DisconnectAsync();

        settings.Remove(mac);
        known.Forget(mac);

        // Выбранное колесо забыто — выбора больше нет, и приложение снова встречает плашкой
        // «Колесо не выбрано». Область настроек переезжает вместе с выбором, как при любой смене
        // колеса: иначе живые настройки остались бы слоем, которого уже нет.
        if (Same(selected.Value.Address, mac)) userSettings.SaveWheel("");
    }

    private string Alias(string mac) => settings.Read(mac).GetValueOrDefault(WheelPage.AliasKey, "");

    private static bool Same(string? address, string mac) =>
        string.Equals(address, mac, StringComparison.OrdinalIgnoreCase);
}
