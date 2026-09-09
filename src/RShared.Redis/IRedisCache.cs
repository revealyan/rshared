namespace RShared.Redis;

/// <summary>
/// Typed and string cache over Redis with a common key prefix.
/// </summary>
public interface IRedisCache
{
	/// <summary>
	/// Get a raw string value, null when the key does not exist.
	/// </summary>
	Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set a raw string value with an optional time to live.
	/// </summary>
	Task SetAsync(string key, string value, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Delete the key: true when it existed.
	/// </summary>
	Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

	/// <summary>
	/// Whether the key exists.
	/// </summary>
	Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get a typed JSON value, default when the key does not exist.
	/// </summary>
	Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set a typed JSON value with an optional time to live.
	/// </summary>
	Task SetAsync<T>(string key, T value, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get or compute: a hit returns the stored value, a miss runs the factory and stores it.
	/// Concurrent misses may compute the value several times, but the stored and returned
	/// instance is always the single winner of SET NX; enable
	/// <see cref="RedisGetOrSetOption.SingleFlight"/> to compute under a per-key lock.
	/// </summary>
	Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> valueFactory, TimeSpan timeToLive,
		RedisGetOrSetOption? options = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Atomic increment with an expiry set on the very first increment; null timeToLive — no expiry.
	/// </summary>
	Task<long> IncrementAsync(string key, long delta = 1, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Delete keys matching a glob pattern (SCAN + UNLINK, never KEYS). The pattern applies
	/// to the full key including the key prefix. Best effort at the moment of the scan;
	/// the walk is O(keyspace) but never blocks the server.
	/// </summary>
	Task<long> DeleteByPatternAsync(string pattern, long batchSize = 500, CancellationToken cancellationToken = default);
}
