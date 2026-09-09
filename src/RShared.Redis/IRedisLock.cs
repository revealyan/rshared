namespace RShared.Redis;

/// <summary>
/// Distributed locks over Redis: SET NX with a TTL, release only by the holder's token.
/// </summary>
public interface IRedisLock
{
	/// <summary>
	/// Try to acquire without waiting: null when the lock is held by someone else.
	/// The lease dies with its TTL — pick it with a margin over the expected work time.
	/// </summary>
	Task<RedisLease?> TryAcquireAsync(string name, TimeSpan timeToLive, CancellationToken cancellationToken = default);

	/// <summary>
	/// Poll until acquired or the timeout elapses: null on timeout.
	/// </summary>
	Task<RedisLease?> WaitAsync(string name, TimeSpan timeToLive, TimeSpan timeout, CancellationToken cancellationToken = default);
}
