using WheelTalk.Core.Contracts;
using WheelTalk.Core.Services;
using WheelTalk.Core.Settings.Device;

namespace WheelTalk.Tests.Settings;

/// <summary>
/// Четыре состояния раздела «Конфигурация колеса» (план 34 §5, этап 3). Каждое проверяется по
/// своему признаку, а не по внешнему виду экрана: разбор — чистая функция от связи, последнего
/// кадра и часов, и в Droid остаётся только показ.
/// </summary>
public class WheelSettingsStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private static TelemetrySnapshot Frame(
        WheelType type = WheelType.Veteran,
        string version = "006.0.00",
        DateTimeOffset? settingsAt = null) => new()
        {
            WheelType = type,
            Version = version,
            WheelSettings = settingsAt is { } at
                ? new WheelSettingsSnapshot(at, [new KeyValuePair<string, WheelSettingValue>(
                    WheelSettingKeys.PedalHardness, WheelSettingValue.Reported(94, 94))])
                : null,
        };

    /// <summary>
    /// Связи нет — и неважно, что лежит в последнем снимке сессии: он переживает обрыв, а настройка
    /// колеса есть состояние устройства. Погоня за оборвавшейся связью — то же самое «нет связи».
    /// </summary>
    [Theory]
    [InlineData(ConnectionState.Disconnected)]
    [InlineData(ConnectionState.Connecting)]
    [InlineData(ConnectionState.Reconnecting)]
    public void Offline_when_link_is_not_live(ConnectionState link)
    {
        var view = WheelSettingsState.Resolve(link, Frame(settingsAt: Now), Now, Now);

        Assert.Equal(WheelSettingsView.Offline, view);
    }

    /// <summary>Колесо чужой марки — раньше всех прочих признаков: раздела у него нет ни на связи, ни без неё.</summary>
    [Theory]
    [InlineData(ConnectionState.Connected)]
    [InlineData(ConnectionState.Disconnected)]
    public void Other_brand_beats_everything(ConnectionState link)
    {
        var view = WheelSettingsState.Resolve(link, Frame(WheelType.GotWay, "0.00"), Now, Now);

        Assert.Equal(WheelSettingsView.OtherBrand, view);
    }

    /// <summary>
    /// Колесо младше пятого поколения страниц не шлёт вовсе: номер страницы стоит байтом 46, а его
    /// кадр до него не дотягивает. Abrams — 002, Patton — 004; ждать от них настроек нечего, и
    /// вердикт выносится сразу, без десяти секунд молчания.
    /// </summary>
    [Theory]
    [InlineData("002.0.02")]
    [InlineData("004.0.12")]
    public void Old_generation_does_not_report_settings(string version)
    {
        var view = WheelSettingsState.Resolve(ConnectionState.Connected, Frame(version: version), Now, Now);

        Assert.Equal(WheelSettingsView.NotReported, view);
    }

    /// <summary>Пятое поколение и новее страницы шлёт — Lynx (005) и Sherman L (006) ждут ответа, а не получают приговор.</summary>
    [Theory]
    [InlineData("005.0.00")]
    [InlineData("006.0.00")]
    public void New_generation_is_waited_for(string version)
    {
        var view = WheelSettingsState.Resolve(ConnectionState.Connected, Frame(version: version), Now, Now);

        Assert.Equal(WheelSettingsView.Waiting, view);
    }

    /// <summary>
    /// Кадра телеметрии ещё не было — сказать о колесе нечего вовсе, и молчащее колесо не
    /// объявляется ни чужим, ни старым: пустая версия это незнание, а не признак.
    /// </summary>
    [Fact]
    public void Silent_wheel_is_neither_foreign_nor_old()
    {
        var view = WheelSettingsState.Resolve(ConnectionState.Connected, null, Now, Now);

        Assert.Equal(WheelSettingsView.Waiting, view);
    }

    /// <summary>Десять секунд — два с половиной периода страницы (§1.2). До них раздел молчит, после — отвечает.</summary>
    [Fact]
    public void No_answer_after_ten_seconds_of_silence()
    {
        var before = WheelSettingsState.Resolve(
            ConnectionState.Connected, Frame(), Now, Now.AddSeconds(9.5));
        var after = WheelSettingsState.Resolve(
            ConnectionState.Connected, Frame(), Now, Now.AddSeconds(10));

        Assert.Equal(WheelSettingsView.Waiting, before);
        Assert.Equal(WheelSettingsView.NoAnswer, after);
    }

    /// <summary>Свежий снимок — единственное состояние, в котором показываются строки.</summary>
    [Fact]
    public void Fresh_snapshot_shows_values()
    {
        var view = WheelSettingsState.Resolve(
            ConnectionState.Connected, Frame(settingsAt: Now), Now.AddSeconds(-60), Now.AddSeconds(3));

        Assert.Equal(WheelSettingsView.Values, view);
    }

    /// <summary>
    /// Снимок состарился при живой связи — значения убираются с экрана, а не остаются серыми:
    /// показанное число обязано значить «столько у колеса сейчас». Отсчёт при этом ведётся от
    /// последнего ответа, а не от открытия экрана, иначе повторный заход прятал бы молчание.
    /// </summary>
    [Fact]
    public void Stale_snapshot_is_not_shown()
    {
        var view = WheelSettingsState.Resolve(
            ConnectionState.Connected, Frame(settingsAt: Now), Now.AddSeconds(20), Now.AddSeconds(21));

        Assert.Equal(WheelSettingsView.NoAnswer, view);
    }
}
