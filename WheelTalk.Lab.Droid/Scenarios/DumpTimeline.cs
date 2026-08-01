using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Logging;
using WheelTalk.Core.Ports;
using WheelTalk.Core.Services;

namespace WheelTalk.Lab.Droid.Scenarios;

/// <summary>
/// Записанный дамп, пропущенный через настоящий декодер. Иначе стенд показывал бы придуманные
/// числа: ШИМ, проценты заряда и напряжение считаются в декодере, и «просто прочитать csv» дало бы
/// другую картинку, чем покажет приложение на том же файле.
/// <para>
/// Перенесено из <c>WheelTalk.Lab/Scenarios/DumpTimeline.cs</c>. Две правки, обе платформенные:
/// <c>FileSystem.OpenAppPackageFileAsync</c> (MAUI Essentials) заменён на
/// <c>Context.Assets.Open</c>, и разбор ушёл в <see cref="Task.Run(Action)"/> — он синхронный и
/// занимает у двухминутного дампа несколько тысяч кадров через декодер, а держать на это время
/// кадровый цикл стенда незачем.
/// </para>
/// </summary>
public static class DumpTimeline
{
    /// <summary>
    /// Пауза, длиннее которой промежуток между кадрами считается перерывом записи и сжимается.
    /// Дамп охватывает и моменты, когда колесо выключали, — проигрывать их честно значит смотреть
    /// на застывший экран минутами.
    /// </summary>
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(1);

    public static Task<Timeline> LoadAsync(string title, string subtitle, string asset, WheelProtocol protocol) =>
        Task.Run(() =>
        {
            using var stream = Android.App.Application.Context.Assets!.Open(asset);
            using var reader = new StreamReader(stream);
            return Build(title, subtitle, reader, protocol);
        });

    public static Timeline Build(string title, string subtitle, TextReader reader, WheelProtocol protocol)
    {
        var config = new LabWheelConfig();
        var state = new WheelState(config, TimeProvider.System);
        var protocolDecoder = WheelDecoderFactory.Create(protocol, state, config, TimeProvider.System, NullLoggerFactory.Instance);
        var decoder = new Decoder(state, protocolDecoder, new DiscardEventSink(), NullLogger<Decoder>.Instance);

        var frames = new List<TimelineFrame>();
        var at = TimeSpan.Zero;
        TimeSpan? previous = null;

        using var subscription = decoder.Telemetry.Subscribe(snapshot =>
            frames.Add(new TimelineFrame(at, snapshot)));

        while (reader.ReadLine() is { } line)
        {
            if (!RawFrameLog.TryParseLine(line, out var time, out byte[] frame)) continue;

            if (previous is { } prior)
            {
                var gap = time - prior;
                at += gap > TimeSpan.Zero && gap < MaxGap ? gap : TimeSpan.FromMilliseconds(20);
            }
            previous = time;

            decoder.Feed(frame);
        }

        return new Timeline(title, subtitle, frames);
    }

    private sealed class DiscardEventSink : IEventSink
    {
        public void Publish(WheelEvent e)
        {
        }
    }
}
