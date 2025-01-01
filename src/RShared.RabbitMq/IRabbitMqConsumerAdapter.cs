namespace RShared.RabbitMq;

/// <summary>
/// RabbitMq consumer adapter
/// </summary>
public interface IRabbitMqConsumerAdapter
{
	/// <summary>
	/// Start consuming
	/// </summary>
	/// <param name="cancellationToken">Cancellation token for starting consuming operation</param>
	/// <returns>Task of starting consuming operation</returns>
	public Task StartAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Stop consuming
	/// </summary>
	/// <param name="cancellationToken">Cancellation token for stopping consuming operation</param>
	/// <returns>Task of stopping consuming operation</returns>
	public Task StopAsync(CancellationToken cancellationToken = default);
}
