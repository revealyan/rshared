using StackExchange.Redis;

namespace RShared.Redis;

/// <summary>
/// Cache implementation over the shared multiplexer.
/// </summary>
internal sealed class RedisCache(
	IRedisConnection connection,
	IRedisLock locks,
	RedisOption option) : IRedisCache
{
	private readonly IDatabase _database = connection.Multiplexer.GetDatabase();

	public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
	{
		return await _database.StringGetAsync(FullKey(key));
	}

	public async Task SetAsync(string key, string value, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default)
	{
		await _database.StringSetAsync(FullKey(key), value, ToExpiration(timeToLive));
	}

	public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
	{
		return await _database.KeyDeleteAsync(FullKey(key));
	}

	public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
	{
		return await _database.KeyExistsAsync(FullKey(key));
	}

	public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
	{
		var payload = await _database.StringGetAsync(FullKey(key));
		return payload.IsNull ? default : RedisJson.Deserialize<T>(payload!);
	}

	public async Task SetAsync<T>(string key, T value, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default)
	{
		var payload = RedisJson.Serialize(value, option.JsonSerializerOptions);
		await _database.StringSetAsync(FullKey(key), payload, ToExpiration(timeToLive));
	}

	public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> valueFactory, TimeSpan timeToLive,
		RedisGetOrSetOption? options = null, CancellationToken cancellationToken = default)
	{
		var hit = await _database.StringGetAsync(FullKey(key));
		if (!hit.IsNull)
		{
			return RedisJson.Deserialize<T>(hit!);
		}

		RedisLease? lease = null;
		if (options is { SingleFlight: true })
		{
			// ожидание с деградацией: не дождались — считаем параллельно, это не ошибка
			lease = await locks.WaitAsync("sf:" + key, options.LockTimeout, options.LockTimeout, cancellationToken);
			if (lease is not null)
			{
				// double-check: победитель мог записать, пока ждали лок
				var afterLock = await _database.StringGetAsync(FullKey(key));
				if (!afterLock.IsNull)
				{
					await lease.DisposeAsync();
					return RedisJson.Deserialize<T>(afterLock!);
				}
			}
		}

		try
		{
			var value = await valueFactory(cancellationToken);
			var payload = RedisJson.Serialize(value, option.JsonSerializerOptions);
			var won = await _database.StringSetAsync(FullKey(key), payload, timeToLive, When.NotExists);
			if (!won)
			{
				// кто-то успел раньше: храним и возвращаем экземпляр победителя
				var winner = await _database.StringGetAsync(FullKey(key));
				if (!winner.IsNull)
				{
					return RedisJson.Deserialize<T>(winner!);
				}
			}

			return value;
		}
		finally
		{
			if (lease is not null)
			{
				await lease.DisposeAsync();
			}
		}
	}

	public async Task<long> IncrementAsync(string key, long delta = 1, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default)
	{
		var ttl = timeToLive is { } span ? (long)span.TotalMilliseconds : -1;
		var result = await _database.ScriptEvaluateAsync(RedisLua.Increment,
			[FullKey(key)], [(RedisValue)delta, (RedisValue)ttl]);
		return (long)result!;
	}

	public async Task<long> DeleteByPatternAsync(string pattern, long batchSize = 500, CancellationToken cancellationToken = default)
	{
		var deleted = 0L;
		var batch = new List<RedisKey>((int)batchSize);

		foreach (var endpoint in connection.Multiplexer.GetEndPoints())
		{
			var server = connection.Multiplexer.GetServer(endpoint);
			await foreach (var key in server.KeysAsync(pattern: pattern, pageSize: (int)batchSize))
			{
				batch.Add(key);
				// Stryker disable once Equality : граница < vs <= — счёт удалённых тот же, порядок флёша не наблюдаем
				if (batch.Count < batchSize)
				{
					continue;
				}

				deleted += await _database.KeyDeleteAsync([.. batch]);
				batch.Clear();
			}
		}

		if (batch.Count > 0)
		{
			deleted += await _database.KeyDeleteAsync([.. batch]);
		}

		return deleted;
	}

	private RedisKey FullKey(string key)
	{
		return (RedisKey)(option.KeyPrefix + key);
	}

	private static Expiration ToExpiration(TimeSpan? timeToLive)
	{
		return timeToLive is { } span ? span : Expiration.Default;
	}
}
