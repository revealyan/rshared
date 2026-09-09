namespace RShared.Redis;

/// <summary>
/// Lua scripts: atomic operations a single command cannot express.
/// </summary>
internal static class RedisLua
{
	/// <summary>
	/// Release a lock only when the token matches the holder.
	/// </summary>
	public const string ReleaseLock = """
		if redis.call("get", KEYS[1]) == ARGV[1] then
			return redis.call("del", KEYS[1])
		else
			return 0
		end
		""";

	/// <summary>
	/// Extend a held lock only when the token matches the holder.
	/// </summary>
	public const string ExtendLease = """
		if redis.call("get", KEYS[1]) == ARGV[1] then
			redis.call("pexpire", KEYS[1], ARGV[2])
			return 1
		else
			return 0
		end
		""";

	/// <summary>
	/// Atomic counter: INCRBY with an expiry set on the very first increment.
	/// ARGV: [delta, ttlMilliseconds or empty string for no expiry].
	/// </summary>
	public const string Increment = """
		local n = redis.call("INCRBY", KEYS[1], ARGV[1])
		if n == tonumber(ARGV[1]) and ARGV[2] ~= '' then
			redis.call("PEXPIRE", KEYS[1], ARGV[2])
		end
		return n
		""";

	/// <summary>
	/// Fixed window rate limit: INCR with the window expiry set on the first hit.
	/// ARGV: [windowMilliseconds]. Returns {count, pttl}.
	/// </summary>
	public const string FixedWindow = """
		local count = redis.call("INCR", KEYS[1])
		if count == 1 then
			redis.call("PEXPIRE", KEYS[1], ARGV[1])
		end
		local ttl = redis.call("PTTL", KEYS[1])
		return {count, ttl}
		""";

	/// <summary>
	/// Sliding window rate limit over a sorted set of hit timestamps; the clock is the server TIME.
	/// ARGV: [windowMilliseconds, limit, uniqueMember]. Returns {allowed, remaining, retryAfterMs}.
	/// </summary>
	public const string SlidingWindow = """
		local t = redis.call('TIME')
		local now = t[1] * 1000 + math.floor(t[2] / 1000)
		local window = tonumber(ARGV[1])
		local limit = tonumber(ARGV[2])
		redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', now - window)
		if redis.call('ZCARD', KEYS[1]) < limit then
			redis.call('ZADD', KEYS[1], now, ARGV[3])
			redis.call('PEXPIRE', KEYS[1], window)
			return {1, limit - redis.call('ZCARD', KEYS[1]), 0}
		end
		local oldest = redis.call('ZRANGE', KEYS[1], 0, 0, 'WITHSCORES')
		return {0, 0, window - (now - oldest[2])}
		""";

	/// <summary>
	/// Prolong both keys of a sliding cache entry for the stored window.
	/// KEYS: [main, sliding]. ARGV: [windowMilliseconds].
	/// </summary>
	public const string RefreshSliding = """
		if redis.call("exists", KEYS[1]) == 1 then
			redis.call("pexpire", KEYS[1], ARGV[1])
			redis.call("pexpire", KEYS[2], ARGV[1])
		end
		""";
}
