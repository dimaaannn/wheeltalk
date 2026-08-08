using Android.Content;
using Android.Hardware.Camera2;
using Android.Media;
using Android.OS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WheelTalk.Droid.Configuration;
using WheelTalk.Core.Alerts;
using Application = Android.App.Application;

namespace WheelTalk.Droid.Alerts;

/// <summary>
/// Everything the phone does about an alert while it is not being looked at: sound, vibration and
/// the camera flash. Driven purely by the state the core publishes — nothing here decides whether
/// there is an alarm, only how loud it is — so a signal cannot outlive the condition that raised it.
/// </summary>
public sealed class AlertSignals : IDisposable
{
    /// <summary>
    /// Заметно чаще самого короткого сигнала (20 мс на пороге тревоги — см. <see cref="AlertRhythm"/>),
    /// чтобы ритм задавался расчётом, а не тем, когда проснулся таймер. Такая частота оправдана
    /// только звучащей тревогой, поэтому таймер взводится в <see cref="Apply"/> её приходом и
    /// гасится тишиной — постоянный он давал сто пробуждений в секунду всё время жизни процесса,
    /// с колесом и без.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(10);

    private readonly AlertOptions _options;
    private readonly AlertSignalOptions _channels;
    private readonly ILogger<AlertSignals> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly System.Threading.Timer _timer;

    private readonly AlarmTone _alarmTone = new();

    /// <summary>Замок лампы и состояния тревоги: см. <see cref="SetTorch"/>.</summary>
    private readonly Lock _torch = new();

    private ToneGenerator? _tones;
    private Vibrator? _vibrator;
    private CameraManager? _cameras;
    private string? _torchCameraId;

    private AlertState _state = AlertState.Quiet;
    private long _periodStartedAt;
    private long _lastSpeedBeepAt;
    private bool _torchOn;

