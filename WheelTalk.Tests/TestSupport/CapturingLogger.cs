using Microsoft.Extensions.Logging;

namespace WheelTalk.Tests.TestSupport;

/// <summary>
/// Records every call instead of writing anywhere — for tests that assert on the *level* a message
/// went out at (e.g. the handshake-window promotion in WheelService), which
/// <see cref="Microsoft.Extensions.Logging.Abstractions.NullLogger{T}"/> throws away.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, EventId EventId, string Message);

    public List<Entry> Entries { get; } = [];

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new Entry(logLevel, eventId, formatter(state, exception)));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
