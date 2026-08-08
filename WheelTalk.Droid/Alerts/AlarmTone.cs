using Android.Media;
using WheelTalk.Core.Alerts;
using Encoding = Android.Media.Encoding;

namespace WheelTalk.Droid.Alerts;

/// <summary>
/// Звук тревоги по ШИМ. Наружу отдаётся одно число — интенсивность, — а весь ритм считается
/// <b>внутри звукового потока, по счётчику отсчётов</b>: время каждого сэмпла известно точно, и
/// начало каждого писка попадает туда, куда должно, с точностью до одного из них.
/// <para>
/// Так пришлось сделать после того, как звук оказался «странным и нестабильным» и на телефоне
/// тоже. Причина была не в тембре и не в длительностях, а в том, что ритм задавался снаружи:
/// таймер интерфейса дёргал флажок «звучит / не звучит», а между этим флажком и динамиком лежал
/// буфер на девяносто миллисекунд. Писк длиной двадцать миллисекунд просто не мог пройти через
/// такую очередь целым — он размазывался по границам блоков и запаздывал на случайную долю
/// буфера. Здесь между решением и звуком нет ничего.
/// </para>
/// <para>
/// Вторая причина была рядом: поток останавливался и запускался заново на каждом писке, потому
/// что «нечего играть» и «пауза между писками» не различались. Пять остановок в секунду — это и
/// был треск. Теперь дорожка молчит, но не останавливается, пока тревога держится.
/// </para>
/// <para>
/// Сама волна — <see cref="AlarmWaves"/> в ядре, и здесь её нет ни строкой. Один и тот же сигнал
/// слушают на стенде и играют в бою, а вторая его запись значила бы, что сравнивали одно, а едут с
/// другим. Отсюда и вся работа этого класса: счётчик отсчётов, плавный вход-выход и дорожка.
/// </para>
/// </summary>
public sealed class AlarmTone : IDisposable
{
    private const int SampleRate = 44100;
    private const int BlockFrames = 256;

    /// <summary>
    /// Нарастание и спад громкости, около 3 мс. Ухо слышит как мгновенно, а щелчка от скачка не
    /// остаётся. Отсчитывается по отсчётам, а не по блокам, поэтому не зависит от размера блока.
    /// </summary>
    private const int RampFrames = SampleRate / 300;

    private readonly short[] _block = new short[BlockFrames];
    private readonly ManualResetEventSlim _awake = new(initialState: false);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Thread _writer;
    private readonly Lock _gate = new();

    private AudioTrack? _track;
    private double _level;
    private long _frame;
    private double _intensity;
    private AlarmWave _wanted;
    private AlarmWave _playing;

    public AlarmTone()
    {
        _writer = new Thread(Run) { IsBackground = true, Name = "AlarmTone", Priority = ThreadPriority.Highest };
        _writer.Start();
    }

    /// <summary>Какой из отобранных сигналов играть. Меняется настройкой и подхватывается в тишине.</summary>
    public AlarmWave Wave
    {
        set { lock (_gate) _wanted = value; }
    }

    /// <summary>
    /// Насколько близко к пределу, 0…1 и выше. Ноль и меньше — тревоги нет, звук гаснет и дорожка
    /// останавливается. Всё остальное — рисунок, и его считает звуковой поток.
    /// </summary>
    public void SetIntensity(double intensity)
    {
        lock (_gate) _intensity = intensity;
        if (intensity > 0) _awake.Set();
    }

    public void Dispose()
    {
        SetIntensity(0);
        _stopping.Cancel();
        _awake.Set();
        _writer.Join(TimeSpan.FromSeconds(1));

        _track?.Release();
        _track?.Dispose();
        _track = null;
        _awake.Dispose();
        _stopping.Dispose();
    }

    private void Run()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                // Пока тревоги нет, поток спит, а дорожка стоит: держать звуковой тракт открытым
                // ради тишины незачем. Между писками одной тревоги этого не происходит — там
                // интенсивность остаётся положительной, и дорожка просто играет тишину.
                _awake.Wait(_stopping.Token);
                if (_stopping.IsCancellationRequested) return;

                var track = _track ??= Build();
                if (track is null) return;

                // Каждая тревога начинается с начала рисунка, а не с середины пачки: обрывок
                // сигнала на старте читается хуже целого.
                _frame = 0;
                track.Play();

                while (!_stopping.IsCancellationRequested && Fill())
                {
                    track.Write(_block, 0, _block.Length);
                }

                track.Pause();
                track.Flush();
                _awake.Reset();
            }
        }
        catch (OperationCanceledException)
        {
            // expected — Dispose
        }
    }

    /// <summary>
    /// Заполняет блок и говорит, есть ли ещё что играть. Волну считает ядро, здесь — время каждого
    /// отсчёта и плавный вход-выход.
    /// </summary>
    private bool Fill()
    {
        double intensity;
        AlarmWave wanted;
        lock (_gate)
        {
            intensity = _intensity;
            wanted = _wanted;
        }

        // Смена сигнала на звучащей волне — разрыв, то есть щелчок. Настройку правят в тишине,
        // так что ждать этой тишины ничего не стоит.
        if (_level <= 0) _playing = wanted;

        bool alarming = intensity > 0;
        double target = alarming ? 1 : 0;
        double step = 1.0 / RampFrames;

        for (int i = 0; i < _block.Length; i++)
        {
            _level = _level < target
                ? Math.Min(target, _level + step)
                : Math.Max(target, _level - step);

            double sample = _level * AlarmWaves.Sample(_playing, _frame++ / (double)SampleRate, intensity);
            _block[i] = (short)(short.MaxValue * Math.Clamp(sample, -1, 1));
        }

        // Тревога кончилась — доигрываем спад до нуля и только потом останавливаемся: оборванный
        // на полуслове хвост и есть щелчок.
        return alarming || _level > 0;
    }

    private static AudioTrack? Build()
    {
        int minimum = AudioTrack.GetMinBufferSize(SampleRate, ChannelOut.Mono, Encoding.Pcm16bit);
        int size = Math.Max(minimum, BlockFrames * sizeof(short) * 8);

        // Поток тревоги намеренно: сигнал, который проглотит беззвучный режим, хуже отсутствия
        // сигнала. Оригинал играет тревогу в медиа-поток, и это его слабое место, а не образец.
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
