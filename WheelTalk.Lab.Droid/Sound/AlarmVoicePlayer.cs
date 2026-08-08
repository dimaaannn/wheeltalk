using Android.Media;
using Encoding = Android.Media.Encoding;

namespace WheelTalk.Lab.Droid.Sound;

/// <summary>
/// Проигрыватель вариантов тревоги для стенда. Одна дорожка на все варианты: волна берётся у
/// <see cref="AlarmVoice"/>, всё остальное — здесь.
/// <para>
/// Поток тревоги (<see cref="AudioUsageKind.Alarm"/>) — тот же, что у приложения. На выезде это
/// принципиально: маршрут и громкость у потоков разные, и вариант, отобранный в медиапотоке, в бою
/// зазвучал бы иначе.
/// </para>
/// <para>
/// <b>Громкость вариантов выравнивается</b> — иначе на улице сравнивались бы не приёмы, а уровни:
/// вариант, случайно оказавшийся на 6 дБ громче, победил бы любой. Считается по звучащим отсчётам
/// (тишина рисунка в счёт не идёт — рисунок и есть то, что сравнивают), а сверху стоит потолок по
/// пику: до искажений громкость не доводим, искажённый сигнал разборчивее не становится.
/// </para>
/// </summary>
public sealed class AlarmVoicePlayer : IDisposable
{
    private const int SampleRate = 44100;
    private const int BlockFrames = 512;

    /// <summary>К какому среднеквадратичному уровню приводятся все варианты. −9 дБ от полной шкалы.</summary>
    private const double TargetRms = 0.35;

    /// <summary>Потолок по пику: у самой шкалы оставлен запас, чтобы не ловить ограничение тракта.</summary>
    private const double PeakCeiling = 0.98;

    /// <summary>Смена уровня за 3 мс: ухо слышит как мгновенно, а щелчка на старте и стопе не остаётся.</summary>
    private const double LevelStep = 1.0 / (SampleRate * 0.003);

    private readonly short[] _block = new short[BlockFrames];
    private readonly Dictionary<string, double> _gains = [];
    private readonly ManualResetEventSlim _awake = new(initialState: false);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Thread _writer;
    private readonly Lock _gate = new();

    private AudioTrack? _track;
    private AlarmVoice _voice = AlarmVoices.All[0];
    private AlarmVoice _wanted = AlarmVoices.All[0];
    private bool _playing;
    private double _intensity = 1;
    private double _level;
    private long _frame;

    public AlarmVoicePlayer()
    {
        _writer = new Thread(Run) { IsBackground = true, Name = "LabAlarmVoice", Priority = ThreadPriority.Highest };
        _writer.Start();
    }

    public AlarmVoice Voice
    {
        get { lock (_gate) return _wanted; }
        set
        {
            lock (_gate) _wanted = value;
            Wake();
        }
    }

    public bool IsPlaying
    {
        get { lock (_gate) return _playing; }
    }

    /// <summary>Насколько близко к пределу, 0…1. Правит рисунок, а не громкость: громкость выровнена.</summary>
    public double Intensity
    {
        set { lock (_gate) _intensity = Math.Clamp(value, 0, 1); }
    }

    public void Play()
    {
        lock (_gate) _playing = true;
        Wake();
    }

    public void Stop()
    {
        lock (_gate) _playing = false;
    }

    public void Dispose()
    {
        Stop();
        _stopping.Cancel();
        _awake.Set();
        _writer.Join(TimeSpan.FromSeconds(1));

        _track?.Release();
        _track?.Dispose();
        _track = null;
        _awake.Dispose();
        _stopping.Dispose();
    }

    private void Wake() => _awake.Set();

    private void Run()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                _awake.Wait(_stopping.Token);
                if (_stopping.IsCancellationRequested) return;

                var track = _track ??= Build();
                if (track is null) return;

                track.Play();
                while (!_stopping.IsCancellationRequested && Fill())
                {
                    track.Write(_block, 0, _block.Length);
                }

                track.Pause();
                track.Flush();
                _awake.Reset();

                // Нажатие «играть» могло прийти между концом заполнения и сбросом флажка — тогда
                // оно потерялось бы, и кнопка молча ничего не сделала.
                lock (_gate)
                {
                    if (_playing) _awake.Set();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ожидаемо — Dispose
        }
    }

    /// <summary>
    /// Заполняет блок и говорит, есть ли ещё что играть. Вариант подменяется только в тишине: смена
    /// волны на ходу — разрыв, то есть щелчок, а на щелчке половина вариантов слышалась бы бодрее,
    /// чем есть.
    /// </summary>
    private bool Fill()
    {
        AlarmVoice wanted;
        bool playing;
        double intensity;
        lock (_gate)
        {
            wanted = _wanted;
            playing = _playing;
            intensity = _intensity;
        }

        if (_level <= 0 && !ReferenceEquals(_voice, wanted))
        {
            _voice = wanted;
            _frame = 0;
        }

        var voice = _voice;
        double gain = GainOf(voice);
        double target = playing && ReferenceEquals(voice, wanted) ? 1 : 0;

        for (int i = 0; i < _block.Length; i++)
        {
            _level = _level < target
                ? Math.Min(target, _level + LevelStep)
                : Math.Max(target, _level - LevelStep);

            double sample = _level * gain * voice.Sample(_frame++ / (double)SampleRate, intensity);
            _block[i] = (short)(short.MaxValue * Math.Clamp(sample, -1, 1));
        }

        return playing || _level > 0;
    }

    private double GainOf(AlarmVoice voice)
    {
        if (_gains.TryGetValue(voice.Id, out double known)) return known;

        double gain = Measure(voice);
        _gains[voice.Id] = gain;
        return gain;
    }

    /// <summary>
    /// Во сколько раз поднять вариант, чтобы он звучал вровень с остальными. Две секунды — больше
    /// самого длинного рисунка, значит в замер попадают и пачка, и её тишина.
    /// </summary>
    private static double Measure(AlarmVoice voice)
    {
        double squares = 0;
        double peak = 0;
        int sounding = 0;

        for (int n = 0; n < SampleRate * 2; n++)
        {
            double sample = voice.Sample(n / (double)SampleRate, 1);
            double level = Math.Abs(sample);

            // Тишина рисунка в громкость не считается: сравнивают приёмы, а не скважность.
            if (level < 0.001) continue;

            squares += sample * sample;
            peak = Math.Max(peak, level);
            sounding++;
        }

        if (sounding == 0 || peak <= 0) return 1;

        double rms = Math.Sqrt(squares / sounding);
        return Math.Min(TargetRms / rms, PeakCeiling / peak);
    }

    private static AudioTrack? Build()
    {
        int minimum = AudioTrack.GetMinBufferSize(SampleRate, ChannelOut.Mono, Encoding.Pcm16bit);
        int size = Math.Max(minimum, BlockFrames * sizeof(short) * 8);

        return new AudioTrack.Builder()
            .SetAudioAttributes(new AudioAttributes.Builder()!
                .SetUsage(AudioUsageKind.Alarm)!
                .SetContentType(AudioContentType.Sonification)!
                .Build()!)!
            .SetAudioFormat(new AudioFormat.Builder()!
                .SetEncoding(Encoding.Pcm16bit)!
                .SetSampleRate(SampleRate)!
                .SetChannelMask(ChannelOut.Mono)!
                .Build()!)!
            .SetBufferSizeInBytes(size)!
            .SetTransferMode(AudioTrackMode.Stream)!
            .Build();
    }
}
