namespace RShared.Redis;

/// <summary>
/// Ephemeral pub/sub: publish a message to the channel of its type.
/// Fire and forget — a message published while nobody is listening is lost forever;
/// for durable delivery use streams or the RabbitMq rail.
/// </summary>
public interface IRedisPublisher
{
	/// <summary>
	/// Publish a message to the channel of its type: {ChannelPrefix}{TMessage name}.
	/// </summary>
	Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default);
}
