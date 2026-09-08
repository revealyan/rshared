namespace RShared.RabbitMq;

/// <summary>
/// Queue topology for manually registered handlers (mirrors <see cref="RabbitMqQueueAttribute"/>)
/// </summary>
public sealed class RabbitMqTopology
{
	/// <summary>
	/// Exchange to bind the queue to. Default: default exchange (publish straight to the queue).
	/// </summary>
	public string? Exchange { get; set; }

	/// <summary>
	/// Binding routing key. Default: queue name.
	/// </summary>
	public string? RoutingKey { get; set; }

	/// <summary>
	/// Redeliveries of a failed message before it is dead-lettered or dropped
	/// </summary>
	public int MaxRetryCount { get; set; } = 3;

	/// <summary>
	/// Dead-letter queue name. Default: "{queue}.dlq", set to empty string to disable dead-lettering.
	/// </summary>
	public string? DeadLetterQueue { get; set; }

	/// <summary>
	/// Durable queue and messages. On by default.
	/// </summary>
	public bool Durable { get; set; } = true;
}
