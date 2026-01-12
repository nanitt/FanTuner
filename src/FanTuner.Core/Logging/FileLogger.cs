using Microsoft.Extensions.Logging;

namespace FanTuner.Core.Logging;

/// <summary>
/// File logger with rolling file support
/// </summary>
public class FileLogger : ILogger, IDisposable
{
    private readonly string _categoryName;
    private readonly FileLoggerProvider _provider;

    public FileLogger(string categoryName, FileLoggerProvider provider)
    {
        _categoryName = categoryName;
        _provider = provider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _provider.MinLogLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var level = logLevel.ToString().ToUpper().PadRight(5)[..5];

        var logLine = $"[{timestamp}] [{level}] [{_categoryName}] {message}";

        if (exception != null)
        {
            logLine += Environment.NewLine + exception.ToString();
        }

        _provider.WriteLog(logLine);
    }

    public void Dispose()
    {
        // Provider handles disposal
    }
}

/// <summary>
/// File logger provider with rolling file support
/// </summary>
public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly string _logFilePrefix;
    private readonly long _maxFileSizeBytes;
    private readonly int _maxFileCount;
    private readonly object _lock = new();

    private StreamWriter? _writer;
    private string? _currentFilePath;
    private long _currentFileSize;
    private bool _disposed;

    public LogLevel MinLogLevel { get; set; } = LogLevel.Information;

    public FileLoggerProvider(
        string logDirectory,
        string logFilePrefix = "fantuner",
        int maxFileSizeMb = 10,
        int maxFileCount = 5,
        LogLevel minLogLevel = LogLevel.Information)
    {
        _logDirectory = logDirectory;
        _logFilePrefix = logFilePrefix;
        _maxFileSizeBytes = maxFileSizeMb * 1024L * 1024L;
        _maxFileCount = maxFileCount;
        MinLogLevel = minLogLevel;

        Directory.CreateDirectory(_logDirectory);
        InitializeWriter();
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName, this);
    }

    internal void WriteLog(string message)
    {
        if (_disposed) return;

        lock (_lock)
        {
            if (_disposed || _writer == null) return;

            try
            {
                _writer.WriteLine(message);
                _writer.Flush();

                _currentFileSize += message.Length + Environment.NewLine.Length;

                // Check if we need to roll
                if (_currentFileSize >= _maxFileSizeBytes)
                {
                    RollLogFile();
                }
            }
            catch (Exception)
            {
                // Logging should never throw
            }
        }
    }

    private void InitializeWriter()
    {
        _currentFilePath = GetCurrentLogFilePath();

        // Check existing file size
        if (File.Exists(_currentFilePath))
        {
            _currentFileSize = new FileInfo(_currentFilePath).Length;

            if (_currentFileSize >= _maxFileSizeBytes)
            {
                RollLogFile();
                return;
            }
        }
        else
        {
            _currentFileSize = 0;
        }

        _writer = new StreamWriter(_currentFilePath, append: true)
        {
            AutoFlush = false
        };
    }

    private string GetCurrentLogFilePath()
    {
        return Path.Combine(_logDirectory, $"{_logFilePrefix}.log");
    }

    private void RollLogFile()
    {
        _writer?.Close();
        _writer?.Dispose();
        _writer = null;

        // Rename existing files
        for (int i = _maxFileCount - 1; i >= 0; i--)
        {
            var oldPath = i == 0
                ? GetCurrentLogFilePath()
                : Path.Combine(_logDirectory, $"{_logFilePrefix}.{i}.log");

            var newPath = Path.Combine(_logDirectory, $"{_logFilePrefix}.{i + 1}.log");

            if (File.Exists(oldPath))
            {
                if (i + 1 >= _maxFileCount)
                {
                    File.Delete(oldPath);
                }
                else
                {
                    File.Move(oldPath, newPath, overwrite: true);
                }
            }
        }

        // Create new file
        _currentFilePath = GetCurrentLogFilePath();
        _currentFileSize = 0;
        _writer = new StreamWriter(_currentFilePath, append: false)
        {
            AutoFlush = false
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            _writer?.Flush();
            _writer?.Close();
            _writer?.Dispose();
            _writer = null;
        }
    }
}

/// <summary>
/// Extension methods for adding file logging
/// </summary>
public static class FileLoggerExtensions
{
    public static ILoggingBuilder AddFileLogger(
        this ILoggingBuilder builder,
        string logDirectory,
        string logFilePrefix = "fantuner",
        int maxFileSizeMb = 10,
        int maxFileCount = 5,
        LogLevel minLogLevel = LogLevel.Information)
    {
        builder.AddProvider(new FileLoggerProvider(
            logDirectory,
            logFilePrefix,
            maxFileSizeMb,
            maxFileCount,
            minLogLevel));

        return builder;
    }
}