    public AlertSignals(
        IOptions<AlertOptions> options,
        IOptions<AlertSignalOptions> channels,
        TimeProvider timeProvider,
        ILogger<AlertSignals> logger)
    {
        _options = options.Value;
        // The live instance, read on every tick: a channel switched off has to fall silent now,
        // not after a restart.
        _channels = channels.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _periodStartedAt = timeProvider.GetTimestamp();
        _lastSpeedBeepAt = timeProvider.GetTimestamp();
        _timer = new System.Threading.Timer(_ => Tick(), state: null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Apply(AlertState state)
    {
        bool wasTicking = _state.Any;

        // Под тем же замком, что и лампа: тик читает состояние из другого потока, и решение
        // «зажигать ли» должно видеть уже новое, а не то, что застало начало тика.
        lock (_torch) _state = state;

        if (state.Any)
        {
            // Отметки ритма не сбрасываются нарочно: первый тик новой тревоги застаёт «период
            // давно истёк» и сигналит сразу — ровно как при постоянном таймере.
            if (!wasTicking) _timer.Change(TimeSpan.Zero, TickInterval);
        }
        else
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            Silence();
        }
    }

    public void Dispose()
    {
        // Тем же путём, что конец тревоги, а не «стоп-таймер и погасить»: Timer.Dispose не ждёт
        // callback, уже вышедший из пула, и тот договорил бы после Silence — зажёг бы лампу, гасить
        // которую больше некому. Apply переводит состояние в тишину под замком, и такой тик
        // отказывается зажигать сам.
        Apply(AlertState.Quiet);

        _timer.Dispose();
        _alarmTone.Dispose();
        _tones?.Release();
        _tones?.Dispose();
        _tones = null;
    }

    private void Tick()
    {
        try
        {
            var state = _state;
            if (!state.Any)
            {
                Silence();
                return;
            }

            if (state.PwmAlarming)
            {
                SignalPwm(state.PwmIntensity);
            }
            else if (state.SpeedExceeded)
            {
                SignalSpeed();
            }
        }
        catch (Exception ex)
        {
            // A phone that refuses to beep must not take the ride telemetry down with it.
            _logger.LogWarning(ex, "Alert.SignalFailed");
        }
    }

    /// <summary>
    /// Два режима одним тоном: на потолке — сплошной, ниже — сигналы, которые удлиняются, а
    /// тишина между ними сокращается. Ритм считает <see cref="AlertRhythm"/>, звук выдаёт
    /// <see cref="AlarmTone"/>.
    /// </summary>
    /// <summary>
    /// Звуку отдаётся только интенсивность: ритм он считает сам, по счётчику отсчётов, и это
    /// единственный способ уложить двадцатимиллисекундный писк в сетку — отсюда, из-за буфера в
    /// девяносто миллисекунд, точности не хватает и не хватит.
    /// <para>
    /// Вспышка и вибрация, наоборот, остаются здесь: им хватает точности тика, а разъехаться со
    /// звуком на десяток миллисекунд они могут незаметно.
    /// </para>
    /// </summary>
    private void SignalPwm(double intensity)
    {
        // Выбор звука читается на каждом тике по той же причине, что и выключатели каналов: правка
        // настройки должна быть слышна сейчас, а не после перезапуска.
        _alarmTone.Wave = _channels.Wave;
        _alarmTone.SetIntensity(_channels.Sound ? intensity : 0);

        var since = _timeProvider.GetElapsedTime(_periodStartedAt);
        if (AlertRhythm.IsPeriodOver(since))
        {
            _periodStartedAt = _timeProvider.GetTimestamp();
            since = TimeSpan.Zero;
            Vibrate(60);
        }

        SetTorch(AlertRhythm.IsSounding(since, intensity));
    }

    private void SignalSpeed()
    {
        _alarmTone.SetIntensity(0);
        SetTorch(false);

        if (_timeProvider.GetElapsedTime(_lastSpeedBeepAt) < _options.SpeedRepeatInterval) return;

        _lastSpeedBeepAt = _timeProvider.GetTimestamp();
        Tones?.StartTone(Tone.PropBeep, 150);
    }

    private void Silence()
    {
        _alarmTone.SetIntensity(0);
        SetTorch(false);
    }

    // Alarm stream on purpose: an alert that a silent phone swallows is worse than no alert.
    // Null while the channel is off, which is what makes every `Tones?.` below a no-op.
    private ToneGenerator? Tones =>
        _channels.Sound ? _tones ??= new ToneGenerator(Android.Media.Stream.Alarm, volume: 100) : null;

    private void Vibrate(int milliseconds)
    {
        if (!_channels.Vibration) return;

        // VibratorManager only exists from API 31; the phone this is developed against is on 30,
        // so the older service is the one that has to work.
#pragma warning disable CA1422
        _vibrator ??= (Vibrator?)Application.Context.GetSystemService(Context.VibratorService);
#pragma warning restore CA1422
        _vibrator?.Vibrate(VibrationEffect.CreateOneShot(milliseconds, VibrationEffect.DefaultAmplitude));
    }

    /// <summary>
    /// Переключить лампу. Всё, что её касается, идёт под одним замком и в одном порядке, потому что
    /// <b>оставшаяся гореть лампа — худшая из здешних поломок</b>: тревога кончилась, таймер
    /// остановлен, гасить её больше некому.
    /// <para>
    /// Две причины, по которым она оставалась включённой, и обе закрыты здесь. Первая: тик идёт из
    /// пула потоков, а конец тревоги приходит из своего, — тик, начавшийся до тишины, договаривал
    /// уже после неё и зажигал лампу заново. Поэтому <b>зажигаем только при живой тревоге</b>, и
    /// состояние читается под тем же замком, под которым записано. Вторая: отметка «горит» ставилась
    /// <b>до</b> вызова платформы, и стоило тому бросить (камеру занял кто-то другой), как отметка
    /// начинала врать — следующее «погаси» считало лампу уже погашенной и не делало ничего.
    /// </para>
    /// <para>
    /// Третья, найденная разбором 05.08.2026: камера умеет отказывать (<c>CameraAccessException</c> —
    /// её занял кто-то другой, съёмка запрещена политикой), и отказ на пути «погасить» шёл из
    /// <see cref="Silence"/>, то есть прямо из <see cref="Apply"/> — а <see cref="Apply"/> вызывается
    /// подпиской на поток тревог. Исключение оттуда рвало подписку насовсем: до конца жизни процесса
    /// не звучало бы ничего, и лампа так и осталась бы гореть. Поэтому платформа зовётся под своим
    /// перехватом, а отметка «горит» ставится <b>только по её успеху</b>.
    /// </para>
    /// </summary>
    private void SetTorch(bool on)
    {
        // Switched off mid-blink the lamp would stay lit, so the request is turned into "off"
        // rather than dropped.
        if (!_channels.Torch) on = false;

        lock (_torch)
        {
            if (on && !_state.Any) return;
            if (_torchOn == on) return;

            try
            {
                _cameras ??= (CameraManager?)Application.Context.GetSystemService(Context.CameraService);
                _torchCameraId ??= FindTorchCamera();
                if (_cameras is null || _torchCameraId is null) return;

                _cameras.SetTorchMode(_torchCameraId, on);
                _torchOn = on;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Alert.TorchFailed On={On}", on);
            }
        }
    }

    private string? FindTorchCamera()
    {
        if (_cameras?.GetCameraIdList() is not { } ids) return null;

        foreach (string id in ids)
        {
            var hasFlash = (Java.Lang.Boolean?)_cameras.GetCameraCharacteristics(id)
                .Get(CameraCharacteristics.FlashInfoAvailable!);
            if (hasFlash?.BooleanValue() == true) return id;
        }

        return null;
    }
}
