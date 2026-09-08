namespace RShared.RabbitMq;

/// <summary>
/// Publisher over the default exchange: routing key is the queue name.
/// With publisher confirms on, publish completes only after the broker acks.
/// </summary>
public interface IRabbitMqPublisher
{
	/// <summary>
	/// Publish a message to the queue
	/// </summary>
	/// <typeparam name="TMessage">Message type, serialized to JSON</typeparam>
	/// <param name="queueName">Target queue name</param>
	/// <param name="message">Message payload</param>
	Task PublishAsync<TMessage>(string queueName, TMessage message, CancellationToken cancellationToken = default);
}
