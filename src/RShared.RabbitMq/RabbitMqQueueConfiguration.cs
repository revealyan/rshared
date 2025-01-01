namespace RShared.RabbitMq;

/// <summary>
/// RabbitMq queue configuration
/// </summary>
public class RabbitMqQueueConfiguration
{
	/// <summary>
	/// Uniq queue id for application
	/// </summary>
	public string Id { get; set; } = string.Empty;

	/// <summary>
	/// RabbitMq queue name
	/// </summary>
	public string QueueName { get; set; } = string.Empty;

	/// <summary>
	/// RabbitMq exchange name
	/// RabbitMq exchange name
	/// </summary>
	public string ExchangeName { get; set; } = string.Empty;
}
