using Xunit;
using NSubstitute;
using StackExchange.Redis;

namespace RShared.Redis.Tests;

/// <summary>
/// Сборка опций соединения и владение мультиплексором
/// </summary>
public sealed class RedisConnectionTests
{
	[Fact]
	public void BuildOptions_parses_the_connection_string()
	{
		var option = new RedisOption { ConnectionString = "localhost:6379,password=secret" };

		var options = RedisConnection.BuildOptions(option);

		var endpoint = Assert.IsType<System.Net.DnsEndPoint>(options.EndPoints[0]);
		Assert.Equal("localhost", endpoint.Host);
		Assert.Equal(6379, endpoint.Port);
		Assert.Equal("secret", options.Password);
	}

	[Fact]
	public void BuildOptions_does_not_abort_on_connect()
	{
		var option = new RedisOption { ConnectionString = "localhost:6379" };

		var options = RedisConnection.BuildOptions(option);

		// хост не валится на старте без редиса: соединение восстановится само
		Assert.False(options.AbortOnConnectFail);
	}

	[Fact]
	public void BuildOptions_applies_the_escape_hatch()
	{
		var option = new RedisOption
		{
			ConnectionString = "localhost:6379",
			ConfigureConnection = o =>
			{
				o.ConnectTimeout = 1234;
				o.AbortOnConnectFail = true;
			},
		};

		var options = RedisConnection.BuildOptions(option);

		Assert.Equal(1234, options.ConnectTimeout);
		Assert.True(options.AbortOnConnectFail);
	}

	[Fact]
	public async Task External_connection_is_never_disposed()
	{
		var external = Substitute.For<IConnectionMultiplexer>();
		var connection = new RedisConnection(new RedisOption { Connection = external });

		await connection.DisposeAsync();

		await external.DidNotReceiveWithAnyArgs().DisposeAsync();
	}

	[Fact]
	public void Adopted_connection_is_exposed_as_is()
	{
		var external = Substitute.For<IConnectionMultiplexer>();

		var connection = new RedisConnection(new RedisOption { Connection = external });

		Assert.Same(external, connection.Multiplexer);
	}
}
