namespace RShared.RabbitMq;

/// <summary>
/// Typed handler for messages of one queue. One class — one queue,
/// bound either by <see cref="RabbitMqQueueAttribute"/> or by AddRabbitMqHandler.
/// </summary>
/// <typeparam name="TMessage">Message type the body is deserialized to</typeparam>
public interface IRabbitMqHandler<TMessage>
{
	/// <summary>
	/// Handle a delivered message. Throw to trigger the failure policy (retry, then dead-letter).
	/// </summary>
	Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}
