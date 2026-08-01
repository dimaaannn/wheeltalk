using Microsoft.Extensions.Logging;

namespace WheelTalk.Droid.Diagnostics;

/// <summary>
/// Routes Microsoft.Extensions.Logging output to logcat, so `adb logcat -s WheelTalk` shows the
/// same lines the console app writes through Serilog. Serilog itself is deliberately not used
/// here: logcat already timestamps and filters, and it costs nothing when nobody is watching.
/// <para>
/// This used to say there was nowhere useful on a phone to write files to, and that logcat
/// survives a crash. The first half was wrong — rides and dumps go to the external files
/// directory and come off with a plain <c>adb pull</c>. The second is only true for an hour: the
/// ring buffer had turned over by the time the phone from 28.07.2026 reached a computer, taking
/// three crashes with it. What survives a crash is <see cref="FileLog"/>, next to this.
/// </para>
/// </summary>
public sealed class LogcatLoggerProvider : ILoggerProvider
{
    public const string Tag = "WheelTalk";

    public ILogger CreateLogger(string categoryName) => new LogcatLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class LogcatLogger(string category) : ILogger
    {
        // The category is a full type name; only its last segment is worth screen space.
        private readonly string _shortCategory = category[(category.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string message = $"[{_shortCategory}] {formatter(state, exception)}";
            if (exception is not null)
            {
                message = $"{message}{Environment.NewLine}{exception}";
            }

            switch (logLevel)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    Android.Util.Log.Debug(Tag, message);
                    break;
                case LogLevel.Information:
                    Android.Util.Log.Info(Tag, message);
                    break;
                case LogLevel.Warning:
                    Android.Util.Log.Warn(Tag, message);
                    break;
                default:
                    Android.Util.Log.Error(Tag, message);
                    break;
            }
        }
    }
}
