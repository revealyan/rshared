using StackExchange.Redis;

namespace RShared.Redis;

/// <summary>
/// Rate limiter implementation: both windows are a single atomic Lua script per check.
/// </summary>
internal sealed class RedisRateLimiter(
	IRedisConnection connection,
	RedisOption option) : IRedisRateLimiter
{
	private readonly IDatabase _database = connection.Multiplexer.GetDatabase();

	public async Task<RedisRateLimitResult> AllowAsync(string key, int limit, TimeSpan window,
		RedisRateWindow windowKind = RedisRateWindow.Fixed, CancellationToken cancellationToken = default)
	{
		if (limit < 1)
		{
			throw new ArgumentException("Limit must be positive", nameof(limit));
		}

		if (window <= TimeSpan.Zero)
		{
			throw new ArgumentException("Window must be positive", nameof(window));
		}

		var redisKey = (RedisKey)(option.KeyPrefix + "rate:" + key);
		var windowMs = (long)window.TotalMilliseconds;

		RedisResult raw;
		if (windowKind == RedisRateWindow.Sliding)
		{
			// уникальный member: попадания в одну миллисекунду не схлопываются
			var member = Guid.NewGuid().ToString("N");
			raw = await _database.ScriptEvaluateAsync(RedisLua.SlidingWindow,
				[redisKey], [(RedisValue)windowMs, (RedisValue)limit, (RedisValue)member]);
		}
		else
		{
			raw = await _database.ScriptEvaluateAsync(RedisLua.FixedWindow,
				[redisKey], [(RedisValue)windowMs]);
		}

		var parts = (RedisResult[])raw!;

		if (windowKind == RedisRateWindow.Sliding)
		{
			// {allowed, remaining, retryAfterMs}
			return (int)parts[0] == 1
				? new RedisRateLimitResult(true, (int)parts[1], TimeSpan.Zero)
				: new RedisRateLimitResult(false, 0, TimeSpan.FromMilliseconds((long)parts[2]));
		}

		// fixed: {count, pttl}
		var count = (int)parts[0];
		return count <= limit
			? new RedisRateLimitResult(true, limit - count, TimeSpan.Zero)
			: new RedisRateLimitResult(false, 0, TimeSpan.FromMilliseconds((int)parts[1]));
	}
}
