namespace RShared.Redis;

/// <summary>
/// Single-flight knobs for <c>GetOrSetAsync</c>.
/// </summary>
public sealed class RedisGetOrSetOption
{
	/// <summary>
	/// Compute the value under a per-key lock so concurrent misses compute once.
	/// Off by default: a lock wait timeout falls back to concurrent computation, it never fails.
	/// </summary>
	public bool SingleFlight { get; set; }

	/// <summary>
	/// How long waiters may wait for the single-flight lock before falling back
	/// to concurrent computation. Also the lock time to live.
	/// </summary>
	public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
