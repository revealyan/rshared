using StackExchange.Redis;

namespace RShared.Redis;

/// <summary>
/// Raw database door for everything the package does not wrap (hash, list, set,
/// sorted set, bitmaps, geo). A conscious escape from the laconic concepts: the caller
/// opts out of the rails and works with StackExchange.Redis types directly.
/// If a usage pattern repeats from project to project — it is a candidate for a concept.
/// </summary>
public interface IRedisNative
{
	/// <summary>
	/// The raw database of the shared multiplexer.
	/// </summary>
	IDatabase Database { get; }
}

/// <inheritdoc />
internal sealed class RedisNative(IRedisConnection connection) : IRedisNative
{
	public IDatabase Database => connection.Multiplexer.GetDatabase();
}
