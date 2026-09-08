using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace RShared.RabbitMq;

/// <summary>
/// Resolved endpoint: queue topology plus the handler bound to it
/// </summary>
internal sealed class RabbitMqEndpoint
{
	private readonly Func<IServiceProvider, object, CancellationToken, Task> _invoke;

	public RabbitMqEndpoint(string queue, string exchange, string routingKey, int maxRetryCount,
		string? deadLetterQueue, bool durable, Type handlerType, Type messageType)
	{
		Queue = queue;
		Exchange = exchange;
		RoutingKey = routingKey;
		MaxRetryCount = maxRetryCount;
		DeadLetterQueue = deadLetterQueue;
		Durable = durable;
		HandlerType = handlerType;
		MessageType = messageType;
		_invoke = BuildInvoke(handlerType, messageType);
	}

	public string Queue { get; }

	/// <summary>Default exchange ("") means routing straight to the queue</summary>
	public string Exchange { get; }

	public string RoutingKey { get; }

	public int MaxRetryCount { get; }

	/// <summary>Dead-letter queue name, null when dead-lettering is off</summary>
	public string? DeadLetterQueue { get; }

	public bool Durable { get; }

	public Type HandlerType { get; }

	public Type MessageType { get; }

	/// <summary>
	/// Resolve the handler from the scope and invoke it with the deserialized message
	/// </summary>
	public Task InvokeAsync(IServiceProvider provider, object message, CancellationToken cancellationToken)
	{
		return _invoke(provider, message, cancellationToken);
	}

	private static Func<IServiceProvider, object, CancellationToken, Task> BuildInvoke(Type handlerType, Type messageType)
	{
		var method = typeof(RabbitMqEndpoint)
			.GetMethod(nameof(InvokeTyped), BindingFlags.NonPublic | BindingFlags.Static)!
			.MakeGenericMethod(handlerType, messageType);

		return (Func<IServiceProvider, object, CancellationToken, Task>)method.Invoke(null, [])!;
	}

	private static Func<IServiceProvider, object, CancellationToken, Task> InvokeTyped<THandler, TMessage>()
		where THandler : IRabbitMqHandler<TMessage>
	{
		return (provider, message, cancellationToken) =>
			provider.GetRequiredService<THandler>().HandleAsync((TMessage)message, cancellationToken);
	}
}
