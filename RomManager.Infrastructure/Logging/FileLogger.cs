using Microsoft.Extensions.Logging;

namespace RomManager.Infrastructure.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object gate = new();
    private readonly StreamWriter writer;
    public FileLoggerProvider(string directory)
    {
        Directory.CreateDirectory(directory);
        writer = new StreamWriter(Path.Combine(directory, $"rommanager-{DateTime.UtcNow:yyyyMMdd}.log"), append: true) { AutoFlush = true };
    }
    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, this);
    public void Dispose() => writer.Dispose();
    private void Write(string line) { lock (gate) writer.WriteLine(line); }

    private sealed class Logger(string category, FileLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => owner.Write($"{DateTimeOffset.Now:O} [{level}] {category}: {formatter(state, exception)}{(exception is null ? "" : Environment.NewLine + exception)}");
    }
}
