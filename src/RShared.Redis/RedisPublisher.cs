using StackExchange.Redis;

namespace RShared.Redis;

/// <summary>
/// Publisher over the shared multiplexer.
/// </summary>
internal sealed class RedisPublisher(
	IRedisConnection connection,
	RedisOption option) : IRedisPublisher
{
	private readonly ISubscriber _subscriber = connection.Multiplexer.GetSubscriber();

	public async Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
	{
		var channel = (RedisChannel)(option.ChannelPrefix + typeof(TMessage).Name);
		var payload = RedisJson.Serialize(message, option.JsonSerializerOptions);
		await _subscriber.PublishAsync(channel, payload);
	}
}
