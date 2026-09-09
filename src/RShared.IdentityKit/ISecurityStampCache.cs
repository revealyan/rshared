using Microsoft.Extensions.Caching.Memory;

namespace RShared.IdentityKit;

/// <summary>
/// Security stamp cache behind the session validator. The default keeps stamps
/// in process memory; a distributed implementation (Redis bridge) makes session
/// invalidation instant and global across nodes.
/// </summary>
public interface ISecurityStampCache
{
	/// <summary>
	/// Last known stamp of the user, null when not cached.
	/// </summary>
	Task<string?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Remember the stamp for the validation interval.
	/// </summary>
	Task SetAsync(Guid userId, string securityStamp, TimeSpan timeToLive, CancellationToken cancellationToken = default);

	/// <summary>
	/// Drop the cached stamp: called right after a stamp rotation (password set/reset),
	/// so every node sees the rotation immediately.
	/// </summary>
	Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-process stamp cache. An eviction on the local node only — other nodes still
/// hold their copies for up to the validation interval.
/// </summary>
internal sealed class MemorySecurityStampCache(IMemoryCache memory) : ISecurityStampCache
{
	public Task<string?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(memory.TryGetValue(userId, out string? stamp) ? stamp : null);
	}

	public Task SetAsync(Guid userId, string securityStamp, TimeSpan timeToLive, CancellationToken cancellationToken = default)
	{
		memory.Set(userId, securityStamp, timeToLive);
		return Task.CompletedTask;
	}

	public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		memory.Remove(userId);
		return Task.CompletedTask;
	}
}
