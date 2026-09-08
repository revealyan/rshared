using Microsoft.Extensions.Logging;

namespace RShared.RabbitMq.Tests;

/// <summary>
/// Логгер-шпион: собирает отформатированные сообщения для проверок в тестах
/// </summary>
internal sealed class TestLogger<T> : ILogger<T>
{
	public List<(LogLevel Level, string Message)> Entries { get; } = [];

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
		Func<TState, Exception?, string> formatter)
		=> Entries.Add((logLevel, formatter(state, exception)));
}
