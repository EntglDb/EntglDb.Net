using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace EntglDb.Test.Maui;

public class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _logs;

    public InMemoryLoggerProvider(ConcurrentQueue<LogEntry> logs)
    {
        _logs = logs;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new InMemoryLogger(categoryName, _logs);
    }

    public void Dispose()
    {
    }
}

public class InMemoryLogger : ILogger
{
    private readonly string _categoryName;
    private readonly ConcurrentQueue<LogEntry> _logs;

    public InMemoryLogger(string categoryName, ConcurrentQueue<LogEntry> logs)
    {
        _categoryName = categoryName;
        _logs = logs;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = logLevel,
            Category = _categoryName,
            Message = message,
            Exception = exception
        };
        
        _logs.Enqueue(entry);
        
        // Keep size manageable
        if (_logs.Count > 1000)
        {
            _logs.TryDequeue(out _);
        }
    }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Category { get; set; }
    public string Message { get; set; }
    public Exception? Exception { get; set; }

    public string Formatted => $"[{Timestamp:HH:mm:ss}] [{Level}] {Category}: {Message} {(Exception != null ? Exception.ToString() : "")}";
    public Color Color => Level switch
    {
        LogLevel.Error or LogLevel.Critical => Colors.Red,
        LogLevel.Warning => Colors.Orange,
        LogLevel.Information => Colors.Black, // Will adjust validation for Dark Mode via binding/style ideally, but sticking to basic for now.
        _ => Colors.Gray
    };
}
