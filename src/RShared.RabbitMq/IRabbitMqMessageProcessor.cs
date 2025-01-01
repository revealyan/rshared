namespace RShared.RabbitMq;

/// <summary>
/// RabbitMq message processor
/// </summary>
public interface IRabbitMqMessageProcessor
{
	/// <summary>
	/// Queue id for processing
	/// </summary>
	public string QueueId { get; }

	/// <summary>
	/// Start consuming
	/// </summary>
	/// <param name="message">Rabbit mq message</param>
	/// <param name="cancellationToken">Cancellation token for processing operation</param>
	/// <returns>Successful or failed processing</returns>
	public Task<bool> ProcessAsync(RabbitMqMessage message, CancellationToken cancellationToken = default);
}
