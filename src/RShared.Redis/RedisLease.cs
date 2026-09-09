using StackExchange.Redis;

namespace RShared.Redis;

/// <summary>
/// A held lock. Dispose releases it (only by its own token) and is idempotent;
/// ExtendAsync prolongs the TTL of a still held lease.
/// </summary>
public sealed class RedisLease : IAsyncDisposable, IDisposable
{
	private readonly IDatabase _database;
	private readonly string _key;
	private readonly string _token;
	private int _released;

	internal RedisLease(IDatabase database, string key, string token)
	{
		_database = database;
		_key = key;
		_token = token;
	}

	/// <summary>
	/// Extend the lease. False when the lock expired or was taken over by someone else —
	/// the work must stop.
	/// </summary>
	public async Task<bool> ExtendAsync(TimeSpan timeToLive)
	{
		var result = await _database.ScriptEvaluateAsync(RedisLua.ExtendLease,
			[(RedisKey)_key], [(RedisValue)_token, (RedisValue)(long)timeToLive.TotalMilliseconds]);
		return (int)result! == 1;
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _released, 1) == 1)
		{
			return;
		}

		await _database.ScriptEvaluateAsync(RedisLua.ReleaseLock, [(RedisKey)_key], [(RedisValue)_token]);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}
}
