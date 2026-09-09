using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace RShared.Redis;

/// <summary>
/// Subscribes to the channel of every registered message type at host start.
/// A poison body or a failing handler is logged and dropped — the subscription stays alive;
/// messages of one channel are delivered sequentially by the multiplexer, channels run in parallel.
/// The multiplexer restores subscriptions after a reconnect on its own.
/// </summary>
internal sealed class RedisSubscriberService(
	IRedisConnection connection,
	RedisSubscriptionRegistry registry,
	RedisOption option,
	ILogger<RedisSubscriberService> logger) : IHostedService
{
	private readonly List<RedisChannel> _channels = [];

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var subscriber = connection.Multiplexer.GetSubscriber();
		foreach (var group in registry.Snapshot().GroupBy(entry => entry.MessageType))
		{
			var messageType = group.Key;
			var handlers = group.Select(entry => entry.Handler).ToArray();
			var channel = (RedisChannel)(option.ChannelPrefix + messageType.Name);

			await subscriber.SubscribeAsync(channel, (chan, body) => _ = DispatchAsync(messageType, chan, body, handlers));
			_channels.Add(channel);
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		// Stryker disable Statement, Block : guard на пустой список — эквивалент без него
		if (_channels.Count == 0)
		{
			return;
		}

		var subscriber = connection.Multiplexer.GetSubscriber();
		foreach (var channel in _channels)
		{
			await subscriber.UnsubscribeAsync(channel);
		}

		_channels.Clear();
	}

	private async Task DispatchAsync(Type messageType, RedisChannel channel, RedisValue body, Func<object?, Task>[] handlers)
	{
		object? message;
		try
		{
			message = JsonSerializer.Deserialize((byte[])body!, messageType, option.JsonSerializerOptions);
		}
		catch (JsonException exception)
		{
			logger.LogError(exception, "Poison message on channel {Channel}: dropped", channel.ToString());
			return;
		}

		foreach (var handler in handlers)
		{
			try
			{
				await handler(message);
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "Handler failed on channel {Channel}: message dropped for it", channel.ToString());
			}
		}
	}
}
