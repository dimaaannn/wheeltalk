using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using WheelTalk.Core.Alerts;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Services;
using WheelTalk.Droid.Resources.Strings;

namespace WheelTalk.Droid.Alerts;

/// <summary>
/// Что о тревоге написано райдеру прямо сейчас — одной строкой и в одном месте на всё приложение.
/// Сам ничего не показывает: показом заняты полоса главного экрана и <see cref="AlertOverlay"/>,
/// а здесь решается только, какие слова верны.
/// <para>
/// Считать это в каждом экране порознь нельзя — не из-за дублирования, а потому что разошедшиеся
/// копии дали бы райдеру разные ответы на один и тот же вопрос в зависимости от того, где он стоял.
/// Счёт тревоги остаётся у ядра: <see cref="AlertState"/> приходит из общего потока контейнера,
/// второго вычислителя здесь нет.
/// </para>
/// </summary>
public sealed class AlertBanner : IDisposable
{
    private readonly ILogger<AlertBanner> _logger;
    private readonly Lock _gate = new();
    private readonly IDisposable _alerts;
    private readonly IDisposable _telemetry;
    private readonly IDisposable _connection;

    private AlertState _alert = AlertState.Quiet;
    private string _wheelWords = "";

    public AlertBanner(WheelSession session, IObservable<AlertState> alerts, ILogger<AlertBanner> logger)
    {
        _logger = logger;

        _alerts = alerts.Subscribe(state => Update(alert: state, wheelWords: null));
        _telemetry = session.Telemetry.Subscribe(snapshot => Update(alert: null, WheelWords(snapshot)));

        // Телеметрия просто перестаёт идти, когда колесо потеряно, — последние сказанные им слова
        // остались бы на экране навсегда. Разрыв связи их снимает.
        _connection = session.State
            .Where(state => state != ConnectionState.Connected)
            .Subscribe(_ => Update(alert: null, wheelWords: ""));
    }

    /// <summary>Слова тревоги самого колеса — то, и только то, что показывает полоса главного экрана.</summary>
    public string WheelText { get; private set; } = "";

    /// <summary>
    /// Тревога как число — для тех, кто рисует её, а не пишет: полосы сверху и снизу растут от
    /// интенсивности. Читается на каждом кадре и потому отдаётся полем, а не событием: <see
    /// cref="Changed"/> говорит про смену слов, а сила тревоги меняется непрерывно.
    /// </summary>
    public AlertState Alert => _alert;

    /// <summary>
    /// Полный текст тревоги: слова колеса, а если их нет — наша тревога по скважности или скорости.
    /// <para>
    /// Главный экран берёт не его, а <see cref="WheelText"/>, и это не забывчивость: перегрузку и
    /// превышение панель уже показывает своими средствами, а всплывающая поверх них полоса сдвинула
    /// бы приборы вниз ровно в тот момент, когда райдеру нужно, чтобы цифры стояли на месте. На
    /// прочих экранах приборов нет, и слова — единственный способ сказать то же самое.
    /// </para>
    /// </summary>
    public string Text { get; private set; } = "";

    /// <summary>Текст сменился. Приходит из потока телеметрии — показывающему нужен свой поток интерфейса.</summary>
    public event Action? Changed;

    public void Dispose()
    {
        _alerts.Dispose();
        _telemetry.Dispose();
        _connection.Dispose();
    }

    private static string WheelWords(TelemetrySnapshot snapshot) =>
        snapshot.AlertForDisplay is { Length: > 0 } words ? words
        : snapshot.WheelAlarm ? AppStrings.StripWheelAlarm
        : "";

    /// <summary>
    /// Три источника сходятся здесь под одним замком: они приходят из разных потоков, и без него
    /// две одновременные правки могли бы сложить текст из половин разных состояний.
    /// <c>null</c> значит «этот источник ничего не сказал», а не «сказал пусто».
    /// </summary>
    private void Update(AlertState? alert, string? wheelWords)
    {
        lock (_gate)
        {
            if (alert is not null) _alert = alert;
            if (wheelWords is not null) _wheelWords = wheelWords;

            string text = _wheelWords.Length > 0 ? _wheelWords
                : _alert.PwmAlarming ? AppStrings.StripPwmAlarm
                : _alert.SpeedExceeded ? AppStrings.StripSpeedExceeded
                : "";

            if (_wheelWords == WheelText && text == Text) return;

            WheelText = _wheelWords;
            Text = text;
        }

        // Поднялась и снялась — в журнал: разбирая поездку постфактум, «была ли тревога и сколько
        // держалась» больше узнать неоткуда, а по этой паре строк видно и то, и другое.
        _logger.LogInformation("Alert.Banner Text={Text}", Text.Length > 0 ? Text : "(нет)");

        Changed?.Invoke();
    }
}
