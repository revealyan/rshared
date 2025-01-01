namespace RShared.RabbitMq;

/// <summary>
/// RabbitMq configuration
/// </summary>
public class RabbitMqConfiguration
{
	/// <summary>
	/// Client provided name
	/// </summary>
	public string ClientName { get; set; } = string.Empty;

	/// <summary>
	/// Connection string amqp 0-9-1 format
	/// </summary>
	public string ConnectionString { get; set; } = string.Empty;

	/// <summary>
	/// RabbitMq queues configurations
	/// </summary>
	public RabbitMqQueueConfiguration[] Queues { get; set; } = [];
}
