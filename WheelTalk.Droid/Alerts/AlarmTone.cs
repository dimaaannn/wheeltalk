using Android.Media;
using WheelTalk.Core.Alerts;
using Encoding = Android.Media.Encoding;

namespace WheelTalk.Droid.Alerts;

/// <summary>
/// Звук тревоги по ШИМ. Наружу отдаётся одно число — интенсивность, — а весь ритм считается
/// <b>внутри звукового потока, по счётчику отсчётов</b>: период 200 мс это ровно 8820 отсчётов, и
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
/// Волна — как в оригинале (<c>utils/AudioUtil.kt</c>): 440 Гц плюс половина на октаве и четверть
/// на две выше. Чистая синусоида звучит как сигнал прибора, эта смесь — как тревога.
/// </para>
/// </summary>
public sealed class AlarmTone : IDisposable
{
    private const int SampleRate = 44100;
    private const double Frequency = 440;
    private const double Amplitude = 0.9;

    /// <summary>
    /// Вторая волна не в кратном отношении к основной — 1,34 против 1. Разница около 150 Гц даёт
    /// не медленное покачивание, а резкость: две волны, не складывающиеся в общий период,
    /// звучат жёстко и цепляют слух там, где чистый тон сливается с городским шумом.
    /// <para>
    /// Отношение взято у оригинала (<c>utils/AudioUtil.kt</c>), но у него оно во второй области
    /// буфера — той, что играет тревогу по току; тревоге по ШИМ достаётся первая, без биений.
    /// То есть это осознанное отклонение, а не воспроизведение: записано в AGENTS.md.
    /// </para>
    /// </summary>
    private const double BeatRatio = 1.34;

    /// <summary>Сумма амплитуд — на неё нормируется, чтобы ничего не переполнялось.</summary>
    private const double HarmonicSum = 1 + 1 + 0.5 + 0.25;

    private const int BlockFrames = 256;

    /// <summary>
    /// Нарастание и спад громкости, около 3 мс. Ухо слышит как мгновенно, а щелчка от скачка не
    /// остаётся. Отсчитывается по отсчётам, а не по блокам, поэтому не зависит от размера блока.
    /// </summary>
    private const int RampFrames = SampleRate / 300;

    private static readonly int PeriodFrames = (int)(AlertRhythm.Period.TotalSeconds * SampleRate);

    private readonly short[] _block = new short[BlockFrames];
    private readonly ManualResetEventSlim _awake = new(initialState: false);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Thread _writer;
    private readonly Lock _gate = new();

    private AudioTrack? _track;
    private double _phase;
    private double _gain;
    private int _positionInPeriod;
    private double _intensity;

    public AlarmTone()
    {
        _writer = new Thread(Run) { IsBackground = true, Name = "AlarmTone", Priority = ThreadPriority.Highest };
        _writer.Start();
    }

    /// <summary>
    /// Насколько близко к пределу, 0…1 и выше. Ноль и меньше — тревоги нет, звук гаснет и дорожка
    /// останавливается. Всё остальное — ритм, и его считает звуковой поток.
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

                _positionInPeriod = 0;
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
    /// Заполняет блок и говорит, есть ли ещё что играть. Ритм считается здесь, по отсчётам: это
    /// единственное место, где известно точное время каждого сэмпла.
    /// </summary>
    private bool Fill()
    {
        double intensity;
        lock (_gate) intensity = _intensity;

        int toneFrames = (int)(AlertRhythm.ToneLength(intensity).TotalSeconds * SampleRate);
        double step = Amplitude / RampFrames;
        double advance = 2 * Math.PI * Frequency / SampleRate;
        bool alarming = intensity > 0;

        for (int i = 0; i < _block.Length; i++)
        {
            // Писк занимает начало периода, остальное — тишина. На потолке длина писка равна
            // периоду, и тишине взяться неоткуда: см. AlertRhythm.
            bool sounding = alarming && _positionInPeriod < toneFrames;
            if (++_positionInPeriod >= PeriodFrames) _positionInPeriod = 0;

            double target = sounding ? Amplitude : 0;
            _gain = _gain < target ? Math.Min(target, _gain + step) : Math.Max(target, _gain - step);

            // Фаза не сбрасывается никогда — ни между блоками, ни между писками. Именно её разрыв
            // и слышен как щелчок.
            // Заворачивается по 2π·50, а не по 2π: у волны в 1,34 основной период другой, и заворот
            // по периоду основной дал бы ей разрыв фазы. Пятьдесят периодов — общий знаменатель
            // (1,34 × 50 = 67 ровно), поэтому обе волны приходят к нему целыми.
            _phase += advance;
            if (_phase >= 2 * Math.PI * 50) _phase -= 2 * Math.PI * 50;

            double wave = Math.Sin(_phase)
                + Math.Sin(BeatRatio * _phase)
                + 0.5 * Math.Sin(2 * _phase)
                + 0.25 * Math.Sin(4 * _phase);
            _block[i] = (short)(short.MaxValue * _gain * wave / HarmonicSum);
        }

        // Тревога кончилась — доигрываем спад до нуля и только потом останавливаемся: оборванный
        // на полуслове хвост и есть щелчок.
        return alarming || _gain > 0;
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
