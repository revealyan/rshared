using StackExchange.Redis;

namespace RShared.Redis;

/// <summary>
/// Lock implementation: SET NX PX acquire, token-bound Lua release.
/// </summary>
internal sealed class RedisLock(
	IRedisConnection connection,
	RedisOption option) : IRedisLock
{
	// интервал опроса в WaitAsync: замены не предполагаем, тесты ждут реально
	internal const int WaitPollMilliseconds = 100;

	private readonly IDatabase _database = connection.Multiplexer.GetDatabase();

	public async Task<RedisLease?> TryAcquireAsync(string name, TimeSpan timeToLive, CancellationToken cancellationToken = default)
	{
		var key = (RedisKey)(option.KeyPrefix + "lock:" + name);
		var token = Guid.NewGuid().ToString("N");

		var won = await _database.StringSetAsync(key, token, timeToLive, When.NotExists);
		return won ? new RedisLease(_database, key, token) : null;
	}

	public async Task<RedisLease?> WaitAsync(string name, TimeSpan timeToLive, TimeSpan timeout, CancellationToken cancellationToken = default)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (true)
		{
			var lease = await TryAcquireAsync(name, timeToLive, cancellationToken);
			if (lease is not null)
			{
				return lease;
			}

			// Stryker disable once Equality : граница >= vs > на часах — недоказуемо стабильным тестом
			if (DateTime.UtcNow >= deadline)
			{
				return null;
			}

			// Stryker disable once Statement : пауза опроса — семантика та же, спин был бы лишь медленнее
			await Task.Delay(WaitPollMilliseconds, cancellationToken);
		}
	}
}
