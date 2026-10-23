using Microsoft.Extensions.Logging;

namespace VehicleManager.Tests.Infrastructure;

/// <summary>Keeps every formatted log message (and its exception's message), so tests can check what's logged.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Add(formatter(state, exception) + (exception is null ? "" : " " + exception.Message));
}
