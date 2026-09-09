using NSubstitute;
using StackExchange.Redis;

namespace RShared.Redis.Tests;

/// <summary>
/// Моки соединения и базы: цепочка IConnectionMultiplexer → GetDatabase/GetServer/GetEndPoints
/// </summary>
internal static class RedisMocks
{
	public static (IRedisConnection Connection, IConnectionMultiplexer Multiplexer, IDatabase Database) Database()
	{
		var multiplexer = Substitute.For<IConnectionMultiplexer>();
		var database = Substitute.For<IDatabase>();
		multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);

		var connection = Substitute.For<IRedisConnection>();
		connection.Multiplexer.Returns(multiplexer);

		return (connection, multiplexer, database);
	}

	/// <summary>
	/// Асинхронный перечислитель ключей для мока IServer.KeysAsync
	/// </summary>
	internal sealed class KeySource(params RedisKey[] keys) : IAsyncEnumerable<RedisKey>
	{
		public async IAsyncEnumerator<RedisKey> GetAsyncEnumerator(CancellationToken cancellationToken = default)
		{
			foreach (var key in keys)
			{
				await Task.Yield();
				yield return key;
			}
		}
	}
}
