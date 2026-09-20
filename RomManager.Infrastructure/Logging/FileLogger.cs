using Microsoft.Extensions.Logging;
using System.Text;

namespace RomManager.Infrastructure.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxFileSize = 2L * 1024 * 1024;
    private const int MaxRetainedFiles = 20;
    private static readonly TimeSpan MaxLogAge = TimeSpan.FromDays(14);
    private readonly object gate = new();
    private readonly string directory;
    private readonly bool verboseScan;
    private string sessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
    private StreamWriter? writer;
    private DateTime currentUtcDate;
    private int segment;
    private long bytesWritten;

    public FileLoggerProvider(string directory, bool verboseScan = false)
    {
        this.directory = directory;
        this.verboseScan = verboseScan;
        Directory.CreateDirectory(directory);
        DeleteExpiredLogs();
        OpenNextFile();
    }
    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, this);
    public void Dispose() { lock (gate) { writer?.Dispose(); writer = null; } }

    private bool IsEnabled(string category, LogLevel level)
    {
        if (level == LogLevel.None) return false;
        var isApplication = category.StartsWith("RomManager.", StringComparison.Ordinal);
        var isVerboseCategory = category.StartsWith("RomManager.Core.Scanner", StringComparison.Ordinal)
            || category.StartsWith("RomManager.Infrastructure.FileSystem", StringComparison.Ordinal);
        if (verboseScan && isVerboseCategory) return level >= LogLevel.Debug;
        return level >= (isApplication ? LogLevel.Information : LogLevel.Warning);
    }

    private void Write(string line)
    {
        lock (gate)
        {
            var activeWriter = writer;
            if (activeWriter is null) return;
            var byteCount = Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(Environment.NewLine);
            if (currentUtcDate != DateTime.UtcNow.Date || bytesWritten + byteCount > MaxFileSize)
            {
                activeWriter.Dispose();
                if (currentUtcDate != DateTime.UtcNow.Date)
                {
                    sessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
                    segment = 0;
                }
                else segment++;
                activeWriter = OpenNextFile();
            }
            activeWriter.WriteLine(line);
            bytesWritten += byteCount;
        }
    }

    private StreamWriter OpenNextFile()
    {
        currentUtcDate = DateTime.UtcNow.Date;
        var path = Path.Combine(directory, $"rommanager-{sessionStamp}-{segment:000}.log");
        while (File.Exists(path)) path = Path.Combine(directory, $"rommanager-{sessionStamp}-{++segment:000}.log");
        writer = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
        bytesWritten = 0;
        return writer;
    }

    private void DeleteExpiredLogs()
    {
        try
        {
            var cutoff = DateTime.UtcNow - MaxLogAge;
            var files = Directory.EnumerateFiles(directory, "rommanager-*.log")
                .Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTimeUtc).ToArray();
            foreach (var file in files.Where((x, index) => x.LastWriteTimeUtc < cutoff || index >= MaxRetainedFiles - 1))
            {
                try { file.Delete(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed class Logger(string category, FileLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => owner.IsEnabled(category, logLevel);
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            owner.Write($"{DateTimeOffset.Now:O} [{level}] {category}: {formatter(state, exception)}{(exception is null ? "" : Environment.NewLine + exception)}");
        }
    }
}
