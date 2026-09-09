using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StackExchange.Redis;

namespace RShared.Redis.Tests;

/// <summary>
/// Регистрации: валидация BuildOption, биндинг секции, builder-перегрузки, TryAdd-перебиваемость
/// </summary>
public sealed class RegistrationTests
{
	private static IConfigurationSection Section(string json)
	{
		var config = new ConfigurationBuilder()
			.AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
			.Build();
		return config.GetSection("redis");
	}

	[Fact]
	public void Missing_connection_settings_throw()
	{
		var exception = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddRedis(_ => { }));

		Assert.Contains("ConnectionString or Connection is required", exception.Message);
	}

	[Fact]
	public void Ambiguous_connection_settings_throw()
	{
		var exception = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddRedis(o =>
		{
			o.ConnectionString = "localhost:6379";
			o.Connection = Substitute.For<IConnectionMultiplexer>();
		}));

		Assert.Contains("mutually exclusive", exception.Message);
	}

	[Fact]
	public void Section_binds_string_settings()
	{
		var services = new ServiceCollection().AddRedis(Section("""
			{ "redis": { "connectionString": "localhost:6379", "keyPrefix": "app:", "channelPrefix": "app-" } }
			"""));

		var option = services.BuildServiceProvider().GetRequiredService<RedisOption>();
		Assert.Equal("localhost:6379", option.ConnectionString);
		Assert.Equal("app:", option.KeyPrefix);
		Assert.Equal("app-", option.ChannelPrefix);
	}

	[Fact]
	public void Section_without_connection_string_throws()
	{
		Assert.Throws<ArgumentException>(() => new ServiceCollection().AddRedis(Section("""{ "redis": { "keyPrefix": "app:" } }""")));
	}

	[Fact]
	public void Consumer_option_is_not_replaced()
	{
		var own = new RedisOption { ConnectionString = "localhost:6379" };
		var services = new ServiceCollection();
		services.AddSingleton(own);
		services.AddRedis(o => o.ConnectionString = "other:6379");

		using var provider = services.BuildServiceProvider();
		Assert.Same(own, provider.GetRequiredService<RedisOption>());
	}

	[Fact]
	public void WebApplication_builder_overload_registers_redis()
	{
		var builder = WebApplication.CreateBuilder();
		builder.AddRedis(o => o.ConnectionString = "localhost:6379");

		using var app = builder.Build();

		Assert.Equal("localhost:6379", app.Services.GetRequiredService<RedisOption>().ConnectionString);
	}

	[Fact]
	public void WebApplication_section_overload_registers_redis()
	{
		var builder = WebApplication.CreateBuilder();
		builder.AddRedis(Section("""{ "redis": { "connectionString": "localhost:6379" } }"""));

		using var app = builder.Build();

		Assert.NotNull(app.Services.GetRequiredService<RedisOption>().ConnectionString);
	}

	[Fact]
	public async Task External_connection_is_adopted()
	{
		var connection = Substitute.For<IConnectionMultiplexer>();
		var services = new ServiceCollection().AddRedis(o => o.Connection = connection);

		await using var provider = services.BuildServiceProvider();

		// резолв ленивый — соединение строится при первом запросе IRedisConnection
		Assert.Same(connection, provider.GetRequiredService<IRedisConnection>().Multiplexer);
	}

	[Fact]
	public void Default_services_resolve()
	{
		var services = new ServiceCollection().AddRedis(o => o.ConnectionString = "localhost:6379");
		services.AddLogging();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();

		// каждая TryAdd-строка AddRedisCore подтверждается резолвом
		Assert.IsType<RedisCache>(scope.ServiceProvider.GetRequiredService<IRedisCache>());
		Assert.IsType<RedisLock>(scope.ServiceProvider.GetRequiredService<IRedisLock>());
		Assert.IsType<RedisRateLimiter>(scope.ServiceProvider.GetRequiredService<IRedisRateLimiter>());
		Assert.IsType<RedisPublisher>(scope.ServiceProvider.GetRequiredService<IRedisPublisher>());
		Assert.IsType<RedisStreamPublisher>(scope.ServiceProvider.GetRequiredService<IRedisStreamPublisher>());
		Assert.IsType<RedisNative>(scope.ServiceProvider.GetRequiredService<IRedisNative>());
		Assert.NotNull(scope.ServiceProvider.GetRequiredService<RedisSubscriptionRegistry>());
		Assert.NotNull(scope.ServiceProvider.GetRequiredService<RedisStreamHandlerRegistry>());

		var hosted = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToList();
		Assert.Contains(hosted, h => h is RedisSubscriberService);
		Assert.Contains(hosted, h => h is RedisStreamConsumerService);
	}
}
