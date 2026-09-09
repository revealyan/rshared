namespace RShared.Redis;

/// <summary>
/// Rate window kind.
/// </summary>
public enum RedisRateWindow
{
	/// <summary>
	/// INCR with the window expiry set on the first hit. Cheap; allows a 2x burst at a window border.
	/// </summary>
	Fixed,

	/// <summary>
	/// Sorted set of hit timestamps per key. No border burst; O(log limit) time and
	/// O(limit) memory per key. The clock is the server TIME — client drift does not matter.
	/// </summary>
	Sliding,
}

/// <summary>
/// Rate limit check outcome.
/// </summary>
/// <param name="Allowed">Whether the call fits the limit.</param>
/// <param name="Remaining">Calls left in the window when allowed.</param>
/// <param name="RetryAfter">Time until the window resets when denied.</param>
public sealed record RedisRateLimitResult(bool Allowed, int Remaining, TimeSpan RetryAfter);

/// <summary>
/// Distributed rate limiting over Redis.
/// </summary>
public interface IRedisRateLimiter
{
	/// <summary>
	/// Register a call and check the limit: true while the key made fewer than
	/// <paramref name="limit"/> calls within the <paramref name="window"/>.
	/// Sliding is the right pick for brute force protection; fixed is fine for rough quotas.
	/// Do not mix window kinds on the same key.
	/// </summary>
	Task<RedisRateLimitResult> AllowAsync(string key, int limit, TimeSpan window,
		RedisRateWindow windowKind = RedisRateWindow.Fixed, CancellationToken cancellationToken = default);
}
