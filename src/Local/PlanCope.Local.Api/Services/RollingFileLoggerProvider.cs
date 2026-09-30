using System.Globalization;
using Microsoft.Extensions.Logging;

namespace PlanCope.Local.Api.Services;

/// <summary>Small warning-and-error file sink for the desktop-hosted API.</summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int RetainedFiles = 3;
    private readonly string _filePath;
    private readonly object _writeLock = new();
    private bool _disposed;

    public RollingFileLoggerProvider(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_writeLock) _disposed = true;
    }

    private void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        if (level < LogLevel.Warning) return;
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:O} [{level}] {category} (event {eventId.Id}): {message}");
        if (exception is not null) line += Environment.NewLine + exception;
        lock (_writeLock)
        {
            if (_disposed) return;
            try
            {
                if (File.Exists(_filePath) && new FileInfo(_filePath).Length + line.Length + Environment.NewLine.Length > MaxFileBytes)
                    Rotate();
                File.AppendAllText(_filePath, line + Environment.NewLine);
            }
            catch (IOException) { /* Logging must not take down the local API. */ }
            catch (UnauthorizedAccessException) { /* Logging must not take down the local API. */ }
        }
    }

    private void Rotate()
    {
        var oldest = $"{_filePath}.{RetainedFiles}";
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var index = RetainedFiles - 1; index >= 1; index--)
        {
            var source = $"{_filePath}.{index}";
            if (File.Exists(source)) File.Move(source, $"{_filePath}.{index + 1}");
        }
        if (File.Exists(_filePath)) File.Move(_filePath, $"{_filePath}.1");
    }

    private sealed class FileLogger(RollingFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Write(category, logLevel, eventId, formatter(state, exception), exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
