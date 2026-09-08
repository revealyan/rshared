using System.Reflection;
using System.Text.Json;

namespace RShared.RabbitMq;

/// <summary>
/// RabbitMq options
/// </summary>
public sealed class RabbitMqOption
{
	/// <summary>
	/// Connection string in amqp 0-9-1 format. Required, registration fails fast without it.
	/// </summary>
	public string ConnectionString { get; set; } = string.Empty;

	/// <summary>
	/// Client provided name shown in RabbitMq management. Defaults to the entry assembly name.
	/// </summary>
	// Stryker disable String : фолбэк для хостов без entry assembly — в тестах недостижим
	public string ClientName { get; set; } = Assembly.GetEntryAssembly()?.GetName().Name ?? "RShared.RabbitMq";

	/// <summary>
	/// Prefetch (basicQos) per consumer channel: how many unacked messages a consumer may hold
	/// </summary>
	public ushort PrefetchCount { get; set; } = 16;

	/// <summary>
	/// Publisher confirms: publish completes only after the broker acks the message. On by default.
	/// </summary>
	public bool PublisherConfirms { get; set; } = true;

	/// <summary>
	/// Persistent delivery mode for published messages. On by default.
	/// </summary>
	public bool PersistentMessages { get; set; } = true;

	/// <summary>
	/// JSON serializer options for message bodies
	/// </summary>
	public JsonSerializerOptions? JsonSerializerOptions { get; set; }
}
