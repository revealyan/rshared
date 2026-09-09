using RShared.IdentityKit;

namespace RShared.Redis.IdentityKit;

/// <summary>
/// Security stamp cache in Redis: session invalidation is instant and global across nodes
/// (IdentityKit drops the stamp from the cache right after a rotation).
/// </summary>
public sealed class RedisSecurityStampCache(IRedisCache cache) : ISecurityStampCache
{
	/// <inheritdoc />
	public Task<string?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		return cache.GetAsync(Key(userId));
	}

	/// <inheritdoc />
	public Task SetAsync(Guid userId, string securityStamp, TimeSpan timeToLive, CancellationToken cancellationToken = default)
	{
		return cache.SetAsync(Key(userId), securityStamp, timeToLive);
	}

	/// <inheritdoc />
	public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		return cache.DeleteAsync(Key(userId));
	}

	private static string Key(Guid userId)
	{
		return "stamp:" + userId;
	}
}
