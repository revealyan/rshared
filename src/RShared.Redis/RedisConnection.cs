using StackExchange.Redis;

namespace RShared.Redis;

/// <summary>
/// Owns the connection multiplexer: built from the option or adopted from the consumer.
/// </summary>
internal interface IRedisConnection
{
	/// <summary>
	/// The single multiplexer shared by every package service.
	/// </summary>
	IConnectionMultiplexer Multiplexer { get; }
}

/// <summary>
/// Builds or adopts the multiplexer. A created one is disposed with the container,
/// an adopted one stays under the caller's ownership.
/// </summary>
internal sealed class RedisConnection : IRedisConnection, IAsyncDisposable
{
	private readonly bool _owned;

	public RedisConnection(RedisOption option)
	{
		// Stryker disable all : живое соединение — интеграционным прогоном; BuildOptions покрыт юнитами
		Multiplexer = option.Connection ?? ConnectionMultiplexer.Connect(BuildOptions(option));
		// Stryker restore all
		_owned = option.Connection is null;
	}

	public IConnectionMultiplexer Multiplexer { get; }

	/// <summary>
	/// Parse the connection string and apply package defaults plus the escape hatch.
	/// </summary>
	internal static ConfigurationOptions BuildOptions(RedisOption option)
	{
		var options = ConfigurationOptions.Parse(option.ConnectionString!);
		// не валить хост на старте без редиса: соединение восстановится само
		options.AbortOnConnectFail = false;
		option.ConfigureConnection?.Invoke(options);
		return options;
	}

	public async ValueTask DisposeAsync()
	{
		// Stryker disable Statement, Block : диспоз созданного нами мультиплексора — интеграционным прогоном
		if (_owned)
		{
			await Multiplexer.DisposeAsync();
		}
	}
}
