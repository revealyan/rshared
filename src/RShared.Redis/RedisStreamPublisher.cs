using StackExchange.Redis;

namespace RShared.Redis;

/// <summary>
/// Durable pub/sub over Redis streams: a message survives a subscriber restart,
/// consumption is at-least-once with a consumer group, dead letters and reclaim.
/// Unlike ephemeral pub/sub — but still lighter than the RabbitMq rail.
/// </summary>
public interface IRedisStreamPublisher
{
	/// <summary>
	/// Append a message to the stream of its type: {KeyPrefix}stream:{TMessage name}.
	/// The stream is trimmed on publish by the MaxStreamLength of the handler of this type,
	/// 100 000 when nobody subscribes yet, 0 — never trim.
	/// </summary>
	Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publisher over the shared multiplexer: XADD with an approximate MAXLEN from the handler option.
/// </summary>
internal sealed class RedisStreamPublisher(
	IRedisConnection connection,
	RedisOption option,
	RedisStreamHandlerRegistry handlers) : IRedisStreamPublisher
{
	private readonly IDatabase _database = connection.Multiplexer.GetDatabase();

	public async Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
	{
		// Stryker disable String : имя стрима ассертится тестом (ключ и поле type), маппинг Stryker теряет
		var key = (RedisKey)(option.KeyPrefix + "stream:" + typeof(TMessage).Name);
		var payload = RedisJson.Serialize(message, option.JsonSerializerOptions);
		var maxLen = handlers.OptionFor(typeof(TMessage))?.MaxStreamLength ?? 100_000;

		await _database.StreamAddAsync(key,
			[new NameValueEntry("body", (RedisValue)payload), new NameValueEntry("type", typeof(TMessage).Name)],
			messageId: null,
			maxLength: maxLen > 0 ? (int?)maxLen : null,
			useApproximateMaxLength: true);
	}
}
