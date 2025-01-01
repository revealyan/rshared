namespace RShared.RabbitMq;

/// <summary>
/// RabbitMq consumer adapter
/// </summary>
public interface IRabbitMqPublisherAdapter
{
	/// <summary>
	/// Publish message
	/// </summary>
	/// <typeparam name="TData">Data type</typeparam>
	/// <param name="queueId">Queue id for publishing</param>
	/// <param name="data">Message data</param>
	/// <param name="cancellationToken">Cancellation token for publishing operation</param>
	/// <returns>Task of publishing operation</returns>
	public Task PublishAsync<TData>(string queueId, TData data, CancellationToken cancellationToken = default);
}
