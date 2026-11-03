namespace CameraService.Host.Testing;

using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

// Keeps the log entries of the server (Information and above) for the assertions
internal sealed class TestLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<TestLogEntry> entries = new();

    public IReadOnlyList<TestLogEntry> Entries => [.. entries];

    public ILogger CreateLogger(string categoryName) => new TestLogger(entries);

    public void Dispose()
    {
    }

    private sealed class TestLogger : ILogger
    {
        private readonly ConcurrentQueue<TestLogEntry> entries;

        public TestLogger(ConcurrentQueue<TestLogEntry> entries)
        {
            this.entries = entries;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new TestLogEntry(logLevel, formatter(state, exception)));
    }
}

internal sealed record TestLogEntry(LogLevel Level, string Message);
